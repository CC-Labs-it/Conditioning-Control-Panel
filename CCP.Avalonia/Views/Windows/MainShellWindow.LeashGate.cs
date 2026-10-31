// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Leash.cs (InitializeLeash gate half,
// LeashBlocksGames, PresentLeashGateFromLauncher, CheckLeashGate, ReadLeashWorld, Show/HideLeashGate,
// PardonLeashAsync, PanicFromLeashGate, the CutRequested lambda; WPF 7.1.5). THE GATE sits in the remote
// overlay's cell one Z step under it and stands up at the next idle moment while a punishment is pending
// (LeashGateRule.ShouldShow, checked every 2 s). The launcher asks LeashBlocksGames before a game tile
// opens (LauncherHost.cs:360-366). Platform/LeashHead seeds both providers.
// The gate's action starts the task on the Core LeashTaskRunner (Platform/LeashTaskHost; WPF StartLeashTask,
// BindLeashRunner, GuardLeashRunner, OnLeashProgress/Completed/Stopped). Not read yet: StartupLadder/Tutorial
// modals; the LeashFx done/denied sounds (leash fx unit).
// The browser card hides while the gate is up (WPF airspace, MainWindow.Leash.cs:184-201: NativeWebView
// is a native host here too, SettingsTabView.axaml:971, and would cover Panic and Cut).
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;
using ILeashTaskRunner = ConditioningControlPanel.Controls.Leash.ILeashTaskRunner;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>True while a leash punishment is due (WPF LeashLocator.Service()?.GateDue != null).</summary>
        internal static Func<bool>? LeashGateDueProvider;

        /// <summary>Brings the leash gate up on the panel (WPF CheckLeashGate after clearing the snooze).</summary>
        internal static Action<MainShellWindow>? PresentLeashGateProvider;

        private LeashGateCard? _leashGate;
        private DateTime _leashSnoozeUntilUtc = DateTime.MinValue;
        private bool _leashHidBrowser;
        private ILeashTaskRunner? _leashRunner;
        private string? _leashUnplayablePid;

        /// <summary>The card on this panel (tests read it).</summary>
        internal LeashGateCard? LeashGate => _leashGate;

        /// <summary>True while a punishment stands between a launcher tile and its game.</summary>
        internal static bool LeashBlocksGames
        {
            get { try { return PresentLeashGateProvider != null && LeashGateDueProvider?.Invoke() == true; } catch { return false; } }
        }

        /// <summary>The launcher's way in: a tile press is a deliberate "I want to play", so the gate comes up now.</summary>
        internal void PresentLeashGateFromLauncher()
        {
            try { PresentLeashGateProvider?.Invoke(this); }
            catch (Exception ex) { Serilog.Log.Debug("Leash gate from launcher failed: {E}", ex.Message); }
        }

        /// <summary>WPF PresentLeashGateFromLauncher: it also ends a panic snooze.</summary>
        internal void PresentLeashGate()
        {
            _leashSnoozeUntilUtc = DateTime.MinValue;
            CheckLeashGate();
        }

        private void InitializeLeashGate()
        {
            try
            {
                if (Named<Border>("RemoteControlOverlay") is not { Parent: Grid host } overlay) return;
                _leashGate = new LeashGateCard();
                Grid.SetRow(_leashGate, Grid.GetRow(overlay));
                Grid.SetRowSpan(_leashGate, Grid.GetRowSpan(overlay));
                Grid.SetColumn(_leashGate, Grid.GetColumn(overlay));
                Grid.SetColumnSpan(_leashGate, Grid.GetColumnSpan(overlay));
                // Under the remote overlay (the leash gate goes before any other gate).
                _leashGate.ZIndex = overlay.ZIndex - 1;
                host.Children.Add(_leashGate);
                _leashGate.StartRequested += StartLeashTask;
                _leashGate.PardonRequested += p => _ = PardonLeashAsync(p);
                _leashGate.PanicRequested += PanicFromLeashGate;
                _leashGate.CutRequested += () => _ = CutLeashAsync();
                _leashGate.LaterRequested += () => { _leashUnplayablePid = null; HideLeashGate(); };

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (_, _) => CheckLeashGate();
                timer.Start();
                Closed += (_, _) => timer.Stop();
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash gate init failed: {E}", ex.Message); }
        }

        internal void CheckLeashGate()
        {
            try
            {
                var svc = LeashHead.Service;
                BindLeashRunner();
                GuardLeashRunner(svc);
                var (due, unplayable) = PickLeashGate(svc);
                if (due != null && LeashGateRule.ShouldShow(ReadLeashWorld(true)))
                    ShowLeashGate(due, svc!.Snapshot.Me?.Pardons ?? 0, unplayable);
                else HideLeashGate();
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash gate check failed: {E}", ex.Message); }
        }

        private LeashGateInputs ReadLeashWorld(bool due) => new(
            Due: due,
            SessionRunning: CoreSession.IsSessionRunning || CoreEngine.IsRunning,
            GameUp: PanicSurfaces.AnyOwnsTheScreen(),
            LockCardOpen: LockCardWindow.IsAnyOpen() || BubbleCountWindow.IsAnyOpen(),
            FullscreenEffect: CoreEngine.Video?.IsPlaying == true,
            ModalUp: LockdownActive || Named<Border>("RemoteControlOverlay")?.IsVisible == true,
            PanelAway: !IsVisible || WindowState == WindowState.Minimized,
            Watching: _leashRunner?.IsRunning == true || DateTime.UtcNow < _leashSnoozeUntilUtc);

        /// <summary>WPF PickLeashGate: a video that just would not play (once, until "Not now"), else the oldest due.</summary>
        private (Punishment? Due, bool Unplayable) PickLeashGate(ILeashService? svc)
        {
            if (svc == null) { _leashUnplayablePid = null; return (null, false); }
            var due = svc.GateDue;   // read first: GateDue is where a sign-out is noticed
            if (_leashUnplayablePid is { } pid)
            {
                var p = svc.Snapshot.Me?.Pending.FirstOrDefault(x => x.Pid == pid);
                if (p != null && svc.IsUnplayable(pid)) return (p, true);
                _leashUnplayablePid = null;
            }
            return (due, false);
        }

        /// <summary>WPF GuardLeashRunner: the task behind the runner went away (pardoned, expired, the leash ended).</summary>
        private void GuardLeashRunner(ILeashService? svc)
        {
            if (_leashRunner is not { IsRunning: true } r) return;
            MyLeash? me = null;
            try { me = svc?.Snapshot.Me; } catch { }
            if (!LeashGateRule.Orphaned(r.RunningPid, r.RunningAid, me)) return;
            LeashTaskHost.EndTask("task gone", this);
        }

        internal void OnLeashTaskEnded() { _leashUnplayablePid = null; HideLeashGate(); }

        private void BindLeashRunner()
        {
            var next = LeashTaskHost.Runner;
            if (ReferenceEquals(next, _leashRunner)) return;
            if (_leashRunner != null)
            {
                _leashRunner.Progress -= OnLeashProgress;
                _leashRunner.Completed -= OnLeashCompleted;
                _leashRunner.Stopped -= OnLeashStopped;
            }
            _leashRunner = next;
            if (_leashRunner != null)
            {
                _leashRunner.Progress += OnLeashProgress;
                _leashRunner.Completed += OnLeashCompleted;
                _leashRunner.Stopped += OnLeashStopped;
            }
        }

        /// <summary>WPF StartLeashTask: the gate's main button.</summary>
        private void StartLeashTask(Punishment p)
        {
            BindLeashRunner();
            bool started = false;
            try { started = _leashRunner?.Start(p) == true; }
            catch (Exception ex) { Serilog.Log.Debug("Leash task start failed: {E}", ex.Message); }
            if (!started)
            {
                var noPhrases = p.Kind == PunishKind.Lines && LockCardScheduler.EnabledPhrases().Count == 0;
                App.Notifications.Show(Loc.Get(noPhrases ? "leash_stop_no_phrases" : "leash_gate_cannot_start"), Helpers.NotificationType.Warning);
                return;
            }
            Serilog.Log.Information("Leash: task {Kind} x{Size} started", p.Kind, p.Size);
            _leashGate?.SetProgress(0, Math.Max(1, LeashUiRules.Pips(p)), running: true);
            CheckLeashGate();
        }

        private void OnLeashProgress(string pid, int done, int total)
            { if (_leashGate?.Pid == pid) _leashGate.SetProgress(done, total, running: done < total); }

        private async void OnLeashCompleted(string pid)
        {
            try
            {
                Serilog.Log.Information("Leash: task {Pid} done", pid);
                if (LeashHead.Service is { } s) await s.CompleteAsync(pid);
                App.Notifications.Show(Loc.Get("leash_gate_done"), Helpers.NotificationType.Success);
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash complete failed: {E}", ex.Message); }
            finally { CheckLeashGate(); }
        }

        /// <summary>WPF OnLeashStopped: say why; the gate comes back with the task still pending. A video that will
        /// not play is skipped on the server when it can be, else left off the gate for a day (said once).</summary>
        private async void OnLeashStopped(LeashTaskStopped s)
        {
            try
            {
                Serilog.Log.Information("Leash: task {Id} stopped ({Reason})", s.Id, s.Reason);
                _leashGate?.StopRunning();
                var svc = LeashHead.Service;
                var me = svc?.Snapshot.Me;
                var kind = me?.Pending.FirstOrDefault(p => p.Pid == s.Id)?.Kind;
                LeashSkipResult? skip = null;
                if (!s.Assignment && s.Reason == LeashTaskStop.Unplayable)
                {
                    skip = svc == null ? LeashSkipResult.Marked : await svc.SkipUnplayableAsync(s.Id);
                    if (skip == LeashSkipResult.Marked) _leashUnplayablePid = s.Id;
                }
                App.Notifications.Show(Loc.GetF(LeashUiRules.StopKey(s, kind, skip), me?.Holder.Name ?? ""), Helpers.NotificationType.Info);
            }
            catch (Exception ex) { Serilog.Log.Debug("Leash stop handling failed: {E}", ex.Message); }
            finally { CheckLeashGate(); }
        }

        /// <summary>WPF LeashOnPanicPress's gate half (LeashTaskHost.OnPanicPress): back in ten minutes.</summary>
        internal void LeashStandBack()
        {
            _leashSnoozeUntilUtc = DateTime.UtcNow + LeashUiRules.PanicSnooze;
            _leashGate?.StopRunning();
            HideLeashGate();
        }

        private void ShowLeashGate(Punishment p, int pardons, bool unplayable)
        {
            if (_leashGate == null) return;
            if (!_leashGate.IsUp && LeashBrowser is { IsVisible: true } b)
            {
                b.IsVisible = false;
                _leashHidBrowser = true;
            }
            _leashGate.Present(p, pardons, unplayable);
        }

        private void HideLeashGate()
        {
            if (_leashGate?.IsUp != true) return;
            _leashGate.Dismiss();
            if (!_leashHidBrowser) return;
            _leashHidBrowser = false;
            // Never hand the browser back over the remote overlay's own blindfold.
            if (Named<Border>("RemoteControlOverlay")?.IsVisible != true && LeashBrowser is { } b) b.IsVisible = true;
        }

        /// <summary>The browser card's native host (WPF SettingsTab.BrowserContainer).</summary>
        internal Control? LeashBrowser => Named<Tabs.SettingsTabView>("SettingsTab")?.BrowserContainer;

        private async System.Threading.Tasks.Task PardonLeashAsync(Punishment p)
        {
            bool ok = false;
            try { if (LeashHead.Service is { } s) ok = await s.PardonAsync(p.Pid); }
            catch (Exception ex) { Serilog.Log.Debug("Leash pardon failed: {E}", ex.Message); }
            if (ok && _leashUnplayablePid == p.Pid) _leashUnplayablePid = null;
            App.Notifications.Show(Loc.Get(ok ? "leash_gate_pardoned" : "leash_gate_no_pardon"), ok ? Helpers.NotificationType.Success : Helpers.NotificationType.Info);
            CheckLeashGate();
        }

        /// <summary>WPF PanicFromLeashGate: the gate stands back ten minutes, then the panic stop pass.
        /// Nothing about the leash can stop or delay a panic, so it skips the global key's switches
        /// (PanicKeyEnabled, Lockdown) the way WPF's button does.</summary>
        private void PanicFromLeashGate()
        {
            _leashSnoozeUntilUtc = DateTime.UtcNow + LeashUiRules.PanicSnooze;
            HideLeashGate();
            PanicSurfaces.StopAll("leash gate", this);
            NoteLocalPanicPause(); // WPF MainWindow.Leash.cs:307 -> local stop-button panic
        }

        /// <summary>WPF CutRequested -> LeashSurfaces.Cut: the gate goes, the cut is said, and the Core
        /// cut runs (its LeashCutSafety steps first, whatever the network does).</summary>
        internal async System.Threading.Tasks.Task CutLeashAsync()
        {
            LeashTaskHost.EndTask("cut", this);   // WPF LeashSurfaces.CutDone -> EndLeashTask
            App.Notifications.Show(Loc.Get("leash_cut_done"), Helpers.NotificationType.Success);
            try { if (LeashHead.Service is { } s) await s.CutAsync(); }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] cut failed (service retries): {E}", ex.Message); }
        }
    }
}
