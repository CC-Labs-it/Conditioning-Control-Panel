// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs.
// The client is Core RemoteRelay; the tab (Views/Tabs/RemoteControlTabView.axaml.cs) owns toggle, waiver,
// QR, status, command log and the emote picker. This partial is the SHELL half: the full-window
// "someone is controlling you" overlay (fade in/out, session code + PIN, idle subtitle, session info card,
// big emote picker, End Session), the 2 s command toast, the controller-joined notice and the Start
// button lock while a controller drives, and the session verbs (StartSessionFromRemote & co, :1287-1420).
// The remote end/leave teardown stops a session only when the controller started it (IsSessionRemoteStarted).
// ponytail: still missing here - the directory opt-in chain, tray minimise/restore
// for remote, the taskbar flash on join (no Avalonia API), the RemoteHud pill and the browser
// blindfold (no embedded browser under the overlay on this head).

using System;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The Play door's Available Subjects entry. One ShowTab, as in WPF. The roster
        /// on that page is still filled by App.AvailableSubjects, so this opens an empty tab.</summary>
        private void BtnAvailableSubjects_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("availablesubjects");

        /// <summary>Clock for the toast hold and the fade-out collapse; tests step it.</summary>
        internal static TimeProvider RemoteOverlayTime = TimeProvider.System;
        internal static readonly TimeSpan RemoteToastHold = TimeSpan.FromSeconds(2), RemoteOverlayFadeOut = TimeSpan.FromMilliseconds(200);
        private DispatcherTimer? _remoteOverlayTimer;
        private long? _remoteToastShownAt, _remoteOverlayHidingAt;

        private static bool RemoteControllerConnected => RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected;

        /// <summary>WPF wires these in ChkRemoteControlEnabled_Changed; the relay here is one per process, so
        /// the shell listens from startup and lets go on close.</summary>
        private void InitializeRemoteControlOverlay()
        {
            var r = RemoteControlTabView.Relay.Value;
            EventHandler connected = (_, _) => Dispatcher.UIThread.Post(OnRemoteControllerChanged);
            EventHandler idle = (_, _) => Dispatcher.UIThread.Post(OnRemoteControllerIdleChanged);
            EventHandler ended = (_, _) => Dispatcher.UIThread.Post(OnRemoteSessionEnded);
            EventHandler<string> command = (_, a) => Dispatcher.UIThread.Post(() => OnRemoteCommandReceived(a));
            r.ControllerConnectedChanged += connected;
            r.ControllerIdleChanged += idle;
            r.SessionEnded += ended;
            r.CommandReceived += command;
            Func<string, Session?, string?> sessionVerb = RemoteSessionVerb;
            RemoteCommands.SessionVerb = sessionVerb;
            RemoteCommands.FindSession = FindRemoteSession;
            Action<bool> stopSession = StopRemoteSession;
            RemoteCommands.StopSessionHook = stopSession;
            Closed += (_, _) =>
            {
                if (RemoteCommands.SessionVerb == sessionVerb) (RemoteCommands.SessionVerb, RemoteCommands.FindSession) = (null, null);
                if (RemoteCommands.StopSessionHook == stopSession) RemoteCommands.StopSessionHook = null;
                r.ControllerConnectedChanged -= connected;
                r.ControllerIdleChanged -= idle;
                r.SessionEnded -= ended;
                r.CommandReceived -= command;
                _remoteOverlayTimer?.Stop();
            };
            // WPF fades the overlay in 300 ms and the toast in 200 ms (out: 200 / 300 ms).
            if (Named<Border>("RemoteControlOverlay") is { } o)
                o.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) } };
            if (Named<Border>("RemoteCommandNotification") is { } n)
                n.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(250) } };
        }

        /// <summary>WPF FindSessionByIdCallback (:1029): an available built-in session by id first (:1034), then the rack.</summary>
        private Session? FindRemoteSession(string id) => Session.GetAllSessions().FirstOrDefault(s => s.Id == id && s.IsAvailable)
            ?? Named<PresetsTabView>("PresetsTab")?.FindSession(id);

        /// <summary>WPF _remoteStartedSession: the session object the controller's start_session ran.</summary>
        private Session? _remoteStartedSession;

        /// <summary>WPF IsSessionRemoteStarted (:1280): the running session is the one the controller started.</summary>
        internal bool IsSessionRemoteStarted => _remoteStartedSession != null
            && App.Sessions is { IsRunning: true } r && ReferenceEquals(r.CurrentSession, _remoteStartedSession);

        /// <summary>WPF StopAllRemoteEffects (RemoteControlService.cs:1034): a panic, or a remote end/leave over
        /// the controller's own session, stops it; a session the subject started is not the remote's to end.</summary>
        private void StopRemoteSession(bool force)
        {
            if (force || IsSessionRemoteStarted) RemoteSessionVerb("stop_session", null);
            else if (App.Sessions?.IsRunning == true) Serilog.Log.Information("[RemoteControl] Leaving the subject's own session running: the controller did not start it");
        }

        /// <summary>WPF MainWindow.StartSessionFromRemote / PauseSessionFromRemote / ResumeSessionFromRemote /
        /// StopSessionFromRemote (:1287-1420) and the start_session panic refusal (RemoteControlService.cs:1517).
        /// UI thread; returns the refusal the controller sees, or null.</summary>
        internal string? RemoteSessionVerb(string action, Session? session)
        {
            if (App.Sessions is not { } runner) return RemoteCommands.NotOnThisBuild;
            switch (action)
            {
                case "start_session":
                    if (RemoteCommands.LocalPanicHoldsSession(runner)) return "panic";
                    if (session == null) return "no session";
                    if (runner.IsRunning) runner.Stop(completed: false);   // WPF: StopSession(completed: false) first
                    _remoteStartedSession = session;   // WPF: marked before the start lands
                    StartSession(session);
                    return null;
                case "pause_session":
                    if (!runner.IsRunning || runner.IsPaused) return null;
                    runner.Pause();
                    PinkFilterOverlay.Refresh(this);
                    SpiralOverlay.Refresh(this);
                    SetPauseButton(true);
                    return null;
                case "resume_session":
                    // A session a local panic paused stays paused: say so, not a silent ok (PanicAftermath).
                    if (RemoteCommands.LocalPanicHoldsSession(runner)) return "panic";
                    if (!runner.IsRunning || !runner.IsPaused) return null;
                    StartEffect(() =>   // effects come back, so the portal panic bind comes first (BtnPauseSession_Click)
                    {
                        if (App.Sessions is not { IsPaused: true } r) return;
                        r.Resume();
                        PinkFilterOverlay.Refresh(this);
                        SpiralOverlay.Refresh(this);
                        SetPauseButton(false);
                        OnSessionTick();
                    });
                    return null;
                case "stop_session":   // WPF StopEngineAndSession("RemoteControl")
                    if (runner.IsRunning) runner.Stop();
                    if (CoreEngine.IsRunning) StopEngine();
                    return null;
                default: return RemoteCommands.NotOnThisBuild;
            }
        }

        /// <summary>WPF OnRemoteControllerChanged (:784).</summary>
        internal void OnRemoteControllerChanged()
        {
            var connected = RemoteControllerConnected;
            UpdateStartButtonForRemoteControl(connected);
            if (connected) { ShowRemoteControlOverlay(); NotifyRemoteControllerJoined(); }
            else HideRemoteControlOverlay();
        }

        /// <summary>WPF OnRemoteControllerIdleChanged (:818): orange "may be idle", else the grey default.</summary>
        internal void OnRemoteControllerIdleChanged()
        {
            if (Named<TextBlock>("TxtRemoteOverlaySubtitle") is not { } t) return;
            var idle = RemoteControlTabView.Relay.Value.ControllerIdle;
            t.Bind(TextBlock.TextProperty, new Binding($"[{(idle ? "label_controller_may_be_idle" : "label_someone_else_is_controlling_your_app")}]")
            { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
            t.Foreground = new SolidColorBrush(idle ? Color.FromRgb(0xFF, 0xA5, 0x00) : Color.FromRgb(0xA0, 0xA0, 0xA0));
        }

        /// <summary>WPF OnRemoteSessionEnded (:832), shell half; the tab unticks itself.</summary>
        internal void OnRemoteSessionEnded()
        {
            HideRemoteControlOverlay();
            UpdateStartButtonForRemoteControl(false);
        }

        /// <summary>WPF OnRemoteCommandReceived (:1091): quiet verbs make no toast.</summary>
        internal void OnRemoteCommandReceived(string action)
        {
            if (RemoteCommands.Quiet.Contains(action)) return;
            ShowCommandNotification(action);
        }

        /// <summary>WPF ShowRemoteControlOverlay (:891).</summary>
        private void ShowRemoteControlOverlay()
        {
            var r = RemoteControlTabView.Relay.Value;
            var text = string.IsNullOrEmpty(r.SessionCode) ? "" : $"Session: {string.Join(" ", r.SessionCode.ToCharArray())}";
            if (!string.IsNullOrEmpty(r.ConnectPin)) text += $"  PIN: {r.ConnectPin}";
            if (Named<TextBlock>("TxtOverlaySessionCode") is { } code) code.Text = text;
            // The big picker: the first three slots above End Session, the last two below (WPF LoadSettings).
            var presets = CoreSettings.Current.RemoteEmotePresets;
            if (Named<ItemsControl>("LstEmotePresetsBigTop") is { } top) top.ItemsSource = presets.Take(3).ToList();
            if (Named<ItemsControl>("LstEmotePresetsBigBottom") is { } bottom) bottom.ItemsSource = presets.Skip(3).ToList();
            if (Named<Border>("RemoteControlOverlay") is { } o) { o.IsVisible = true; o.Opacity = 1; }
            _remoteOverlayHidingAt = null;
            _remoteOverlayTimer ??= new DispatcherTimer(RemoteOverlaySlowTick, DispatcherPriority.Background, (_, _) => RemoteOverlayTick());
            _remoteOverlayTimer.Start();
            RemoteOverlayTick();
        }

        /// <summary>WPF's 1 s session-info cadence; 100 ms only while a toast or fade-out is pending (P07).</summary>
        internal static readonly TimeSpan RemoteOverlaySlowTick = TimeSpan.FromSeconds(1), RemoteOverlayFastTick = TimeSpan.FromMilliseconds(100);
        internal TimeSpan? RemoteOverlayInterval => _remoteOverlayTimer?.Interval;

        private void SetRemoteOverlayCadence()
        {
            if (_remoteOverlayTimer is not { } timer) return;
            var want = _remoteToastShownAt != null || _remoteOverlayHidingAt != null ? RemoteOverlayFastTick : RemoteOverlaySlowTick;
            if (timer.Interval != want) timer.Interval = want;
        }

        /// <summary>WPF HideRemoteControlOverlay (:915): fade 200 ms, then collapse; the toast stops too.</summary>
        private void HideRemoteControlOverlay()
        {
            if (Named<Border>("RemoteControlOverlay") is not { IsVisible: true } o) return;
            o.Opacity = 0;
            _remoteToastShownAt = null;
            _remoteOverlayHidingAt = RemoteOverlayTime.GetTimestamp();
            SetRemoteOverlayCadence();
        }

        /// <summary>The overlay's one timer (runs only while the overlay is up): WPF's 1 s session info
        /// refresh (:1057), the 2 s toast hold (:1240) and the fade-out collapse.</summary>
        internal void RemoteOverlayTick()
        {
            if (_remoteOverlayHidingAt is { } h && RemoteOverlayTime.GetElapsedTime(h) >= RemoteOverlayFadeOut)
            {
                _remoteOverlayHidingAt = null;
                _remoteOverlayTimer?.Stop();
                if (Named<Border>("RemoteControlOverlay") is { } o) o.IsVisible = false;
                if (Named<Border>("RemoteCommandNotification") is { } n0) n0.Opacity = 0;
                return;
            }
            if (_remoteToastShownAt is { } t && RemoteOverlayTime.GetElapsedTime(t) >= RemoteToastHold)
            {
                _remoteToastShownAt = null;
                if (Named<Border>("RemoteCommandNotification") is { } n) n.Opacity = 0;
            }
            SetRemoteOverlayCadence();
            UpdateRemoteSessionInfo();
        }

        /// <summary>WPF UpdateRemoteSessionInfo (:1057): the idle card, or name / mm:ss of mm:ss (+ PAUSED) / phase.</summary>
        private void UpdateRemoteSessionInfo()
        {
            var running = App.Sessions is { IsRunning: true, CurrentSession: not null };
            if (Named<Control>("RemoteSessionIdle") is { } idle) idle.IsVisible = !running;
            if (Named<Control>("RemoteSessionActive") is { } active) active.IsVisible = running;
            if (!running) return;
            var s = App.Sessions!;
            var session = s.CurrentSession!;
            if (Named<TextBlock>("TxtRemoteSessionName") is { } name) name.Text = $"{session.Icon} {session.GetModeAwareName()}";
            var total = TimeSpan.FromMinutes(session.DurationMinutes);
            if (Named<TextBlock>("TxtRemoteSessionTime") is { } time) time.Text = $"{s.Elapsed:mm\\:ss} / {total:mm\\:ss}{(s.IsPaused ? "  ⏸ PAUSED" : "")}";
            var i = s.CurrentPhaseIndex;
            if (Named<TextBlock>("TxtRemoteSessionPhase") is { } phase)
                phase.Text = session.Phases != null && i >= 0 && i < session.Phases.Count ? session.GetModeAwarePhaseName(session.Phases[i]) : "";
        }

        /// <summary>WPF ShowCommandNotification (:1231): the label for 2 s.</summary>
        private void ShowCommandNotification(string action)
        {
            if (Named<TextBlock>("TxtRemoteCommand") is { } t)
                t.Text = RemoteCommands.LabelKeys.TryGetValue(action, out var k) ? Loc.Get(k) : action.Replace("_", " ");
            if (Named<Border>("RemoteCommandNotification") is { } n) n.Opacity = 1;
            _remoteToastShownAt = RemoteOverlayTime.GetTimestamp();
            SetRemoteOverlayCadence();
        }

        /// <summary>WPF NotifyRemoteControllerJoined (:1481): the tray balloon (here the OS notification).</summary>
        private static void NotifyRemoteControllerJoined()
        {
            try { Platform.OsNotifications.Show(Loc.Get("title_remote_controller_joined"), Loc.Get("msg_remote_controller_joined")); }
            catch (Exception ex) { Serilog.Log.Debug("Remote controller notice failed: {Error}", ex.Message); }
        }

        /// <summary>WPF UpdateStartButtonForRemoteControl (StartStop.cs:992): disabled, green, 🎮 REMOTE CONNECTED.</summary>
        internal void UpdateStartButtonForRemoteControl(bool connected)
        {
            if (Named<Button>("BtnStart") is not { } b) return;
            b.IsEnabled = !connected;
            if (!connected) { UpdateStartButton(force: true); return; }
            b.Bind(BackgroundProperty, new Binding { Source = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88)) });
            if (Named<TextBlock>("TxtStartIcon") is { } icon) icon.Text = "\U0001F3AE";
            Named<TextBlock>("TxtStartLabel")?.Bind(TextBlock.TextProperty,
                (Binding)new Localization.StrExtension("label_remote_connected").ProvideValue(null!));
        }

        /// <summary>WPF BtnEndRemoteSession_Click (:1255): stop the relay; the tab unticks on SessionEnded.</summary>
        private async void BtnEndRemoteSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await RemoteControlTabView.Relay.Value.StopAsync();
            HideRemoteControlOverlay();
            UpdateStartButtonForRemoteControl(false);
        }

        private async void BtnEmotePresetBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button { Tag: Models.EmotePreset p } && !string.IsNullOrWhiteSpace(p.Text))
                await RemoteControlTabView.SendEmoteAndReportAsync(p.Text, p.Icon ?? "", "preset", Named<TextBlock>("TxtEmoteStatusBig"));
        }

        private async void BtnEmoteCustomSendBig_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => await SendBigCustomEmote();

        private async void TxtEmoteCustomBig_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await SendBigCustomEmote();
        }

        private System.Threading.Tasks.Task SendBigCustomEmote() =>
            Named<TextBox>("TxtEmoteCustomBig") is { } box && Named<TextBlock>("TxtEmoteStatusBig") is { } status
                ? RemoteControlTabView.SendCustomEmoteAsync(box, status) : System.Threading.Tasks.Task.CompletedTask;
    }
}
