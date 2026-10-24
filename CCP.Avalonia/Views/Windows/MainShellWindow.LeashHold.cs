// PORTED from WPF ConditioningControlPanel/MainWindow/MainWindow.Leash.cs:308-358 (LeashHoldSwallows,
// OnLeashKeyReleased, AskToCutFromHold) and :46-53 (leash off ends the hold). Hold the panic key five
// seconds on a leashed account: a 5..1 ring (LeashHoldRing), then "Cut the leash?" (LeashCutConfirmWindow).
// Only that window's Yes cuts; a release before five seconds only takes the ring down. The panic itself is
// untouched: the first down still runs HandlePanicKeyPress exactly as before (X11PanicKey.Route). The
// decisions are Core LeashHoldFlow. X11 raw keys do not auto-repeat, so a 250 ms clock feeds the hold
// while the key is down (a repeat by LeashHoldToCut.RepeatGap), and each tick ends the hold unless the key
// is physically down (XQueryKeymap), so a lost key-up never asks. Windows has real repeats and no clock.
// Like WPF, it works with the panic key switched off and under Lockdown (panic always works on a leash).
// ponytail: no tick sound (WPF LeashFx.Tick; no leash sfx on this head yet); the GlobalShortcuts portal
// path reports no key-up, so a native-Wayland-only press cannot hold; the Yes does not close a punish
// window (WPF LeashPunishWindow.CloseNow; lands with leash-punish).
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

        private static bool LeashHoldLeashed
        {
            get { try { return LeashHead.Service?.Snapshot.Me != null; } catch { return false; } }
        }

        private void InitializeLeashHold()
        {
            _leashHoldClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _leashHoldClock.Tick += (_, _) => LeashHoldClockTick();
            Action released = () => Dispatcher.UIThread.Post(LeashHoldReleased);
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
            if (!_leashHold.Held || !LeashHoldLeashed || LeashHoldKeyDown() == false) { LeashHoldReleased(); return; }
            ApplyLeashHold(_leashHold.Down(LeashHoldTime.GetUtcNow().UtcDateTime, true));
        }

        /// <summary>The panic key came up: the hold ends, the ring goes, nothing is asked or cut.</summary>
        internal void LeashHoldReleased()
        {
            _leashHoldClock?.Stop();
            ApplyLeashHold(_leashHold.Up());
        }

        private void ApplyLeashHold(LeashHoldStep step)
        {
            if (step.Dismiss) LeashHoldRing.Dismiss();
            if (step.Ring is { } held) LeashHoldRing.ShowHeld(held);
            if (step.Ask) AskToCutFromHold();
        }

        private void AskToCutFromHold()
        {
            if (!LeashHoldLeashed) return;
            string? holder = null;
            try { holder = LeashHead.Service?.Snapshot.Me?.Holder.Name; } catch { }
            Serilog.Log.Information("Leash: panic key held 5 s, asking to cut");
            LeashCutConfirmWindow.Ask(holder, () => _ = CutLeashAsync(this));   // the gate's cut path (leash-punish renamed CutFromLeashGateAsync)
        }
    }
}
