// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Leash.cs (InitializeLeash gate half,
// LeashBlocksGames, PresentLeashGateFromLauncher, CheckLeashGate, ReadLeashWorld, Show/HideLeashGate,
// PardonLeashAsync, PanicFromLeashGate, the CutRequested lambda; WPF 7.1.5). THE GATE sits in the remote
// overlay's cell one Z step under it and stands up at the next idle moment while a punishment is pending
// (LeashGateRule.ShouldShow, checked every 2 s). The launcher asks LeashBlocksGames before a game tile
// opens (LauncherHost.cs:360-366). Platform/LeashHead seeds both providers.
// ponytail: the task runner and the punishment video window (WPF LeashTaskRunner/AppLeashTaskHost +
// LeashPunishWindow, WebView2) are lane leash-punish: until then the gate's action says it cannot start,
// as WPF does when its runner refuses. Not read yet: StartupLadder/Tutorial modals and the browser hide
// (WPF airspace; this head's browser is not a native child).
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

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
                _leashGate.StartRequested += _ => App.Notifications.Show(Loc.Get("leash_gate_cannot_start"), Helpers.NotificationType.Warning);
                _leashGate.PardonRequested += p => _ = PardonLeashAsync(p);
                _leashGate.PanicRequested += PanicFromLeashGate;
                _leashGate.CutRequested += () => _ = CutFromLeashGateAsync();
                _leashGate.LaterRequested += HideLeashGate;

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
                var due = svc?.GateDue;
                if (due != null && LeashGateRule.ShouldShow(ReadLeashWorld(true)))
                    _leashGate?.Present(due, svc!.Snapshot.Me?.Pardons ?? 0);
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
            Watching: DateTime.UtcNow < _leashSnoozeUntilUtc);

        private void HideLeashGate()
        {
            if (_leashGate?.IsUp == true) _leashGate.Dismiss();
        }

        private async System.Threading.Tasks.Task PardonLeashAsync(Punishment p)
        {
            bool ok = false;
            try { if (LeashHead.Service is { } s) ok = await s.PardonAsync(p.Pid); }
            catch (Exception ex) { Serilog.Log.Debug("Leash pardon failed: {E}", ex.Message); }
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
        }

        /// <summary>WPF CutRequested -> LeashSurfaces.Cut: the gate goes, the cut is said, and the Core
        /// cut runs (its LeashCutSafety steps first, whatever the network does).</summary>
        private async System.Threading.Tasks.Task CutFromLeashGateAsync()
        {
            HideLeashGate();
            App.Notifications.Show(Loc.Get("leash_cut_done"), Helpers.NotificationType.Success);
            try { if (LeashHead.Service is { } s) await s.CutAsync(); }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] cut failed (service retries): {E}", ex.Message); }
        }
    }
}
