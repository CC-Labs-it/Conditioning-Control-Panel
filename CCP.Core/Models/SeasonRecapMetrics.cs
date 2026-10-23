using System;

namespace ConditioningControlPanel.Models;

/// <summary>WPF SeasonRecapService.PercentileFor: unranked = 0; ranked = top 1..99%.</summary>
public static class SeasonRecapMetrics
{
    public static int PercentileFor(int rank, int total) => rank <= 0 || total <= 0
        ? 0 : Math.Clamp((int)Math.Ceiling((double)rank / total * 100), 1, 99);
}
