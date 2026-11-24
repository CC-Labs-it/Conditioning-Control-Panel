using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1737: a finished lockdown of 20 minutes or more also counts as one session for Daily
/// and Weekly Devotion, and a preset session played inside that lockdown is not counted twice.
/// </summary>
public class LockdownSessionCreditTests
{
    private static readonly DateTime End = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(20)]
    [InlineData(45)]
    public void A_lockdown_of_twenty_minutes_or_more_credits_a_session(double minutes) =>
        Assert.True(new LockdownSessionCredit().LockdownEarnsSession(TimeSpan.FromMinutes(minutes), End));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(19.99)]
    public void A_lockdown_under_twenty_minutes_credits_nothing(double minutes) =>
        Assert.False(new LockdownSessionCredit().LockdownEarnsSession(TimeSpan.FromMinutes(minutes), End));

    [Fact]
    public void A_preset_session_inside_the_lockdown_is_counted_once()
    {
        var credit = new LockdownSessionCredit();
        var served = TimeSpan.FromMinutes(40);
        int sessions = 0;

        // The preset finishes 25 minutes into the lockdown and credits its own session.
        credit.SessionCredited(End - served + TimeSpan.FromMinutes(25));
        sessions++;

        if (credit.LockdownEarnsSession(served, End)) sessions++;

        Assert.Equal(1, sessions);
    }

    [Fact]
    public void A_preset_session_before_the_lockdown_does_not_cancel_its_credit()
    {
        var credit = new LockdownSessionCredit();
        var served = TimeSpan.FromMinutes(30);
        credit.SessionCredited(End - served - TimeSpan.FromSeconds(1));

        Assert.True(credit.LockdownEarnsSession(served, End));
    }

    [Fact]
    public void A_session_inside_one_lockdown_does_not_cancel_the_next_lockdown()
    {
        var credit = new LockdownSessionCredit();
        var served = TimeSpan.FromMinutes(30);
        var firstEnd = End - TimeSpan.FromHours(2);
        credit.SessionCredited(firstEnd - TimeSpan.FromMinutes(10));

        Assert.False(credit.LockdownEarnsSession(served, firstEnd));
        Assert.True(credit.LockdownEarnsSession(served, End));
    }

    /// <summary>
    /// QuestService needs a dispatcher and a progress file, so the wiring is checked in the source:
    /// the session path records its credit and the lockdown path asks before crediting a session.
    /// </summary>
    [Fact]
    public void QuestService_wires_both_paths_through_the_guard()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services",
            "Progression", "QuestService.cs"));

        var session = Member(src, "public void TrackSessionCompleted()");
        Assert.Contains("_lockdownSession.SessionCredited(", session);
        Assert.Contains("UpdateQuestProgress(QuestCategory.Session, 1);", session);

        var lockdown = Member(src, "public void TrackLockdownCompleted(TimeSpan served)");
        Assert.Contains("if (!counts) return;", lockdown);
        Assert.Contains("UpdateQuestProgress(QuestCategory.Lockdown, 1);", lockdown);
        Assert.Contains("_lockdownSession.LockdownEarnsSession(served,", lockdown);
        Assert.Contains("UpdateQuestProgress(QuestCategory.Session, 1);", lockdown);
        Assert.True(lockdown.IndexOf("if (!counts) return;", StringComparison.Ordinal)
                    < lockdown.IndexOf("QuestCategory.Session", StringComparison.Ordinal),
            "the session credit must sit after the 20-minute check");
    }

    private static string Member(string src, string signature)
    {
        var start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "missing " + signature);
        var open = src.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException("unbalanced braces after " + signature);
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
