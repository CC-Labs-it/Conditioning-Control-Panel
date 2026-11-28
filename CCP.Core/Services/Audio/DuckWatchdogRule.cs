namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>What the duck watchdog does on a tick.</summary>
    public enum DuckWatchdogStep
    {
        /// <summary>Nothing is ducked: the net retires until the next Duck arms a fresh one.</summary>
        Retire,
        /// <summary>The duck's owner is still alive (a video still plays): wait one more period.</summary>
        Rearm,
        /// <summary>A duck with no live owner outlived the period: force the restore.</summary>
        ForceRestore,
    }

    /// <summary>
    /// The 5 minute duck safety net (AudioService.DuckWatchdogTick). ccp-bugs #1762: a floating
    /// video run lasts longer than 5 minutes, and the net force-unducked other apps while the video
    /// still played ("audio ducking stopped mid floating video"). The net now waits another period
    /// while a playing video still holds its duck ref, and keeps firing for a duck whose owner is
    /// gone (a leaked ref, a missing Unduck). Pure, pinned by DuckWatchdogRuleTests.
    /// </summary>
    public static class DuckWatchdogRule
    {
        public static DuckWatchdogStep Decide(bool isDucked, bool ownerAlive)
        {
            if (!isDucked) return DuckWatchdogStep.Retire;
            return ownerAlive ? DuckWatchdogStep.Rearm : DuckWatchdogStep.ForceRestore;
        }
    }
}
