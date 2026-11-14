// PORTED from ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs: BtnStart_Click (:35),
// BtnStartMenu_Click, MenuStartNormal_Click, StartEngine (:294), StopEngine (:444) and
// UpdateStartButton (:944), for the features Core drives (CoreEngine). The session half of
// BtnStart_Click (stop-session dialog) is ConfirmStopSession (MainShellWindow.SessionRun.cs).
// Remaining lifecycle gaps are listed in docs/avalonia-r72-start-stop-recut.md.
// MenuJumpRightIn_Click / RandomizeAndStart (:132) are ported.

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Avalonia.Views.Overlays;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // Bumped by every Stop, so a Start still waiting on the portal bind (up to 30 s) is dropped
        // if the user stopped or panicked meanwhile.
        private static int _engineGen;
        private System.DateTime? _emiEngineStartedUtc;
        internal System.Action<string, object> EmiMoment = (name, context) => EmiDesk.EmiDeskService.Instance.Fire(name, context);
        internal System.Func<System.DateTime> EmiNow = () => System.DateTime.UtcNow;

        internal void EmiEngineStarted(bool systemInitiated)
        {
            _emiEngineStartedUtc = EmiNow();
            EmiMoment("engineStarted", new { systemInitiated });
        }

        internal void EmiEngineStopped()
        {
            var start = _emiEngineStartedUtc;
            _emiEngineStartedUtc = null;
            int minutes = start == null ? 0 : System.Math.Max(0, (int)(EmiNow() - start.Value).TotalMinutes);
            EmiMoment("engineStopped", new { minutes });
        }

        private void BtnStartMenu_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button b) b.ContextMenu?.Open(b);
        }

        private async void BtnStart_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            FlashStartIgnition(); // WPF MainWindow.StartStop.cs:40
            if (RemoteControllerConnected) return;   // WPF StartStop.cs:43: a controller drives
            if (CoreEngine.IsRunning && RefuseStopUnderLockdown()) return;
            // WPF MainWindow.StartStop.cs:58: a running session asks first; declining keeps everything on.
            if (CoreEngine.IsRunning && App.Sessions?.IsRunning == true)
            {
                Serilog.Log.Information("Start button: asking to stop the running session");
                await ConfirmStopSession("dialog_stop_session_title", "dialog_stop_session_body",
                    "btn_stop_session_yes", "btn_stop_session_no");
                if (!App.Sessions.IsRunning)
                {
                    // WPF StartStop.cs:101-111: a stop by hand inside the window holds the scheduler off.
                    Scheduler.NoteManualStop(CoreSettings.Current, SchedulerNow());
                    StopEngine();
                }
                return;
            }
            if (CoreEngine.IsRunning)
            {
                Serilog.Log.Information("Start button: stopping the engine");
                Scheduler.NoteManualStop(CoreSettings.Current, SchedulerNow());
                App.Achievements?.TrackPanicPressed();
                StopEngine();
            }
            else StartEngineFromUser();
        }

        /// <summary>WPF StartEngineFromUser: only the user's own START ends the post-panic quiet early
        /// (Core PanicAftermath); the scheduler, programs, a remote or the Lockdown keeper never do.</summary>
        internal void StartEngineFromUser()
        {
            Scheduler.NoteManualStart();
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            StartEngine(systemInitiated: false, resumeSession: true);
        }

        private void MenuJumpRightIn_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => RandomizeAndStart();

        /// <summary>WPF StartStop.cs:142 "Jump right in": a fun, non-overwhelming mix with random
        /// pacing, then Start. Refused whole mid-session (it used to re-roll a prescribed mix).
        /// Setters clamp, so the ranges are always safe.</summary>
        internal void RandomizeAndStart(System.Random? rng = null)
        {
            if (RefuseActionIfSessionLocked("jump-right-in")) return;
            rng ??= new System.Random();
            var s = CoreSettings.Current;
            s.FlashEnabled = true;
            s.FlashFrequency = rng.Next(20, 81);
            s.SimultaneousImages = rng.Next(2, 9);
            s.SubliminalEnabled = true;
            s.SubliminalFrequency = rng.Next(3, 13);
            s.SpiralEnabled = rng.Next(2) == 0;
            s.PinkFilterEnabled = rng.Next(2) == 0;
            CoreSettings.Save();
            if (!CoreEngine.IsRunning) StartEngineFromUser();
        }

        private void MenuStartNormal_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!CoreEngine.IsRunning) StartEngineFromUser();
        }

        /// <summary>WPF StartEngine, through the portal wrapper so the panic key is bound first.</summary>
        internal void StartEngine(bool systemInitiated = true, bool resumeSession = false)
        {
            var gen = ++_engineGen;
            StartEffect(() =>
            {
                if (gen != _engineGen || CoreEngine.IsRunning) return;
                // WPF StartStop.cs:345-350: no notice for audio-only or system starts.
                if (!systemInitiated && !CoreSettings.Current.AudioOnlySession) WarnIfFlashLibraryEmpty();
                if (!systemInitiated) App.Achievements?.CheckRelapse();
                // WPF StartEngineFromUser: a resume is not a new run; the parked session comes back after the start.
                var resuming = resumeSession && App.Sessions is { IsRunning: true, IsPaused: true };
                CoreEngine.Start(resumingSession: resuming);
                StartEngineRamp();
                if (resuming) App.Sessions?.ResumeAfterEngineStart();
                PinkRushHost.Start();              // WPF StartEngine :309 App.SkillTree?.Start()
                PinkFilterOverlay.Refresh(this);   // WPF App.Overlay.Start()
                SpiralOverlay.Refresh(this);
                CornerGifOverlay.Refresh(this);
                EmiEngineStarted(systemInitiated); // WPF StartStop.cs:420-421
                StartConditioningTimeTracker();    // WPF StartEngine :423
                UpdateStartButton();
                if (!systemInitiated) _ = MaybePromptMandatoryVideoEnhancement(this, () => CoreEngine.IsRunning,   // WPF StartStop.cs:443
                    () => Platform.WebcamTracker.Instance.IsRunning, () => Named<Tabs.SettingsTabView>("SettingsTab")?.BtnWebcamTracking_Click(null, new()));
            });
        }

        /// <summary>WPF MainWindow.StartStop.cs:365-367: Audio Only forces the existing layered bed.</summary>
        internal void OnEngineStarted()
        {
            var settings = CoreSettings.Current;
            Services.EngineCrashSentinel.Mark(
                $"started {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} | flash {settings.FlashFrequency}/min x{settings.SimultaneousImages}" +
                $" | bubbles {(settings.BubblesEnabled ? "on" : "off")} | video {(settings.MandatoryVideosEnabled ? "on" : "off")}" +
                $" | subliminal {(settings.SubliminalEnabled ? "on" : "off")}");
            if (CoreSettings.Current.AudioOnlySession) Platform.LayeredAudio.Instance?.Start(ignoreMasterToggle: true);
        }

        /// <summary>WPF StopEngine. Saved flags are left alone; <see cref="OnEngineStopped"/> clears the screen.
        /// A running session is paused first, not ended (panic, tray Stop): WPF MainWindow.xaml.cs:1726.</summary>
        internal static void StopEngine()
        {
            _engineGen++;
            App.Sessions?.Pause();
            CoreEngine.Stop();
        }

        /// <summary><see cref="CoreEngine.StoppedHook"/>: the head half of StopEngineCore.</summary>
        internal void OnEngineStopped()
        {
            // Direct Core stops (a remote panic) also freeze the session clock. A session's own Stop
            // has already cleared IsRunning by the time it stops the engine, so this is a no-op there.
            App.Sessions?.Pause();
            Named<Views.Tabs.SettingsTabView>("SettingsTab")?.StopBrowserEnhancement();
            // WPF StartStop.cs:577-580 ExitBrowserFullscreenForTeardown (#952): a topmost fullscreen browser
            // left up by a session's end hides the completion dialog behind it (#4423).
            ExitBrowserPopoutFullscreen();
            Services.EngineCrashSentinel.Clear();
            EmiEngineStopped(); // WPF StartStop.cs:570-579
            Platform.ProgramEngineBridge.RaiseSessionChanged();   // any session end repaints the Programs row
            // WPF StartStop.cs:480-482: Stop stops everything (owner, 7.1.8): the audio player with a playlist
            // goes quiet with the engine. The list stays. StartEngine never touches the player.
            App.StopAudioPlaylist();
            // WPF StartStop.cs:485: hand back anything a Takeover pulse borrowed before the overlays go;
            // also retires the pulse timers so an old one cannot end the next run's bubbles.
            CancelAutonomyPulses();
            Deeper.ScreenShake.Stop();
            // WPF MainWindow.StartStop.cs:533-547: preserve only the standalone bed, release ducking.
            if (!CoreSettings.Current.AudioLayersEnabled) Platform.LayeredAudio.Instance?.Stop();
            Platform.LibVlcAudio.Instance?.ForceUnduck();
            PinkRushHost.Stop();                   // WPF StartStop.cs:505 App.SkillTree?.Stop()
            StopConditioningTimeTracker();         // WPF StartStop.cs:540
            App.StopDesktopOverlays(final: false);
            PopQuizHost.Instance.CloseAll();   // first: drops a queued quiz before cards close; WPF StartStop.cs:521
            LockCardWindow.ForceCloseAll();
            QuizWindow.ForceCloseAll();
            DescentFuseWindow.ForceCloseAll();
            Deeper.EnhancementPlayerWindow.ForceExitFullscreenAll();
            PinkFilterOverlay.Refresh(this);
            UpdateStartButton();
            StopEngineRamp();
            // A paused session keeps its labels, lock and pause button (WPF StopEngine leaves them).
            if (App.Sessions?.IsRunning == true) { SetPauseButton(App.Sessions.IsPaused); OnSessionTick(); return; }
            // WPF OnSessionStopped: the session button and the dials come back on every exit path.
            Named<Tabs.PresetsTabView>("PresetsTab")?.SetSessionButtonLabel(null);
            if (Named<Button>("BtnPauseSession") is { } pause) pause.IsVisible = false;
            RefreshSessionFeatureLock();
        }

        /// <summary>WPF UpdateStartButton: red ■ Stop while running, the accent ▶ Start otherwise.</summary>
        internal void UpdateStartButton(bool force = false)
        {
            if (!force && RemoteControllerConnected) return;   // WPF :941 keeps the remote label
            var running = CoreEngine.IsRunning;
            if (Named<Button>("BtnStart") is { } b)
            {
                if (running) b.Bind(BackgroundProperty, new Binding { Source = new SolidColorBrush(Color.FromRgb(255, 107, 107)) });
                else b.Bind(BackgroundProperty, b.GetResourceObservable("AccentGradientBrush"));
            }
            if (Named<TextBlock>("TxtStartIcon") is { } icon) icon.Text = running ? "■" : "▶";
            Named<TextBlock>("TxtStartLabel")?.Bind(TextBlock.TextProperty,
                (Binding)new StrExtension(running ? "label_stop" : "label_start").ProvideValue(null!));
            RefreshBarStatus();
            if (App.Sessions?.IsRunning == true) OnSessionTick();
            // Cosmetic, and also reached from OnEngineStopped (panic, stop): a throw here must not skip the
            // pause-button / session-label / RefreshSessionFeatureLock steps that follow it there.
            try { ApplyStartHeroState(); }
            catch (System.Exception ex) { Serilog.Log.Debug(ex, "[Shell] start hero state"); }
        }
    }
}
