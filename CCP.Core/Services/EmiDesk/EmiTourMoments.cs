using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// Which EMI moment a tour event fires. Taken from WPF
/// <c>ConditioningControlPanel/Services/EmiDesk/EmiTourNarrator.cs</c> (:40-43, :139, :159-168, :186-187) so a
/// new head narrates from one mapping; the WPF head keeps its own copy (frozen).
/// </summary>
public static class EmiTourMoments
{
    public const string Started = "tourStarted";
    public const string StepPrefix = "tour.";
    public const string StepFallback = "tourStep";

    /// <summary><c>tour.&lt;stepId&gt;</c> when that moment exists, else <c>tourStep</c>; null for a blank id.</summary>
    public static string? Step(string? stepId, IEnumerable<string> knownMoments)
    {
        if (string.IsNullOrWhiteSpace(stepId)) return null;
        var own = StepPrefix + stepId;
        return knownMoments.Contains(own, StringComparer.Ordinal) ? own : StepFallback;
    }

    public static string Ending(bool completed) => completed ? "tourFinished" : "tourSkipped";
}
