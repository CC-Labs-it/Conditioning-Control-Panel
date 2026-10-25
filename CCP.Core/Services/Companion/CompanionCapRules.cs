using System;
using System.Collections.Generic;
using System.Globalization;
namespace ConditioningControlPanel.Services.Companion;
/// <summary>The conversation cap subset of WPF ConversionRules.</summary>
public static class CompanionCapRules
{
    public const string KeyCompanionCap = "companion_cap";

    /// <summary>The companion's daily message caps per tier (brief owner decisions).</summary>
    public const int CompanionCapFree = 100, CompanionCapBasic = 1000, CompanionCapPrime = 2000;

    /// <summary>The tier the companion cap row offers after <paramref name="tierNow"/> (0 free,
    /// 1 Basic, 2 Prime), or null when there is nothing above it: Prime gets no row.</summary>
    public static int? CapRowOffers(int tierNow) => tierNow >= 2 ? null : tierNow == 1 ? 2 : 1;

    public static string DayStamp(DateTime local) => local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static bool Owed(IReadOnlyDictionary<string, string> stamps, string day)
    => !stamps.TryGetValue(KeyCompanionCap, out var last) || last != day;
    public static string PlanUrl(int tier) => tier >= 2
    ? "https://www.patreon.com/checkout/CodeBambi?rid=27681505"
    : "https://www.patreon.com/checkout/CodeBambi?rid=27604786";
}
