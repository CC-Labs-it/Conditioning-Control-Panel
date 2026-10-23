using System;
using System.Globalization;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>
/// ONE TRY A DAY for the Daily Trigger (ccp-bugs #1455). The day's word is the same for everyone
/// and a retake replays the identical board, so a try LEFT mid-class (a double Esc, a panic close)
/// already saw the rows it typed. Before this rule only <c>class-ended</c> claimed the day's XP, so
/// leaving and coming back made the next run a paid first run, as often as the player liked.
///
/// <para>The host now claims the day's XP slot (<see cref="ArcademyMetaStore.TryClaimXpDay"/>) when
/// a timed Daily Trigger class is left without a <c>class-ended</c>. The claim is on the SAME UTC
/// day the page seeded the class with (the credit clock, ticket 1557537313851056138), never the
/// local date. Free swims and every other class keep "leaving costs nothing".</para>
///
/// <para>Pure: no WPF, no <see cref="App"/>, so it is exercised standalone.</para>
/// </summary>
public static class ArcademyOneTryRule
{
    public const string DailyTriggerKey = "daily_trigger";

    /// <summary>True when leaving this class without a grade still spends the day's try.</summary>
    public static bool ClaimsDayOnLeave(string? gameKey, bool endless) =>
        !endless && string.Equals(gameKey, DailyTriggerKey, StringComparison.Ordinal);

    /// <summary>The UTC day a class belongs to: the page's <c>dayUtc</c> (its seed's day, which
    /// can be yesterday's for a window opened before UTC midnight) when well formed, else today's
    /// UTC date. A missing or malformed field must never skip the claim.</summary>
    public static string DayFor(string? pageDayUtc, DateTime utcNow)
    {
        var day = (pageDayUtc ?? "").Trim();
        if (DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            return day;
        return utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
