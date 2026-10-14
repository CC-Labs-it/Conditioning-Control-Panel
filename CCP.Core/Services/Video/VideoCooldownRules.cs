using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The timing rules of WPF VideoService that hold something off for a while: the post-poisoning
    /// cooldown (#766), the stale-trigger escape (#1135) and the grace pause's timer re-phasing (#735).
    /// Pure, so they are tested without LibVLC or a dispatcher.
    /// </summary>
    internal static class VideoCooldownRules
    {
        // ---- post-poisoning cooldown (#766) ----

        /// <summary>After a native Stop() wedges, optional consumers (bubble count) skip a turn for this
        /// long. The mandatory pipeline never checks it: skipping a mandatory video is a regression.</summary>
        internal const int NativePoisonCooldownMs = 60000;

        /// <summary>Milliseconds left of the cooldown armed at <paramref name="armedTicks"/>
        /// (0 = never armed), 0 when clear.</summary>
        internal static long PoisonCooldownRemainingMs(long armedTicks, long nowTicks)
        {
            if (armedTicks == 0) return 0;
            var elapsed = (nowTicks - armedTicks) / TimeSpan.TicksPerMillisecond;
            return elapsed >= NativePoisonCooldownMs ? 0 : NativePoisonCooldownMs - elapsed;
        }

        // ---- the trigger guard (#1135) ----

        /// <summary>How long a trigger may sit between claiming the in-progress guard and reaching
        /// PlayVideo before the next trigger declares it dead and takes over. Generously past the real
        /// path (an off-thread pick, which decrypts a whole pack clip, plus the 800ms freeze delay) and
        /// far short of the InteractionQueue's 5-minute stuck detector.</summary>
        internal static readonly TimeSpan TriggerStallCeiling = TimeSpan.FromSeconds(45);

        internal enum TriggerGuardDecision
        {
            /// <summary>No trigger is in flight; claim the guard and go.</summary>
            Proceed,
            /// <summary>A trigger really is mid-flight; this one is a genuine overlap and is dropped.</summary>
            Skip,
            /// <summary>The in-flight trigger is older than anything real can be: it died on the way
            /// to PlayVideo and latched the guard. Clear it and let this trigger through.</summary>
            ClearStaleAndProceed
        }

        /// <summary>The in-progress guard with its escape hatch: without it the guard could latch for
        /// good and silently eat every later video (#1135 "video bubbles do nothing").</summary>
        internal static TriggerGuardDecision EvaluateTriggerGuard(bool triggerInProgress, TimeSpan sinceTriggerStarted)
        {
            if (!triggerInProgress) return TriggerGuardDecision.Proceed;
            return sinceTriggerStarted < TriggerStallCeiling
                ? TriggerGuardDecision.Skip
                : TriggerGuardDecision.ClearStaleAndProceed;
        }

        // ---- grace pause timers (#735); the decision itself is MandatoryVideoScheduler.EvaluateGrace ----

        /// <summary>Floor for a re-armed guard timer, so rounding can never re-arm one at ~0ms.</summary>
        internal static readonly TimeSpan MinReArmInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>Whether the attention spawner's tick may spawn. The playing flag stays true through a
        /// grace pause by design, so spawning then would drop targets on the Resume card where they
        /// expire unclicked: a guaranteed failed check and a forced replay.</summary>
        internal static bool ShouldSpawnTick(bool videoPlaying, bool gracePaused)
            => videoPlaying && !gracePaused;

        /// <summary>True once the grace window has fully elapsed and the video must resume itself.</summary>
        internal static bool ShouldAutoResume(double elapsedSeconds, int windowSeconds)
            => elapsedSeconds >= windowSeconds;

        /// <summary>
        /// Time left on a guard timer armed at <paramref name="armedUtc"/> for <paramref name="interval"/>,
        /// so a 60s pause does not make the duration guillotine or the max-length cap fire 60s early.
        /// <see cref="TimeSpan.Zero"/> for a timer that was never armed, never less than
        /// <see cref="MinReArmInterval"/> otherwise.
        /// </summary>
        internal static TimeSpan RemainingTimerInterval(DateTime armedUtc, TimeSpan interval, DateTime nowUtc)
        {
            if (armedUtc == default || interval <= TimeSpan.Zero) return TimeSpan.Zero;
            var remaining = interval - (nowUtc - armedUtc);
            return remaining < MinReArmInterval ? MinReArmInterval : remaining;
        }
    }
}
