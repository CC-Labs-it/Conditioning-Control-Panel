using System;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.JustDrop;

namespace ConditioningControlPanel.Services.Haptics
{
    /// <summary>
    /// Haptics for a Just Drop session (handoff-1121/JUSTDROP-WIRING-BRIEF.md). A copy of
    /// <see cref="BackRoomHapticDirector"/>: the page has already decided what a moment feels like
    /// (<c>i_haptic</c>, <c>a_heart</c>, <c>i_breath</c>) and sends one clamped pulse or wave; this
    /// director only decides whether the toy takes it. Nothing plays when access is shut, the master
    /// switch is off or no toy is connected; the page is the LIVE SITE, so it is rate limited again
    /// here with the Back Room's limiter, and the newest accepted pulse replaces the one in play.
    ///
    /// <para>The pulse is a mixer transient, so the user's master intensity and cap apply inside the
    /// mixer and a page can never ask for more than the user set. Panic stops the mixer and closes
    /// the window; the window's close, a process failure, <c>session-exit</c> and
    /// <c>session-complete</c> all run <see cref="Stop"/>.</para>
    /// </summary>
    internal static class JustDropHapticDirector
    {
        private static readonly object Gate = new();
        private static readonly BackRoomHapticDirector.Limiter Limit = new();
        private static CancellationTokenSource? _playing;

        /// <summary>A toy can be driven right now (what <c>caps.haptics</c> reports, plus the master switch).</summary>
        private static bool Ready
            => HapticsAccess.IsOpen
            && App.Settings?.Current?.Haptics is { Enabled: true }
            && App.Haptics is { IsConnected: true };

        public static VibrationMode ModeFor(JustDropBridge.HapticKind kind)
            => kind == JustDropBridge.HapticKind.Wave ? VibrationMode.Wave : VibrationMode.Constant;

        /// <summary>Buttplug commands carry about a second of latency: stretch a short pulse, as the
        /// other directors do, never past the wire's ceiling.</summary>
        public static int DurationFor(int ms, bool buttplug)
            => buttplug ? Math.Min(JustDropBridge.MaxHapticMs, ms * 2) : ms;

        public static void OnHaptic(JustDropBridge.HapticRequest h)
        {
            if (h.Kind == JustDropBridge.HapticKind.Stop) { Stop(); return; }
            var haptics = App.Haptics;
            if (haptics == null || !Ready || h.Intensity <= 0) return;

            CancellationTokenSource cts;
            lock (Gate)
            {
                if (!Limit.Accept(Environment.TickCount64)) return;
                try { _playing?.Cancel(); } catch { }
                cts = _playing = new CancellationTokenSource();
            }
            var ms = DurationFor(h.DurationMs, haptics.IsButtplugProvider);
            App.Logger?.Debug("JustDropHaptics: {Item} {Kind} {Pct}% {Ms}ms", h.Item, h.Kind, (int)(h.Intensity * 100), ms);
            _ = PlayAsync(haptics, h.Intensity, ms, ModeFor(h.Kind), cts);
        }

        private static async System.Threading.Tasks.Task PlayAsync(HapticService haptics, double level, int ms,
            VibrationMode mode, CancellationTokenSource cts)
        {
            try { await haptics.PlayPatternAsync(level, ms, mode, BackRoomHapticDirector.PriorityFor(level), Core.ToyRole.All, cts.Token); }
            catch { /* a toy that dropped mid-pulse is not the drop's problem */ }
            finally
            {
                lock (Gate) { if (_playing == cts) _playing = null; }
                cts.Dispose();
            }
        }

        /// <summary>Stop OUR pulse only. Whatever else drives the toy keeps going.</summary>
        public static void Stop()
        {
            CancellationTokenSource? cts;
            lock (Gate) { cts = _playing; _playing = null; }
            try { cts?.Cancel(); } catch { }
        }
    }
}
