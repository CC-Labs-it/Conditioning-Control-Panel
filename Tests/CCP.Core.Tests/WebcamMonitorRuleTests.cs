using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class WebcamMonitorRuleTests
{
    [Fact]
    public void Named_monitor_wins_over_primary_and_matches_case_insensitively()
    {
        var choice = WebcamMonitorRule.Select("display2", new[] { "DISPLAY1", "DISPLAY2" }, 0);
        Assert.Equal(1, choice.Index);
        Assert.False(choice.TemporaryFallback);
    }

    [Fact]
    public void Sleeping_monitor_falls_back_then_recovers_even_when_order_changes()
    {
        const string chosen = "DISPLAY2";
        var missing = WebcamMonitorRule.Select(chosen, new[] { "DISPLAY1" }, 0);
        Assert.Equal(0, missing.Index);
        Assert.True(missing.TemporaryFallback);
        var returned = WebcamMonitorRule.Select(chosen, new[] { "DISPLAY2", "DISPLAY1" }, 1);
        Assert.Equal(0, returned.Index);
        Assert.False(returned.TemporaryFallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Legacy_unnamed_calibration_uses_primary_without_waiting_for_a_monitor(string? name)
    {
        var choice = WebcamMonitorRule.Select(name, new[] { "DISPLAY1", "DISPLAY2" }, 1);
        Assert.Equal(1, choice.Index);
        Assert.False(choice.TemporaryFallback);
    }

    [Fact]
    public void Missing_primary_uses_first_available_temporarily()
    {
        var choice = WebcamMonitorRule.Select("DISPLAY2", new[] { "DISPLAY1" }, -1);
        Assert.Equal(0, choice.Index);
        Assert.True(choice.TemporaryFallback);
    }

    [Fact]
    public void No_screens_does_not_lose_the_named_monitor()
    {
        var choice = WebcamMonitorRule.Select("DISPLAY2", Array.Empty<string>(), -1);
        Assert.Equal(-1, choice.Index);
        Assert.True(choice.TemporaryFallback);
    }
}
