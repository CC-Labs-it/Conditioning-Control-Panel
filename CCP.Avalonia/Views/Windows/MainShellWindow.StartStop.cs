// PORTED from ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs: BtnStart_Click (:35),
// BtnStartMenu_Click, MenuStartNormal_Click, StartEngine (:294), StopEngine (:444) and
// UpdateStartButton (:944), for the features Core drives (CoreEngine). The session half of
// BtnStart_Click (stop-session dialog) is ConfirmStopSession (MainShellWindow.SessionRun.cs).
// Relapse (TrackPanicPressed/CheckRelapse), the Takeover start/stop rule and the scheduler's manual-stop
// flag are here; ramp in Core CoreIntensityRamp, scheduler in MainShellWindow.Scheduler.cs.
// ponytail: brain-drain start (no BrainDrain service on this head) and the Start hero FX
// (MainWindow.HeroFx.cs) remain: row shell-start-stop names wire-shell-start-stop-b-next.
// MenuJumpRightIn_Click / RandomizeAndStart (:132) are ported.

using System;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
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

        private void BtnStartMenu_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button b) b.ContextMenu?.Open(b);
        }

        private async void BtnStart_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (RemoteControllerConnected) return;   // WPF StartStop.cs:43: a controller drives
            if ((CoreEngine.IsRunning || App.Sessions?.IsRunning == true) && RefuseStopUnderLockdown()) return;
            // WPF MainWindow.StartStop.cs:58: a running session asks first; declining keeps everything on.
            if (App.Sessions?.IsRunning == true)
            {
                Serilog.Log.Information("Start button: asking to stop the running session");
                await ConfirmStopSession("dialog_stop_session_title", "dialog_stop_session_body");
                if (!App.Sessions.IsRunning) ManualStop();
                return;
            }
            if (CoreEngine.IsRunning) { Serilog.Log.Information("Start button: stopping the engine"); ManualStop(); }
            else
            {
                Scheduler.ManuallyStopped = false;   // WPF StartStop.cs:111
                StartEngine();
            }
        }

        /// <summary>WPF StartStop.cs:98-106: the user's own Stop stamps Relapse and holds the schedule off.</summary>
        private void ManualStop()
        {
            App.Achievements?.TrackPanicPressed();
            Scheduler.OnManualToggle(CoreSettings.Current, DateTime.Now, stopping: true);
            StopEngine();
        }

        /// <summary><see cref="CoreEngine.StartedHook"/>: WPF StartEngine's Relapse check (:248, also
        /// SessionEngine TrackSessionStart) and Takeover start (:336), for every engine start.</summary>
        internal void OnEngineStarted()
        {
            App.Achievements?.CheckRelapse();
            Autonomy.Start();   // starts only when AutonomyScheduler.CanStart (entitled, enabled, consented)
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
            if (!CoreEngine.IsRunning) StartEngine();
        }

        private void MenuStartNormal_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!CoreEngine.IsRunning) StartEngine();
        }

        /// <summary>WPF StartEngine, through the portal wrapper so the panic key is bound first.</summary>
        internal void StartEngine()
        {
            var gen = ++_engineGen;
            StartEffect(() =>
            {
                if (gen != _engineGen || CoreEngine.IsRunning) return;
                CoreEngine.Start();
                PinkRushHost.Start();              // WPF StartEngine :309 App.SkillTree?.Start()
                PinkFilterOverlay.Refresh(this);   // WPF App.Overlay.Start()
                SpiralOverlay.Refresh(this);
                StartConditioningTimeTracker();    // WPF StartEngine :423
                UpdateStartButton();
                WarnIfFlashLibraryEmpty();
            });
        }

        /// <summary>WPF StartStop.cs:225: only a deliberate UI start earns this non-blocking notice.
        /// Core/session/remote starts stay silent, and online/mixed or audio-only starts do too.</summary>
        internal void WarnIfFlashLibraryEmpty()
        {
            try
            {
                if (!LocalMediaLibrary.FlashIsEmpty(CoreSettings.Current, CorePaths.EffectiveAssets)) return;
                App.Notifications.Show(
                    string.Format(Loc.Get("msg_no_flash_images"), Loc.Get("nav_door_library"), Loc.Get("tab_assets")),
                    Helpers.NotificationType.Warning, TimeSpan.FromSeconds(18),
                    actionLabel: Loc.Get("btn_open_media_manager"), action: () => ShowTab("assets"),
                    secondaryActionLabel: Loc.Get("btn_get_packs"),
                    secondaryAction: () => Platform.ExternalOpener.Open(DiscordLinks.PackCatalogue));
                Serilog.Log.Information("[empty-state] Flashes started with an empty local image library");
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[empty-state] flash library check failed"); }
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
            Platform.ProgramEngineBridge.RaiseSessionChanged();   // any session end repaints the Programs row
            // WPF StartStop.cs:485: hand back anything a Takeover pulse borrowed before the overlays go;
            // also retires the pulse timers so an old one cannot end the next run's bubbles.
            CancelAutonomyPulses();
            // WPF StartStop.cs:451: Takeover the user armed outlives the engine; anything else stops.
            if (!AutonomyScheduler.CanStart(CoreSettings.Current)) Autonomy.Stop();
            PinkRushHost.Stop();                   // WPF StartStop.cs:505 App.SkillTree?.Stop()
            StopConditioningTimeTracker();         // WPF StartStop.cs:540
            App.StopDesktopOverlays(final: false);
            PopQuizHost.Instance.CloseAll();   // first: drops a queued quiz before cards close; WPF StartStop.cs:521
            LockCardWindow.ForceCloseAll();
            PinkFilterOverlay.Refresh(this);
            UpdateStartButton();
            // A paused session keeps its labels, lock and pause button (WPF StopEngine leaves them).
            if (App.Sessions?.IsRunning == true) { SetPauseButton(App.Sessions.IsPaused); return; }
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
            if (Named<TextBlock>("TxtPresetsStatus") is { } status)
            {
                if (running) status.Bind(TextBlock.TextProperty, (Binding)new StrExtension("label_running").ProvideValue(null!));
                else status.Text = "";
            }
        }
    }
}
