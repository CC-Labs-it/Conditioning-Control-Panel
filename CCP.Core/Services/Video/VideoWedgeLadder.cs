using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The decisions of WPF VideoService's self-heal: when to retire the shared LibVLC instance, how
    /// far up the UI-wedge rescue ladder to climb (#765/#766/#767), and the mid-play vout-loss state
    /// machine (#600). The native work each step triggers stays in the head; whether a retire must be
    /// parked behind live siblings is <see cref="VideoSurfaceHealth.ShouldDeferRetire"/>.
    /// </summary>
    internal static class VideoWedgeLadder
    {
        /// <summary>Circuit breaker: each retire roots a whole LibVLC instance in quarantine forever, so
        /// after this many per session no further instance is condemned.</summary>
        internal const int MaxRetiresPerSession = 4;

        /// <summary>A UI thread with no heartbeat for this long is wedged (#687: 22s was too patient;
        /// the freeze reporters had task-killed the process by ~26s).</summary>
        internal const int StallMs = 8000;
        /// <summary>Rung 2: the wedge outlived the off-thread stops - retire the shared instance.</summary>
        internal const int Rung2Ms = 18000;
        /// <summary>Rung 3: give the user their machine back (the escape hatch).</summary>
        internal const int Rung3Ms = 28000;

        internal enum WedgeAction { None, StopPlayers, RetireInstance, EscapeHatch }

        /// <summary>
        /// The next rung for a stall of <paramref name="stallMs"/>, measured from the start of the stall,
        /// given the highest rung already run. <paramref name="rescueAllowed"/> false (pre-roll or
        /// teardown, #750-#753) leaves only the escape hatch, which touches no native VLC state.
        /// <paramref name="retireRung"/> false is the leased ladder (bubble count): retiring the shared
        /// instance mid-game rescues nothing and spends one of the per-session retires.
        /// </summary>
        internal static WedgeAction NextRung(long stallMs, int rungDone, bool rescueAllowed, bool retireRung)
        {
            if (stallMs < StallMs || rungDone >= 3) return WedgeAction.None;
            if (rescueAllowed && rungDone < 1) return WedgeAction.StopPlayers;
            if (rescueAllowed && retireRung && rungDone < 2 && stallMs >= Rung2Ms) return WedgeAction.RetireInstance;
            return stallMs >= Rung3Ms ? WedgeAction.EscapeHatch : WedgeAction.None;
        }

        internal enum RetireVerdict { AlreadyRetired, NotShared, CapReached, Retire }

        /// <summary>Whether a retire of a known suspect goes ahead: never twice, never a fresh instance
        /// a retry already built in its place, never past <see cref="MaxRetiresPerSession"/>.</summary>
        internal static RetireVerdict DecideRetire(bool hasShared, bool suspectIsShared, int retiresSoFar)
        {
            if (!hasShared) return RetireVerdict.AlreadyRetired;
            if (!suspectIsShared) return RetireVerdict.NotShared;
            return retiresSoFar >= MaxRetiresPerSession ? RetireVerdict.CapReached : RetireVerdict.Retire;
        }

        /// <summary>Outcome of a single mid-play vout poll (#600).</summary>
        internal enum VoutMidDecision
        {
            Healthy,               // vout present — reset the loss clock
            DeferToStartWatchdog,  // never seen a vout yet — the start watchdog owns this case
            StartLossClock,        // vout just went absent — begin timing the loss
            WithinGrace,           // absent but not long enough yet to act
            Heal                   // absent past the grace window — retire + replay
        }

        /// <summary>
        /// Pure state transition for the mid-play vout poll. Given the live <paramref name="voutCount"/>
        /// and the persisted loss state, returns the action and the updated loss-clock value.
        /// </summary>
        internal static VoutMidDecision EvaluateVoutMidPlay(
            uint voutCount, bool everSeen, long lostSinceTicks, long nowTicks, int graceMs, out long newLostSinceTicks)
        {
            if (voutCount > 0)
            {
                newLostSinceTicks = 0;   // healthy — reset the loss window
                return VoutMidDecision.Healthy;
            }

            newLostSinceTicks = lostSinceTicks;

            // vout absent. Before it has ever appeared, a missing vout is the start watchdog's job.
            if (!everSeen) return VoutMidDecision.DeferToStartWatchdog;

            if (lostSinceTicks == 0)
            {
                newLostSinceTicks = nowTicks;   // start the loss clock
                return VoutMidDecision.StartLossClock;
            }

            var lostMs = (nowTicks - lostSinceTicks) / TimeSpan.TicksPerMillisecond;
            return lostMs < graceMs ? VoutMidDecision.WithinGrace : VoutMidDecision.Heal;
        }
    }
}
