namespace ConditioningControlPanel.Services.Fyp.Online;

// Official-note subset of WPF OnlineMeterRules; the meter itself belongs to its own lane.
internal static class OnlineOfficialNoteRules
{
    internal static bool ShowCredit(string? source, bool consent, bool enabled, bool official)
        => consent && enabled && official && source is "online" or "mixed";

    internal static string PlanKey(ScrolllerPlan plan) => plan switch
    {
        ScrolllerPlan.Prime => "online_official_plan_prime",
        ScrolllerPlan.Basic => "online_official_plan_basic",
        _ => "online_official_plan_free",
    };
}
