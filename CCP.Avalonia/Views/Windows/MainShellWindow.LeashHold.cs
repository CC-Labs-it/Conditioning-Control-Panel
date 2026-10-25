// PORTED from WPF ConditioningControlPanel/MainWindow/MainWindow.Leash.cs:308-358 (LeashHoldSwallows,
// OnLeashKeyReleased, AskToCutFromHold) and :46-53 (leash off ends the hold). Hold the panic key five
// seconds on a leashed account: a 5..1 ring (LeashHoldRing), then "Cut the leash?" (LeashCutConfirmWindow).
// Only that window's Yes cuts; a release before five seconds only takes the ring down. The panic itself is
// untouched: the first down still runs HandlePanicKeyPress exactly as before (X11PanicKey.Route). The
// decisions are Core LeashHoldFlow. X11 raw keys do not auto-repeat, so a 250 ms clock feeds the hold
// while the key is down (a repeat by LeashHoldToCut.RepeatGap), and each tick ends the hold unless the key
// is physically down (XQueryKeymap), so a lost key-up never asks. Windows has real repeats and no clock.
// Like WPF, it works with the panic key switched off and under Lockdown (panic always works on a leash).
// The portal supplies Activated/Deactivated instead of physical X11 key state. Its existing effects-only
// binding policy stays intact; a hold that started while bound keeps the session until release. A portal
// hold skips the X11 key check only once that compositor has proven it sends Deactivated; until then a
// portal hold needs the X11 key proven down on every tick: up OR unknown (no X11 listener) ends it, so a
// portal that never reports release never asks. Only a live portal hold keeps the grab (KeepPortalBound).
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Test seam: the clock the hold is timed on.</summary>
        internal static TimeProvider LeashHoldTime = TimeProvider.System;

        /// <summary>Test seam: is the panic key physically down? False ends the hold; null = unknown.</summary>
        internal static Func<bool?> LeashHoldKeyDown = X11PanicKey.IsPanicKeyDown;

        private readonly LeashHoldFlow _leashHold = new();
        private DispatcherTimer? _leashHoldClock;
        private bool _leashHoldFromPortal, _leashHoldPortalUnproven;
        private static bool _portalHoldLive;

        /// <summary>A portal hold is being timed on the leash clock: the portal stays bound until it ends.</summary>
        internal static bool PortalHoldLive => _portalHoldLive;

        private static bool LeashHoldLeashed
        {
            get { try { return LeashHead.Service?.Snapshot.Me != null; } catch { return false; } }
        }

        private void InitializeLeashHold()
        {
            _leashHoldClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _leashHoldClock.Tick += (_, _) => LeashHoldClockTick();
            // XWayland synthesizes releases when native Wayland focus takes over. Only the portal's
            // Deactivated can end a portal hold; its compositor may also have rebound the key.
            Action released = () => Dispatcher.UIThread.Post(() => { if (!_leashHoldFromPortal) LeashHoldReleased(); });
            X11PanicKey.PanicReleased = released;
            Action<int, string?> winDown = (vk, _) => { if (vk != 0 && vk == Win32PanicKey.BoundVirtualKey) Dispatcher.UIThread.Post(LeashHoldPressed); };
            Action<int, string?> winUp = (vk, _) => { if (vk != 0 && vk == Win32PanicKey.BoundVirtualKey) released(); };
            if (OperatingSystem.IsWindows()) { Win32PanicKey.KeyDown += winDown; Win32PanicKey.KeyUp += winUp; }
            Closed += (_, _) =>
            {
                _leashHoldClock.Stop();
                if (X11PanicKey.PanicReleased == released) X11PanicKey.PanicReleased = null;
                if (OperatingSystem.IsWindows()) { Win32PanicKey.KeyDown -= winDown; Win32PanicKey.KeyUp -= winUp; }
                LeashHoldRing.Dismiss();
            };
        }

        /// <summary>A key-down of the panic key (UI thread): starts or continues the hold.</summary>
        internal void LeashHoldPressed()
        {
            ApplyLeashHold(_leashHold.Down(LeashHoldTime.GetUtcNow().UtcDateTime, LeashHoldLeashed));
            if (_leashHold.Held && LeashHoldLeashed && !OperatingSystem.IsWindows()) _leashHoldClock?.Start();
        }

        /// <summary>The held clock (250 ms): the key is still down. Off the leash, the hold ends (WPF :49-53).</summary>
        internal void LeashHoldClockTick()
        {
            var key = _leashHoldFromPortal ? true : LeashHoldKeyDown();
            if (!_leashHold.Held || !LeashHoldLeashed || key == false || (key == null && _leashHoldPortalUnproven)) { LeashHoldReleased(); return; }
            ApplyLeashHold(_leashHold.Down(LeashHoldTime.GetUtcNow().UtcDateTime, true));
        }

        /// <summary>The panic key came up: the hold ends, the ring goes, nothing is asked or cut.</summary>
        internal void LeashHoldReleased()
        {
            _leashHoldFromPortal = _leashHoldPortalUnproven = _portalHoldLive = false;
            _leashHoldClock?.Stop();
            ApplyLeashHold(_leashHold.Up());
        }

        /// <summary>A new portal press (PortalPanicShortcut.Route already dropped repeats): always a panic.
        /// A hold still running here lost its Deactivated, so it ends before the new one starts.</summary>
        internal void OnPortalPanicPressed()
        {
            if (_leashHold.Held) LeashHoldReleased();
            HandlePanicKeyPress(DateTime.Now);
            _leashHoldFromPortal = PortalPanicShortcut.ReportsRelease;
            _leashHoldPortalUnproven = !_leashHoldFromPortal;
            LeashHoldPressed();
            _portalHoldLive = _leashHoldClock?.IsEnabled == true;
        }

        internal void OnPortalPanicReleased() => LeashHoldReleased();

        private void ApplyLeashHold(LeashHoldStep step)
        {
            if (step.Dismiss) LeashHoldRing.Dismiss();
            if (step.Ring is { } held) LeashHoldRing.ShowHeld(held);
            if (step.Tick != null) LeashSound.Tick(LeashHoldTime.GetUtcNow().UtcDateTime);
            if (step.Ask) AskToCutFromHold();
        }

        private void AskToCutFromHold()
        {
            if (!LeashHoldLeashed) return;
            string? holder = null;
            try { holder = LeashHead.Service?.Snapshot.Me?.Holder.Name; } catch { }
            Serilog.Log.Information("Leash: panic key held 5 s, asking to cut");
            LeashCutConfirmWindow.Ask(holder, () => _ = CutLeashAsync());   // EndTask closes the punish window before the cut
        }
    }
}
