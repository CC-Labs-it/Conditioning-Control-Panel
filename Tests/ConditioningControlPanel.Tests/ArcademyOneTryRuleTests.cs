using System;
using System.IO;
using ConditioningControlPanel.Services.Arcademy;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ONE TRY A DAY for the Daily Trigger (ccp-bugs #1455) on the credit clock (UTC, ticket
/// 1557537313851056138). The page half is checked by
/// <c>Resources/web/arcademy/games/daily-trigger/smoke/one-try-check.mjs</c>; this is the host half:
/// the pure rule, and a source tripwire that the host settles every leave through it.
/// </summary>
public class ArcademyOneTryRuleTests
{
    [Fact]
    public void ATimedDailyTriggerLeftMidClassSpendsTheDay()
    {
        Assert.True(ArcademyOneTryRule.ClaimsDayOnLeave("daily_trigger", endless: false));
    }

    [Fact]
    public void FreeSwimsAndOtherClassesStillLeaveForNothing()
    {
        Assert.False(ArcademyOneTryRule.ClaimsDayOnLeave("daily_trigger", endless: true));
        Assert.False(ArcademyOneTryRule.ClaimsDayOnLeave("sort", endless: false));
        Assert.False(ArcademyOneTryRule.ClaimsDayOnLeave(null, endless: false));
        Assert.False(ArcademyOneTryRule.ClaimsDayOnLeave("Daily_Trigger", endless: false));
    }

    [Fact]
    public void TheDayIsThePagesSeedDayWhenWellFormed()
    {
        // A window opened at 23:50 UTC keeps playing yesterday's script after midnight: the claim
        // must land on that script's day, the same one class-ended would have claimed.
        var now = new DateTime(2026, 10, 18, 0, 10, 0, DateTimeKind.Utc);
        Assert.Equal("2026-10-17", ArcademyOneTryRule.DayFor("2026-10-17", now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("17/10/2026")]
    [InlineData("2026-13-40")]
    public void AMissingOrMalformedDayFallsBackToTheUtcDate(string? pageDay)
    {
        var now = new DateTime(2026, 10, 17, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal("2026-10-17", ArcademyOneTryRule.DayFor(pageDay, now));
    }

    [Fact]
    public void TheHostSettlesEveryLeaveThroughTheRule()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services",
            "Arcademy", "ArcademyHostService.cs"));

        int left = src.IndexOf("case \"class-left\":", StringComparison.Ordinal);
        Assert.True(left > 0, "class-left case not found");
        int settle = src.IndexOf("SettleLeftClass(\"left\")", left, StringComparison.Ordinal);
        int nextCase = src.IndexOf("case \"", left + 20, StringComparison.Ordinal);
        Assert.True(settle > left && settle < nextCase, "class-left must settle the open class");

        Assert.Contains("SettleLeftClass(\"closed\")", src);
        Assert.Contains("ArcademyOneTryRule.ClaimsDayOnLeave(key, endless)", src);
        Assert.Contains("_meta.TryClaimXpDay(key, day)", src);

        // A graded class is claimed by the farm guard; the leave after it must settle nothing.
        int ended = src.IndexOf("private static void OnClassEnded(JObject o)", StringComparison.Ordinal);
        int clear = src.IndexOf("_openClassKey = null;", ended, StringComparison.Ordinal);
        int body = src.IndexOf("try", ended, StringComparison.Ordinal);
        Assert.True(ended > 0 && clear > ended && clear < body, "class-ended must close the open class first");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
