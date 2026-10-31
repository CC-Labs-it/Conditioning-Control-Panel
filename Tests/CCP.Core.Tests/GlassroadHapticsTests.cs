using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Racing Thoughts on a connected toy (owner 2026-10-31): the page is untrusted input, so the wire is clamped
/// here; the bed zeroes itself when the page stops sending; the row starts at the owner's 60%.</summary>
public class GlassroadHapticsTests
{
    [Theory]
    [InlineData(0.5, 600, 0.5, 600)]
    [InlineData(9.0, 600, 1.0, 600)]
    [InlineData(0.5, 1, 0.5, GlassroadHapticDirector.MinMs)]
    [InlineData(0.5, 999999, 0.5, GlassroadHapticDirector.MaxMs)]
    public void Pulse_IsClamped(double level, double ms, double wantLevel, int wantMs)
    {
        var p = GlassroadHapticDirector.ParsePulse(new JObject { ["level"] = level, ["ms"] = ms });
        Assert.NotNull(p);
        Assert.Equal(wantLevel, p!.Value.Level, 6);
        Assert.Equal(wantMs, p.Value.Ms);
    }

    [Fact]
    public void Pulse_RefusesWhatIsNotANumber()
    {
        Assert.Null(GlassroadHapticDirector.ParsePulse(null));
        Assert.Null(GlassroadHapticDirector.ParsePulse(new JObject()));
        Assert.Null(GlassroadHapticDirector.ParsePulse(new JObject { ["level"] = "loud", ["ms"] = 300 }));
        Assert.Null(GlassroadHapticDirector.ParsePulse(new JObject { ["level"] = 0.5, ["ms"] = "long" }));
        Assert.Null(GlassroadHapticDirector.ParsePulse(new JObject { ["level"] = double.NaN, ["ms"] = 300 }));
        Assert.Null(GlassroadHapticDirector.ParsePulse(new JObject { ["level"] = 0, ["ms"] = 300 }));
        Assert.Null(GlassroadHapticDirector.ParsePulse(new JObject { ["level"] = -1, ["ms"] = 300 }));
    }

    [Fact]
    public void Bed_IsClamped_AndZeroIsALevel()
    {
        Assert.Equal(0.3, GlassroadHapticDirector.ParseBed(new JObject { ["level"] = 0.3 }));
        Assert.Equal(1.0, GlassroadHapticDirector.ParseBed(new JObject { ["level"] = 4 }));
        Assert.Equal(0.0, GlassroadHapticDirector.ParseBed(new JObject { ["level"] = -4 }));
        Assert.Equal(0.0, GlassroadHapticDirector.ParseBed(new JObject { ["level"] = 0 }));
        Assert.Null(GlassroadHapticDirector.ParseBed(new JObject { ["level"] = "x" }));
        Assert.Null(GlassroadHapticDirector.ParseBed(null));
    }

    [Fact]
    public void Buttplug_StretchesAPulse_NeverPastTheCeiling()
    {
        Assert.Equal(600, GlassroadHapticDirector.DurationFor(600, false));
        Assert.Equal(1200, GlassroadHapticDirector.DurationFor(600, true));
        Assert.Equal(GlassroadHapticDirector.MaxMs, GlassroadHapticDirector.DurationFor(3000, true));
    }

    [Fact]
    public void TheRow_StartsOn_AtTheOwnersSixtyPercent()
    {
        var rule = new HapticSettingsV2().Rule(HapticLayer.Race);
        Assert.True(rule.Enabled);
        Assert.Equal(GlassroadHapticDirector.DefaultScale, rule.Intensity, 6);
    }

    [Fact]
    public void Limiter_RejectsFloods_ThenRecovers()
    {
        var limit = new GlassroadHapticDirector.Limiter();
        Assert.True(limit.Accept(0));
        Assert.False(limit.Accept(79));
        for (int i = 1; i < 6; i++) Assert.True(limit.Accept(i * 80));
        Assert.False(limit.Accept(480));
        Assert.True(limit.Accept(1000));
    }

    [Fact]
    public void Stop_ClearsRaceLayer_LeavesOtherLayers()
    {
        using var service = new ConditioningControlPanel.Services.HapticService(new HapticSettings { Enabled = true });
        var old = ConditioningControlPanel.CoreHaptics.Service;
        var oldPremium = ConditioningControlPanel.CoreEntitlement.HasPremiumProvider;
        try
        {
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => true;
            ConditioningControlPanel.CoreHaptics.Service = service;
            service.SetLayer(HapticLayer.Race, 0.8);
            service.SetLayer(HapticLayer.Video, 0.3);
            Assert.Equal(0.8, service.GetLayer(HapticLayer.Race));
            GlassroadHapticDirector.OnPageMessage(new JObject { ["type"] = "toy-stop" });
            Assert.Equal(0, service.GetLayer(HapticLayer.Race));
            Assert.Equal(0.3, service.GetLayer(HapticLayer.Video));
        }
        finally
        {
            ConditioningControlPanel.CoreHaptics.Service = old;
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = oldPremium;
        }
    }

    [Fact]
    public void Stop_WithNothingPlaying_IsSafe() => GlassroadHapticDirector.Stop();
}
