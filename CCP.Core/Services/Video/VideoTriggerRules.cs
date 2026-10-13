namespace ConditioningControlPanel.Services
{
    /// <summary>What a fresh TriggerVideo call should do about the in-progress guard.</summary>
    internal enum TriggerGuardDecision
    {
        /// <summary>Nothing in flight - claim the guard and go.</summary>
        Proceed,
        /// <summary>A trigger really is mid-flight; this one is a genuine overlap and is dropped.</summary>
        Skip,
        /// <summary>The in-flight trigger is older than anything real can be: it died on the way
        /// to PlayVideo and latched the guard. Clear it and let this trigger through.</summary>
        ClearStaleAndProceed
    }

    /// <summary>
    /// The pure rules of VideoService's trigger path: the in-progress guard with its stale-trigger
    /// escape (#1135) and the descent's video-request cancellation (ccp-bugs #1201). In Core so
    /// every head's video service decides overlaps and cancellations the same way, and so they
    /// are unit-testable without LibVLC.
    /// </summary>
    internal static class VideoTriggerRules
    {
        /// <summary>How long a trigger may sit between claiming the in-progress guard and
        /// reaching PlayVideo before the next trigger declares it dead and takes over. Generously
        /// past the real path (an off-thread clip selection, which for a content-pack clip decrypts
        /// the whole file, plus the 800ms freeze delay) and far short of the InteractionQueue's
        /// 5-minute stuck detector, which is what used to be the only way out.</summary>
        internal static readonly TimeSpan TriggerStallCeiling = TimeSpan.FromSeconds(45);

        /// <summary>
        /// Pure decision for the in-progress guard. The escape exists because the guard could
        /// latch forever and silently eat every later video: the #1135 "video bubbles do
        /// nothing" report.
        /// </summary>
        internal static TriggerGuardDecision EvaluateTriggerGuard(bool triggerInProgress, TimeSpan sinceTriggerStarted)
        {
            if (!triggerInProgress) return TriggerGuardDecision.Proceed;
            return sinceTriggerStarted < TriggerStallCeiling
                ? TriggerGuardDecision.Skip
                : TriggerGuardDecision.ClearStaleAndProceed;
        }

        /// <summary>
        /// The rule behind the cancellation. A request is abandoned only when it carries a token
        /// AND a cancel has swept through that token, so a video requested AFTER the cancel - the
        /// next run's, or the user's own - still plays.
        /// </summary>
        internal static bool ShouldAbandonVideoRequest(int requestToken, int cancelledThrough)
            => requestToken != 0 && requestToken <= cancelledThrough;
    }
}
