using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The way out of a Lockdown (<c>Services/LockdownExitRule.cs</c>): the secret phrase, the clock,
/// the premium veil over a running lockdown, and a recovery file that cannot be read.
/// Night bug hunt 2026-10-22.
/// </summary>
public class LockdownExitRuleTests
{
    // ============================ the phrase ============================

    [Theory]
    [InlineData("let me out")]
    [InlineData("LET ME OUT")]
    [InlineData("Let me out")]
    [InlineData("  let me out  ")]
    [InlineData("let  me   out")]                 // a doubled space
    [InlineData("Let me out.")]                   // touch keyboard double-space period
    [InlineData("let me out!")]
    [InlineData("let me out. ")]
    [InlineData("ｌｅｔ　ｍｅ　ｏｕｔ")]  // full-width IME
    [InlineData("let me out")]          // no-break spaces
    [InlineData("let\tme out")]
    public void Phrase_TheRightWords_AlwaysOpen(string typed)
        => Assert.True(LockdownExitRule.PhraseMatches(typed), $"'{typed}' should open the lockdown");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("letmeout")]
    [InlineData("let me")]
    [InlineData("let me out now")]
    [InlineData("please let me out")]
    [InlineData("let me outt")]
    [InlineData(".let me out")]
    [InlineData("let me in")]
    public void Phrase_OtherWords_NeverOpen(string? typed)
        => Assert.False(LockdownExitRule.PhraseMatches(typed), $"'{typed}' must not open the lockdown");

    [Fact]
    public void Phrase_IsTheOneTheBookTeaches()
        => Assert.Equal("let me out", LockdownExitRule.Phrase);

    // ============================ the clock ============================

    [Fact]
    public void Clock_CountsDownInUtc()
    {
        var start = new DateTime(2026, 10, 25, 0, 58, 0, DateTimeKind.Utc);
        var five = TimeSpan.FromMinutes(5);
        Assert.Equal(five, LockdownExitRule.Remaining(start, five, start));
        Assert.Equal(TimeSpan.FromMinutes(2), LockdownExitRule.Remaining(start, five, start.AddMinutes(3)));
        Assert.Equal(TimeSpan.Zero, LockdownExitRule.Remaining(start, five, start.AddMinutes(5)));
        Assert.Equal(TimeSpan.Zero, LockdownExitRule.Remaining(start, five, start.AddHours(9)));   // slept past the end
    }

    [Fact]
    public void Clock_DaylightSavingChange_AddsNoTime()
    {
        // 25 Oct 2026, Central Europe: 03:00 CEST becomes 02:00 CET (01:00 UTC). A 5-minute lockdown
        // started at 02:58 local. On the old local clock, 3 real minutes later DateTime.Now read 02:01
        // and the lockdown had 62 minutes left.
        var startUtc = new DateTime(2026, 10, 25, 0, 58, 0, DateTimeKind.Utc);
        var startLocal = new DateTime(2026, 10, 25, 2, 58, 0);
        var nowUtc = startUtc.AddMinutes(3);
        var nowLocalAfterFallBack = new DateTime(2026, 10, 25, 2, 1, 0);
        var five = TimeSpan.FromMinutes(5);

        Assert.Equal(TimeSpan.FromMinutes(62), LockdownExitRule.Remaining(startLocal, five, nowLocalAfterFallBack));
        Assert.Equal(TimeSpan.FromMinutes(2), LockdownExitRule.Remaining(startUtc, five, nowUtc));
    }

    [Fact]
    public void ElapsedFraction_IsClamped()
    {
        var start = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        var hour = TimeSpan.FromHours(1);
        Assert.Equal(0, LockdownExitRule.ElapsedFraction(start, hour, start.AddMinutes(-5)));
        Assert.Equal(0.5, LockdownExitRule.ElapsedFraction(start, hour, start.AddMinutes(30)), 6);
        Assert.Equal(1, LockdownExitRule.ElapsedFraction(start, hour, start.AddHours(2)));
        Assert.Equal(0, LockdownExitRule.ElapsedFraction(start, TimeSpan.Zero, start));
    }

    // ============================ the veil ============================

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]    // no premium, no lockdown: the padlock as before
    [InlineData(false, true, false)]    // signed out mid-lockdown: the way out stays reachable
    public void Veil_NeverCoversARunningLockdown(bool hasPremium, bool lockdownActive, bool shows)
        => Assert.Equal(shows, LockdownExitRule.ShowsPremiumVeil(hasPremium, lockdownActive));

    [Fact]
    public void UnreadableRecovery_FailsOpen()
    {
        var (strict, panic) = LockdownExitRule.UnreadableRecovery;
        Assert.False(strict);
        Assert.True(panic);
    }

    // ============================ wiring ============================

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "ConditioningControlPanel", Path.Combine(parts));
        Assert.True(File.Exists(path), $"missing source file: {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void LockdownService_UsesTheRules()
    {
        var src = Source("Services", "Haptics", "LockdownService.cs");
        Assert.Contains("LockdownExitRule.PhraseMatches(phrase)", src);
        Assert.Contains("LockdownExitRule.Remaining(_activatedAt, _duration, DateTime.UtcNow)", src);
        Assert.Contains("LockdownExitRule.ElapsedFraction(_activatedAt, _duration, DateTime.UtcNow)", src);
        Assert.DoesNotContain("DateTime.Now - _activatedAt", src);
        Assert.DoesNotContain("_activatedAt = DateTime.Now;", src);
        Assert.DoesNotContain("DateTime.Now - _startedAt", src);
        Assert.Contains("LockdownExitRule.UnreadableRecovery", src);
        Assert.Contains("File.Move(tmp, RecoveryFilePath, overwrite: true)", src);
    }

    [Fact]
    public void Veil_IsRepaintedThroughTheRule_AndAgainWhenLockdownEnds()
    {
        var patreon = Source("MainWindow", "MainWindow.Patreon.cs");
        Assert.DoesNotContain("RefreshPremiumGate(LockdownTab?.LockdownGate)", patreon);
        Assert.Contains("RefreshLockdownGate();", patreon);
        Assert.Contains("LockdownExitRule.ShowsPremiumVeil", patreon);

        var lab = Source("MainWindow", "MainWindow.Lab.cs");
        var off = lab.IndexOf("private void OnLockdownDeactivated()", StringComparison.Ordinal);
        Assert.True(off >= 0);
        var tick = lab.IndexOf("private void OnLockdownTick(", off, StringComparison.Ordinal);
        Assert.True(tick > off);
        Assert.Contains("RefreshLockdownGate();", lab.Substring(off, tick - off));
    }
}
