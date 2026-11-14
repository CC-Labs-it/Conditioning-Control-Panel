// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs.
// The client is Core RemoteRelay; the tab (Views/Tabs/RemoteControlTabView.axaml.cs) owns toggle, waiver,
// QR, status, command log and the emote picker. This partial is the SHELL half: the full-window
// "someone is controlling you" overlay (fade in/out, session code + PIN, idle subtitle, session info card,
// big emote picker, End Session), the 2 s command toast, the controller-joined notice and the Start
// button lock while a controller drives.
// ponytail: still missing here - remote-driven session start, the directory opt-in chain, tray minimise
// for remote and remote
// browser video playback and reveal.

using System;
using System.Linq;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.RemoteHud;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF MainWindow.RemoteControl.cs:1407-1425: end the run, then stop the engine.
        /// Invalidate a start waiting on the Linux portal too, so it cannot undo the stop.</summary>
        internal static void StopSessionFromRemote()
        {
            _engineGen++;
            // WPF SessionEngine.cs:384 and MainWindow.StartStop.cs:547 force all ducks off on stop.
            if (App.Sessions?.IsRunning == true || CoreEngine.IsRunning)
            {
                try { Platform.LibVlcAudio.Instance?.ForceUnduck(); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Remote stop: force unduck failed"); }
            }
            if (App.Sessions is { IsRunning: true } runner) runner.Stop(completed: false);
            if (CoreEngine.IsRunning) StopEngine();
        }

        /// <summary>The Play door's Available Subjects entry. One ShowTab, as in WPF. The roster
        /// on that page is still filled by App.AvailableSubjects, so this opens an empty tab.</summary>
        private void BtnAvailableSubjects_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("availablesubjects");

        internal static Action<Window> RemoteJoinAttention = Platform.RemoteJoinAttention.Flash;
        /// <summary>Clock for the toast hold and the fade-out collapse; tests step it.</summary>
        internal static TimeProvider RemoteOverlayTime = TimeProvider.System;
        internal static readonly TimeSpan RemoteToastHold = TimeSpan.FromSeconds(2), RemoteOverlayFadeOut = TimeSpan.FromMilliseconds(200);
        private DispatcherTimer? _remoteOverlayTimer;
        private System.Threading.CancellationTokenSource? _remoteMoreLifetime, _remoteMoreConfirmation;
        private long? _remoteToastShownAt, _remoteOverlayHidingAt;

        private static bool RemoteControllerConnected => RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected;

        internal static MainShellWindow? RemoteCommandOwner;

        /// <summary>WPF wires these in ChkRemoteControlEnabled_Changed; the relay here is one per process, so
        /// the shell listens from startup and lets go on close.</summary>
        private void InitializeRemoteControlOverlay()
        {
            RemoteCommandOwner = this;
            Action refreshOverlays = () =>
            {
                Overlays.PinkFilterOverlay.Refresh(this);
                Overlays.SpiralOverlay.Refresh(this);
            };
            RemoteCommands.RefreshOverlays = refreshOverlays;
            Action stopSession = StopSessionFromRemote;   // the one remote session stop (trigger_panic calls it too)
            RemoteCommands.StopSession = stopSession;
            var r = RemoteControlTabView.Relay.Value;
            r.SessionsProvider = () => ConditioningControlPanel.Models.Session.GetAllSessions()
                .Concat(Named<Tabs.PresetsTabView>("PresetsTab")?.RemoteSessionManager?.CustomSessions ?? Enumerable.Empty<ConditioningControlPanel.Models.Session>());
            r.SessionRunnerProvider = () => App.Sessions;
            string? ResumeRemoteSession(Func<bool> stillCurrent)
            {
                if (LocalPanicHoldsSession) return "panic";
                ResumeSessionFromRemote(stillCurrent);
                return null;
            }
            r.ResumeSession = ResumeRemoteSession;
            EventHandler connected = (_, _) =>
            {
                var joined = r.ControllerConnected;
                var stopGeneration = RemoteCommands.OverlayStopGeneration;
                bool restore = r.IsActive && !r.ControllerConnected && CoreSettings.Current.StopEffectsOnRemoteDisconnect;
                Dispatcher.UIThread.Post(() =>
                {
                    // A queued join cannot undo a pause, stop or panic that happened afterward.
                    if (joined && r.IsActive && r.ControllerConnected && stopGeneration == RemoteCommands.OverlayStopGeneration)
                        RemoteCommands.EnsureOverlayRunning();
                    OnRemoteControllerChanged();
                    if (restore) ShowFromTray();
                });
            };
            EventHandler idle = (_, _) => Dispatcher.UIThread.Post(OnRemoteControllerIdleChanged);
            EventHandler ended = (_, _) => Dispatcher.UIThread.Post(OnRemoteSessionEnded);
            EventHandler<string> command = (_, a) => Dispatcher.UIThread.Post(() => OnRemoteCommandReceived(a));
            r.ControllerConnectedChanged += connected;
            r.ControllerIdleChanged += idle;
            r.SessionEnded += ended;
            r.CommandReceived += command;
            Closed += (_, _) =>
            {
                r.SessionsProvider = null;
                r.SessionRunnerProvider = null;
                if (r.ResumeSession == ResumeRemoteSession) r.ResumeSession = null;
                r.ControllerConnectedChanged -= connected;
                r.ControllerIdleChanged -= idle;
                r.SessionEnded -= ended;
                r.CommandReceived -= command;
                if (ReferenceEquals(RemoteCommandOwner, this)) RemoteCommandOwner = null;
                _remoteOverlayTimer?.Stop();
                CancelRemoteMoreConfirmation();
                if (RemoteCommands.RefreshOverlays == refreshOverlays) RemoteCommands.RefreshOverlays = null;
                if (RemoteCommands.StopSession == stopSession) RemoteCommands.StopSession = null;
                DisposeRemoteHud();   // WPF OnClosing: an unowned visible window would hold the app open
            };
            // WPF fades the overlay in 300 ms and the toast in 200 ms (out: 200 / 300 ms).
            if (Named<Border>("RemoteControlOverlay") is { } o)
                o.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) } };
            if (Named<Border>("RemoteCommandNotification") is { } n)
                n.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(250) } };
        }

        /// <summary>WPF OnRemoteControllerChanged (:784).</summary>
        internal void OnRemoteControllerChanged()
        {
            var connected = RemoteControllerConnected;
            UpdateStartButtonForRemoteControl(connected);
            if (connected) { ShowRemoteControlOverlay(); NotifyRemoteControllerJoined(); EnsureRemoteHud(); }
            else
            {
                HideRemoteControlOverlay();
                RemoteHudPill?.Refresh();   // WPF: the pill hides itself when nobody holds the remote
                // WPF RemoteControlService.cs:1189: opt-in disconnect stops an idle overlay service.
                if (CoreSettings.Current.StopEffectsOnRemoteDisconnect && !CoreEngine.IsRunning)
                {
                    Overlays.SpiralOverlay.Stop(); Overlays.PinkFilterOverlay.Stop();
                }
            }
        }

        /// <summary>WPF MainWindow.RemoteHud.cs: the subject's More / Easy / Stop pill.</summary>
        internal RemoteHudWindow? RemoteHudPill { get; private set; }

        private void EnsureRemoteHud()
        {
            try
            {
                RemoteHudPill ??= new RemoteHudWindow(() => RemoteControlTabView.Relay.Value, StopFromRemoteHud, () => this);
                RemoteHudPill.Refresh();
            }
            catch (Exception ex) { Serilog.Log.Warning("RemoteHud: could not show: {E}", ex.Message); }
        }

        private void DisposeRemoteHud()
        {
            var hud = RemoteHudPill;
            RemoteHudPill = null;
            hud?.Dispose();
        }

        /// <summary>WPF StopFromRemoteHud: the local half is exactly what the panic key would do now; with the
        /// panic key off only the leash safety, else nothing local (the signal still stops the remote's effects).</summary>
        internal void StopFromRemoteHud()
        {
            if (RemoteHudRules.LocalPanicAllowed(CoreSettings.Current.PanicKeyEnabled)) HandlePanicKeyPress(DateTime.Now);
            else if (LeashHoldLeashed) PanicKeyLeashOnly();   // WPF LeashOnPanicPress(panicRuns: false); else the signal only
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
            DisposeRemoteHud();   // WPF OnRemoteSessionEnded
            // WPF RemoteControlService.cs:1045: session cleanup leaves the wearer's engine running.
            if (!CoreEngine.IsRunning) { Overlays.SpiralOverlay.Stop(); Overlays.PinkFilterOverlay.Stop(); }
            HideRemoteControlOverlay();
            UpdateStartButtonForRemoteControl(false);
            Platform.FriendsHead.Service?.LeaveActivity(ConditioningControlPanel.Services.Friends.PresenceActivity.Remote);   // WPF RemoteControlService.cs:410
            ShowFromTray();
        }

        /// <summary>WPF OnRemoteCommandReceived (:1091): quiet verbs make no toast.</summary>
        internal void OnRemoteCommandReceived(string action)
        {
            RemoteHudPill?.NoteAction(action);
            if (action == "trigger_panic") ShowFromTray();
            if (RemoteCommands.Quiet.Contains(action)) return;
            ShowCommandNotification(action);
        }

        /// <summary>WPF ShowRemoteControlOverlay (:891).</summary>
        private void ShowRemoteControlOverlay()
        {
            var r = RemoteControlTabView.Relay.Value;
            CancelRemoteMoreConfirmation();
            if (LeashBrowser is { } browser) browser.IsVisible = false; // native web view airspace, WPF :904
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
        internal void HideRemoteControlOverlay()
        {
            CancelRemoteMoreConfirmation();
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
                // WPF MainWindow.Leash.cs:185-201: the gate owns the browser until it goes; a tour that is
                // still up keeps it hidden too (#4384). One rule: RestoreBrowser (MainShellWindow.BrowserCover.cs).
                RestoreBrowser();
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
        private void NotifyRemoteControllerJoined()
        {
            try { Platform.OsNotifications.Show(Loc.Get("title_remote_controller_joined"), Loc.Get("msg_remote_controller_joined")); }
            catch (Exception ex) { Serilog.Log.Debug("Remote controller notice failed: {Error}", ex.Message); }
            if (WindowState == WindowState.Minimized || !IsVisible)
                try { RemoteJoinAttention(this); }
                catch (Exception ex) { Serilog.Log.Debug("Remote controller attention failed: {Error}", ex.Message); }
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
            ApplyStartHeroState();
        }

        /// <summary>WPF BtnEndRemoteSession_Click (:1255): stop the relay; the tab unticks on SessionEnded.</summary>
        private async void BtnEndRemoteSession_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await RemoteControlTabView.Relay.Value.StopAsync();
            HideRemoteControlOverlay();
            UpdateStartButtonForRemoteControl(false);
        }

        private void CancelRemoteMoreConfirmation()
        {
            _remoteMoreLifetime?.Cancel();
            _remoteMoreLifetime?.Dispose();
            _remoteMoreLifetime = null;
            _remoteMoreConfirmation?.Cancel();
            _remoteMoreConfirmation?.Dispose();
            _remoteMoreConfirmation = null;
            if (Named<TextBlock>("TxtRemoteMoreStatus") is { } status) status.Text = "";
        }

        private System.Threading.CancellationToken RestartRemoteMoreConfirmation()
        {
            _remoteMoreConfirmation?.Cancel();
            _remoteMoreConfirmation?.Dispose();
            _remoteMoreConfirmation = new System.Threading.CancellationTokenSource();
            return _remoteMoreConfirmation.Token;
        }

        // The overlay covers the remote tab while connected; More remains available here.
        private async void BtnRemoteMoreOverlay_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!RemoteControllerConnected || Named<TextBlock>("TxtRemoteMoreStatus") is not { } status) return;
            _remoteMoreLifetime ??= new System.Threading.CancellationTokenSource();
            await RemoteControlTabView.SendMoreAndReportAsync(status, _remoteMoreLifetime.Token, RestartRemoteMoreConfirmation);
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
