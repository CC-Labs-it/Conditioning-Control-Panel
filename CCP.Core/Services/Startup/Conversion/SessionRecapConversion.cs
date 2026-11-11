namespace ConditioningControlPanel.Services.Startup.Conversion;

/// <summary>Conversion decisions shared by the heads (WPF 831827339).</summary>
public static class SessionRecapConversion
{
    /// <summary>Only a completed, live session end for an account without a tier.</summary>
    public static bool SessionTasteLine(bool completed, bool liveEnd, bool premium)
        => completed && liveEnd && !premium;
}
