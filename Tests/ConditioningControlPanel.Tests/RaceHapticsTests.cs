using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Glassroad;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Racing Thoughts on a connected toy (owner 2026-10-31): the page is untrusted input, so the wire is clamped
/// here; the bed zeroes itself when the page stops sending; the row starts at the owner's 60%.</summary>
public class RaceHapticsTests
{
    [Theory]
    [InlineData(0.5, 600, 0.5, 600)]
    [InlineData(9.0, 600, 1.0, 600)]
    [InlineData(0.5, 1, 0.5, RaceHapticDirector.MinMs)]
    [InlineData(0.5, 999999, 0.5, RaceHapticDirector.MaxMs)]
    public void Pulse_IsClamped(double level, double ms, double wantLevel, int wantMs)
    {
        var p = RaceHapticDirector.ParsePulse(new JObject { ["level"] = level, ["ms"] = ms });
        Assert.NotNull(p);
        Assert.Equal(wantLevel, p!.Value.Level, 6);
        Assert.Equal(wantMs, p.Value.Ms);
    }

    [Fact]
    public void Pulse_RefusesWhatIsNotANumber()
    {
        Assert.Null(RaceHapticDirector.ParsePulse(null));
        Assert.Null(RaceHapticDirector.ParsePulse(new JObject()));
        Assert.Null(RaceHapticDirector.ParsePulse(new JObject { ["level"] = "loud", ["ms"] = 300 }));
        Assert.Null(RaceHapticDirector.ParsePulse(new JObject { ["level"] = 0.5, ["ms"] = "long" }));
        Assert.Null(RaceHapticDirector.ParsePulse(new JObject { ["level"] = double.NaN, ["ms"] = 300 }));
        Assert.Null(RaceHapticDirector.ParsePulse(new JObject { ["level"] = 0, ["ms"] = 300 }));
        Assert.Null(RaceHapticDirector.ParsePulse(new JObject { ["level"] = -1, ["ms"] = 300 }));
    }

    [Fact]
    public void Bed_IsClamped_AndZeroIsALevel()
    {
        Assert.Equal(0.3, RaceHapticDirector.ParseBed(new JObject { ["level"] = 0.3 }));
        Assert.Equal(1.0, RaceHapticDirector.ParseBed(new JObject { ["level"] = 4 }));
        Assert.Equal(0.0, RaceHapticDirector.ParseBed(new JObject { ["level"] = -4 }));
        Assert.Equal(0.0, RaceHapticDirector.ParseBed(new JObject { ["level"] = 0 }));
        Assert.Null(RaceHapticDirector.ParseBed(new JObject { ["level"] = "x" }));
        Assert.Null(RaceHapticDirector.ParseBed(null));
    }

    [Fact]
    public void Buttplug_StretchesAPulse_NeverPastTheCeiling()
    {
        Assert.Equal(600, RaceHapticDirector.DurationFor(600, false));
        Assert.Equal(1200, RaceHapticDirector.DurationFor(600, true));
        Assert.Equal(RaceHapticDirector.MaxMs, RaceHapticDirector.DurationFor(3000, true));
    }

    [Fact]
    public void TheRow_StartsOn_AtTheOwnersSixtyPercent()
    {
        var rule = new HapticSettingsV2().Rule(HapticLayer.Race);
        Assert.True(rule.Enabled);
        Assert.Equal(RaceHapticDirector.DefaultScale, rule.Intensity, 6);
    }

    [Fact]
    public void Init_SaysTheHostHasToys_AndWhetherOneIsConnected()
    {
        var off = GlassroadRules.InitFrame("u_1", "Kit", 2, false, 1, 1);
        Assert.True((bool)off["caps"]!["toys"]!);
        Assert.False((bool)off["caps"]!["toysConnected"]!);
        var on = GlassroadRules.InitFrame("u_1", "Kit", 2, false, 1, 1, toysConnected: true);
        Assert.True((bool)on["caps"]!["toysConnected"]!);
    }

    [Fact]
    public void Stop_WithNothingPlaying_IsSafe() => RaceHapticDirector.Stop();
}
