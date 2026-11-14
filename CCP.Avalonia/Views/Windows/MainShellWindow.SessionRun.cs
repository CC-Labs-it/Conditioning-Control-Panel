// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Presets.cs: BtnStartSession_Click (:1560),
// StartSession (:1586), OnSessionLogReady + ShowSessionSummaryWhenClear (:1687, :1743),
// OnSessionProgressUpdated (:1838), OnSessionStarted (:1891), OnSessionStopped (:1936) and
// BtnStopSession_Click (:1983); plus the stop-session branch of BtnStart_Click (MainWindow.StartStop.cs:58).
// The head owns one Core SessionRunner (App.Sessions), which runs only the ported subset - flash,
// subliminal, bouncing text, lock cards and mandatory videos (docs/avalonia-decisions.html).
// BtnPauseSession_Click (:2021) too.
// ponytail: remaining gaps include remote gates and punch-card/Bark/profile-sync hooks.
// Video teardown is synchronous (MandatoryVideoOverlay.CloseAll); playback gets WPF's 3s recap wait.

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF BtnStartSession_Click: Stop while a session runs, else confirm and start.</summary>
        internal async void BtnStartSession_Click(Session? selected)
        {
            if (App.Sessions is not { } runner) return;
            if (runner.IsRunning)
            {
                await ConfirmStopSession("title_stop_session_confirm", "msg_stop_session_body",
                    "btn_yes_stop_session", "btn_keep_going");
                return;
            }
            if (selected is null || !selected.IsAvailable) return;

            // WPF's text here is not localised (MainWindow.Presets.cs:1572); kept verbatim.
            var dialog = new MessageDialog($"🌅 Start {selected.GetModeAwareName()}?",
                $"Duration: {selected.DurationMinutes} minutes\n\n" +
                "Your current settings will be temporarily replaced.\n" +
                "They will be restored when the session ends." +
                "\n\nReady to begin?", showCancel: true, cancelText: "Not yet");
            // WPF uses fixed English copy; render its play marker with the head's Fluent icon.
            var startText = LocalizationManager.Instance.GetEnglish("btn_start_session");
            var yes = dialog.FindControl<Button>("BtnOk")!;
            yes.Content = new IconLabel { Text = startText ?? "Start Session", Kind = startText == null ? IconKind.Play : null };
            yes.IsDefault = false;
            if (await dialog.ShowDialogSafe<bool?>(this) == true) StartSession(selected);
        }

        /// <summary>WPF StartSession: the engine start (through the portal wrapper, so the panic key is
        /// bound first) and the runner start in one step - SessionRunner.Start starts CoreEngine itself.</summary>
        internal void StartSession(Session session)
        {
            var gen = ++_engineGen;
            StartEffect(() =>
            {
                if (gen != _engineGen || App.Sessions is not { IsRunning: false } runner) return;
                try
                {
                    Named<Tabs.PresetsTabView>("PresetsTab")?.ApplyCornerGifOption(session);   // WPF StartSession :1588
                    // WPF Presets.cs:1616-1622 -> StartStop.cs:345-350: before session overrides.
                    if (!CoreEngine.IsRunning && !RemoteControllerConnected && !CoreSettings.Current.AudioOnlySession)
                        WarnIfFlashLibraryEmpty();
                    App.Achievements?.CheckRelapse();
                    StartEngineRamp(); // WPF Presets.cs:1618: capture user values before session overrides.
                    _suppressSessionSummaryUntil = DateTime.MinValue;
                    bool engineWasRunning = CoreEngine.IsRunning;
                    runner.Start(session);
                    if (!engineWasRunning && CoreEngine.IsRunning) EmiEngineStarted(false); // WPF Presets.cs:1619
                    AnnounceProgramSessionStarted(); // WPF Presets.cs:2013
                    Platform.FriendsHead.Service?.EnterActivity(ConditioningControlPanel.Services.Friends.PresenceActivity.Session); // WPF SessionEngine.cs:247
                    Platform.ProgramEngineBridge.RaiseSessionChanged();   // the Programs session row (WPF OnSessionStarted)
                    PinkRushHost.Start();          // WPF: a session starts through StartEngine -> SkillTree.Start()
                    StartConditioningTimeTracker();   // ... and StartEngine :423
                    PinkFilterOverlay.Refresh(this);
                    SpiralOverlay.Refresh(this);
                    Log.Information("Started session: {Name} ({Difficulty}, +{XP} XP)", session.Name, session.Difficulty, session.BonusXP);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to start session");
                    _ = MessageDialog.ShowAsync(this, Loc.Get("title_error"), Loc.GetF("msg_failed_to_start_session", ex.Message));
                }
                OnSessionTick();   // WPF OnSessionStarted: labels + the feature lock at once, not a second later
            });
        }

        /// <summary>WPF BtnStopSession_Click / the BtnStart_Click session branch: same body, own keys.</summary>
        internal async Task ConfirmStopSession(string titleKey, string bodyKey, string yesKey, string noKey)
        {
            if (App.Sessions is not { IsRunning: true, CurrentSession: { } session } runner) return;
            if (LockdownActive)   // WPF MainWindow.Presets.cs:1993
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_lockdown"), Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_end_a_se"));
                return;
            }
            var elapsed = runner.Elapsed;
            var remaining = runner.Remaining;
            var potentialXP = (int)Math.Round(session.BonusXP * SessionXp.Multiplier(CoreSettings.Current.PlayerLevel));
            // WPF MainWindow.Presets.cs:2098 and MainWindow.StartStop.cs:85 supply distinct labels.
            // ShowStyledDialog (MainWindow.Presets.cs:695-813) sets no default/cancel
            // and does not assign initial button focus. Enter/Space must not confirm on open.
            var mainStop = bodyKey == "dialog_stop_session_body";
            var penalty = runner.XPPenalty;
            var penaltyText = mainStop
                ? (penalty > 0 ? Loc.GetF("dialog_stop_session_penalty", penalty, Math.Max(0, potentialXP - penalty)) : "")
                : (runner.PauseCount > 0 ? Loc.GetF("msg_plus_pause_penalty_0", penalty) : "");
            var dialog = new MessageDialog(Loc.Get(titleKey),
                Loc.GetF(bodyKey, session.Icon ?? "", mainStop ? session.Name : session.GetModeAwareName(),
                    $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}",
                    $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}",
                    potentialXP, penaltyText),
                showCancel: true, okText: Loc.Get(yesKey), cancelText: Loc.Get(noKey));
            var yes = dialog.FindControl<Button>("BtnOk")!;
            yes.IsDefault = false;
            // WPF ShowStyledDialog (MainWindow.Presets.cs:772-806) puts Yes before No.
            var no = dialog.FindControl<Button>("BtnCancel")!;
            Grid.SetColumn(yes, 1);
            Grid.SetColumn(no, 3);
            yes.Margin = new global::Avalonia.Thickness(0, 0, 10, 0);
            no.Margin = default;
            var confirmed = await dialog.ShowDialogSafe<bool?>(this) == true;
            if (!confirmed || !ReferenceEquals(runner.CurrentSession, session)) return;
            Log.Information("Session stop confirmed by the user");
            runner.Stop(completed: false);
            App.Achievements?.TrackPanicPressed(); // WPF SessionEngine.cs:477-478, every confirmed early stop
            Platform.AccountSeed.Sync?.Nudge("session end");   // explicit end: the runner no longer counts as running
        }

        internal Action<Action> ResumeEffect = StartEffect;

        /// <summary>WPF BtnPauseSession_Click: resume at once; a pause asks first (it costs 100 XP)
        /// unless SkipPauseXpWarning. Lockdown refuses the pause, never the resume (LockdownPauseRule).</summary>
        private async void BtnPauseSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (App.Sessions is not { IsRunning: true } runner) return;
            if (ConditioningControlPanel.Services.LockdownPauseRule.RefusesPauseButton(LockdownActive, runner.IsPaused))
            {
                await Dialogs.MessageDialog.ShowAsync(this, Loc.Get("title_lockdown"), Loc.Get("msg_you_are_in_lockdown_mode_nyou_cannot_pause_du"));
                return;
            }
            if (runner.IsPaused)
            {
                // WPF BtnPauseSession_Click: the user's own Resume ends the post-panic quiet (PanicAftermath).
                ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
                var gen = _engineGen;
                ResumeEffect(() =>   // effects come back, so the portal panic bind comes first
                {
                    if (gen != _engineGen) return;
                    if (App.Sessions is not { IsPaused: true } r) return;
                    if (SessionClockSafety.ResumeStartsEngine(CoreEngine.IsRunning, RemoteControllerConnected)) StartEngineFromUser();
                    else r.Resume();
                    PinkFilterOverlay.Refresh(this);
                    SpiralOverlay.Refresh(this);
                    SetPauseButton(false);
                    OnSessionTick();
                });
                return;
            }
            if (!CoreSettings.Current.SkipPauseXpWarning)
            {
                var dialog = new MessageDialog(Loc.Get("title_pause_session_confirm"),
                    Loc.GetF("msg_pause_session_body", runner.XPPenalty, runner.XPPenalty + SessionXp.PausePenalty),
                    showCancel: true, okText: Loc.Get("btn_yes_pause"), cancelText: Loc.Get("btn_keep_going"));
                var checkbox = dialog.FindControl<CheckBox>("DontAskAgain")!;
                checkbox.Content = new TextBlock { Text = Loc.Get("chk_dont_ask_again") };
                checkbox.IsVisible = true;
                dialog.FindControl<Button>("BtnOk")!.IsDefault = false;
                var confirmed = await dialog.ShowDialogSafe<bool?>(this) == true;
                if (!confirmed || !runner.IsRunning) return;
                if (checkbox.IsChecked == true)
                {
                    CoreSettings.Current.SkipPauseXpWarning = true;
                    CoreSettings.Save();
                }
            }
            runner.Pause();
            // WPF SessionEngine.cs:530 -> OverlayService.Stop:471: pause drops sustained holds too.
            SpiralOverlay.Stop();
            PinkFilterOverlay.Stop();
            PinkFilterOverlay.Refresh(this);   // WPF App.Overlay.Stop()
            SpiralOverlay.Refresh(this);
            SetPauseButton(true);
            OnSessionTick();
        }

        // WPF MainWindow.RemoteControl.cs:1341,1385. Voice bypasses the button's XP dialog.
        internal void PauseSessionFromRemote()
        {
            if (App.Sessions is not { IsRunning: true, IsPaused: false } runner) return;
            runner.Pause();
            // WPF SessionEngine.cs:536 App.Overlay.Stop(): a pause drops voice holds, as the button does above.
            SpiralOverlay.Stop();
            PinkFilterOverlay.Stop();
            PinkFilterOverlay.Refresh(this);
            SpiralOverlay.Refresh(this);
            SetPauseButton(true);
        }

        private ConditioningControlPanel.Services.Safety.PanicAftermath.PanicPause? _panicPause;
        private SessionRunner? _panicPauseRunner;

        // WPF MainWindow.xaml.cs:1898 and MainWindow.RemoteControl.cs:1364.
        internal void NoteLocalPanicPause()
        {
            var runner = App.Sessions;
            _panicPauseRunner = runner;
            _panicPause = ConditioningControlPanel.Services.Safety.PanicAftermath.MarkPause(
                runner?.CurrentSession, runner?.RunNumber ?? 0,
                runner?.IsRunning == true, runner?.IsPaused == true, runner?.PauseCount ?? 0);
        }

        // WPF MainWindow.RemoteControl.cs:1373: only this run's specific local-panic pause.
        internal bool LocalPanicHoldsSession =>
            App.Sessions is { IsRunning: true } runner && ReferenceEquals(runner, _panicPauseRunner)
            && ConditioningControlPanel.Services.Safety.PanicAftermath.RemoteResumeRefused(
                _panicPause, runner.CurrentSession, runner.RunNumber, runner.IsPaused, runner.PauseCount);

        internal void ResumeSessionFromRemote() => ResumeSessionFromRemote(() => true);

        internal void ResumeSessionFromRemote(Func<bool> stillCurrent)
        {
            // WPF :1392: a remote or voice resume cannot undo a local panic's pause.
            if (LocalPanicHoldsSession) return;
            if (App.Sessions is not { IsRunning: true, IsPaused: true }) return;
            StartEffect(() =>
            {
                if (!stillCurrent() || LocalPanicHoldsSession
                    || App.Sessions is not { IsRunning: true, IsPaused: true } runner) return;
                runner.Resume();
                PinkFilterOverlay.Refresh(this);
                SpiralOverlay.Refresh(this);
                SetPauseButton(false);
                OnSessionTick();
            });
        }

        /// <summary>WPF TxtPauseIcon + BtnPauseSession.ToolTip: "\u25B6" + resume while paused, else "\u23F8" + the count.</summary>
        internal void SetPauseButton(bool paused)
        {
            if (Named<Button>("BtnPauseSession") is not { } b) return;
            b.IsVisible = true;
            if (Named<TextBlock>("TxtPauseIcon") is { } icon) icon.Text = paused ? "\u25B6" : "\u23F8";
            Named<Tabs.ProgramsTabView>("ProgramsTab")?.RefreshLiveSessionProgress();
            var count = App.Sessions?.PauseCount ?? 0;
            b.Bind(ToolTip.TipProperty, paused || count == 0
                ? (Binding)new StrExtension(paused ? "tooltip_resume_session" : "tooltip_pause_session_100_xp_penalty_per_pause").ProvideValue(null!)
                : new Binding { Source = Loc.GetF("tooltip_pause_session_100_xp_penalty_per_pause_npause", count) });
            RefreshBarStatus();
        }

        /// <summary>WPF OnSessionProgressUpdated (+ OnSessionStarted's red button): the lock heartbeat
        /// and both clock labels. Runs on the UI thread (the runner ticks through CoreDispatch).</summary>
        private (bool, double?)? _spiralTickKey;

        internal void OnSessionTick()
        {
            RefreshBarStatus();
            RefreshSessionFeatureLock(force: false);
            Named<Tabs.ProgramsTabView>("ProgramsTab")?.RefreshLiveSessionProgress();
            if (App.Sessions is not { IsRunning: true, CurrentSession: { } session } runner) return;
            // WPF Session/SessionEngine.cs:799,814: ramps reclaim opacity after a voice command.
            PinkFilterOverlay.ApplySessionOpacity(runner.PinkOpacity);
            var voiceSpiralChanged = SpiralOverlay.ApplySessionOpacity(runner.SpiralOpacity);
            PinkFilterOverlay.Refresh(this);   // the delayed start and the ramp (SessionRunner.PinkOpacity)
            // The same for SessionRunner.SpiralOpacity (SessionEngine.cs:731, 823), only when the start or the
            // ramp moved it: a Refresh enumerates screens and probes the file. A landing decode refreshes itself.
            var spiralKey = (CoreSettings.Current.SpiralEnabled, runner.SpiralOpacity);
            if (voiceSpiralChanged || spiralKey != _spiralTickKey) { _spiralTickKey = spiralKey; SpiralOverlay.Refresh(this); }
            if (Named<Button>("BtnPauseSession") is { IsVisible: false }) SetPauseButton(false);   // WPF OnSessionStarted
            SetPauseButton(runner.IsPaused);
            var pauseMark = SessionClockSafety.PauseMark(runner.IsPaused, Loc.Get("label_paused"));
            var remaining = runner.Remaining;
            var showCountdown = CoreSettings.Current.ShowSessionCountdown != false;
            Named<Tabs.PresetsTabView>("PresetsTab")?.SetSessionButtonLabel(SessionClockLabel.StopButton(
                remaining, showCountdown, Loc.Get("btn_stop_session_0_1"), Loc.Get("btn_stop_session_2"), pauseMark));

            if (!CoreEngine.IsRunning) return;
            var mName = session.GetModeAwareName();
            var nameMax = showCountdown ? 14 : 22;
            var name = mName.Length > nameMax ? mName.Substring(0, nameMax - 3) + "..." : mName;
            if (Named<Button>("BtnStart") is { } b)
                b.Bind(BackgroundProperty, new Binding { Source = new SolidColorBrush(Color.FromRgb(220, 53, 69)) });
            if (Named<TextBlock>("TxtStartIcon") is { } icon) icon.Text = "⏹";
            Named<TextBlock>("TxtStartLabel")?.Bind(TextBlock.TextProperty, new Binding
            {
                Source = SessionClockLabel.StartButton(name, remaining, showCountdown, pauseMark, Loc.Get("label_0_1_2_3"))
            });
        }

        /// <summary>WPF RefreshBarStatus: repaint from the session clock, without saving settings.</summary>
        private void RefreshBarStatus() => RefreshBarStatus(Named<TextBlock>("TxtPresetsStatus"));

        internal static void RefreshBarStatus(TextBlock? status)
        {
            if (status is null) return;
            status.IsVisible = true;
            if (App.Sessions is not { IsRunning: true, CurrentSession: { } session } runner)
            {
                status.Text = CoreEngine.IsRunning ? Loc.Get("label_running") : "";
                return;
            }
            status.Text = SessionClockLabel.BarStatus(session.GetModeAwareName(), runner.Elapsed,
                runner.Remaining, openEnded: session.DurationMinutes <= 0,
                showCountdown: CoreSettings.Current.ShowSessionCountdown,
                pauseIndicator: runner.IsPaused ? " [" + Loc.Get("label_paused") + "]" : "",
                Loc.Get("label_session_bar_left"), Loc.Get("label_session_bar_elapsed"));
        }

        /// <summary>WPF OnSessionLogReady + ShowSessionSummaryWhenClear: the recap opens by itself,
        /// completed or ended early. Raised on the runner's thread; hops to the UI thread.</summary>
        internal SessionCompleteWindow? _liveSessionRecap;

        /// <summary>Set by <see cref="EndProgramSessionQuietly"/>; the next recap is skipped once.</summary>
        internal DateTime _suppressSessionSummaryUntil;

        internal Func<TimeSpan, Task> SessionSummaryDelay = delay => Task.Delay(delay);

        internal void OnSessionLogReady(object? sender, SessionLogReadyEventArgs e)
        {
            if (_windowClosed || _exitRequested) return;
            if (DateTime.UtcNow < _suppressSessionSummaryUntil) { _suppressSessionSummaryUntil = DateTime.MinValue; return; }
            var log = e.Log;
            Dispatcher.UIThread.Post(async () =>
            {
                if (_windowClosed || _exitRequested) return;
                // WPF MainWindow.Presets.cs:1709: the run that just ended is a receipt now; after the
                // suppressed-summary return above, so a quiet stop stays quiet.
                RefreshTakeawayShelf();
                try
                {
                    await SessionSummaryPresentation.WaitForVideoAsync(
                        () => CoreEngine.Video?.IsPlaying == true, SessionSummaryDelay,
                        () => _windowClosed || _exitRequested);
                    if (_windowClosed || _exitRequested) return;
                    var dialog = new SessionCompleteWindow(log);
                    // WPF MainWindow.Presets.cs:1790 (#1303): under a lock card, pop quiz or Bubble
                    // Count cover the recap opens passive - non-modal, not activated - and only one.
                    // WPF MainWindow.Presets.cs:1784: recheck playback after waiting; timeout stays passive.
                    var passive = SessionSummaryPresentation.Decide(CoreEngine.Video?.IsPlaying == true, LockCardWindow.IsAnyOpen(),
                        PopQuizWindow.IsAnyOpen(), BubbleCountWindow.IsAnyOpen()) == SessionSummaryPresentation.Mode.Passive;
                    dialog.Topmost = !passive;   // WPF MainWindow.Presets.cs:1799
                    if (passive)
                    {
                        _liveSessionRecap?.Close();
                        _liveSessionRecap = dialog;
                        dialog.Closed += (_, _) => { if (ReferenceEquals(_liveSessionRecap, dialog)) _liveSessionRecap = null; };
                        dialog.ShowActivated = false;
                        if (IsVisible) dialog.Show(this); else dialog.Show();
                    }
                    else
                    {
                        dialog.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
                        {
                            if (!dialog.IsVisible) return;
                            dialog.Topmost = false;
                            dialog.Activate();
                        }, DispatcherPriority.Render);
                        if (IsVisible) _ = dialog.ShowDialogSafe(this);
                        else dialog.Show();   // shell in the tray: an owned modal needs a visible owner
                    }
                }
                catch (Exception ex) { Log.Error(ex, "Failed to show post-session log dialog"); }
            }, DispatcherPriority.Background);
        }
    }
}
