using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.PrizeCounter;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Open speed (owner 2026-11-17: "the shop seems to reload after 1 sec that we are in"): one page serves many
/// opens (a reset, never a load, between them), door pages warm soon after their window shows, a press that beats the
/// page slides on a waiting card, and the page's made-up host is never held by a SmartScreen check.</summary>
public class CounterSheetOpenSpeedTests
{
    [Theory]
    [InlineData(true, true, true)]     // served an open: start over
    [InlineData(true, false, false)]   // ready, never handed over: already clean
    [InlineData(false, true, false)]   // still loading: the next open takes that load
    [InlineData(false, false, false)]
    public void Only_a_page_that_served_an_open_is_reset(bool ready, bool handedOver, bool reset)
        => Assert.Equal(reset, CounterSheetRules.NeedsReset(ready, handedOver));

    [Theory]
    [InlineData(true, true, false, true, 0, true)]     // kept and ready: the slide waits for its first paint
    [InlineData(true, false, true, true, 0, true)]     // kept, its reset answers in a few ms
    [InlineData(true, false, false, true, 0, false)]   // still loading: slide now on the waiting card
    [InlineData(false, true, false, true, 0, false)]   // the last open's page (still sliding up)
    [InlineData(true, true, false, false, 0, false)]   // no browser yet (a cold game sheet)
    [InlineData(true, true, false, true, 0.4, false)]  // reopened mid-slide
    public void The_slide_waits_only_for_a_page_that_is_handed_over_at_once(bool fresh, bool ready, bool resetting, bool core, double p, bool holds)
        => Assert.Equal(holds, CounterSheetRules.HoldsSlide(fresh, ready, resetting, core, p));

    [Fact]
    public void Star_pulse_is_still_for_off_and_gentle_and_half_at_reduced()
    {
        Assert.Equal(0, CounterSheetRules.StarPulse(MotionLevel.Off, false).Amp);
        Assert.Equal(0, CounterSheetRules.StarPulse(MotionLevel.Full, true).Amp);
        var full = CounterSheetRules.StarPulse(MotionLevel.Full, false);
        var reduced = CounterSheetRules.StarPulse(MotionLevel.Reduced, false);
        Assert.True(full.Amp > 0 && full.Ms > 0);
        Assert.Equal(full.Amp / 2, reduced.Amp, 3);
    }

    [Fact]
    public void Warm_soon_and_a_silent_reset_falls_back_fast()
    {
        Assert.InRange(CounterSheetRules.WarmDelayMs, 500, 2000);
        Assert.InRange(CounterSheetRules.ResetTimeoutMs, 100, 1000);
    }

    [Fact]
    public void The_view_resets_instead_of_loading_between_opens_and_never_warms_a_game_sheet()
    {
        var view = Read("ConditioningControlPanel", "Controls", "CounterSheetView.cs");
        var game = Read("ConditioningControlPanel", "Controls", "GameCounterSheet.cs");
        Assert.DoesNotContain("if (_core != null && _session == 0) Navigate(", view);
        Assert.Contains("KeepPage();", view);
        Assert.Contains("{\\\"type\\\":\\\"reset\\\"}", view);
        Assert.Contains("IsReputationCheckingRequired = false", view);
        Assert.Contains("warm: false", game);
        var html = Read("ConditioningControlPanel", "Resources", "web", "prizecounter", "sheet.html");
        Assert.Contains("runSheet(", html);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
