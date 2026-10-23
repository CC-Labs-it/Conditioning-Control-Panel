using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SpiralEmbedBridgeTests
{
    [Theory]
    [InlineData("map", "map")]
    [InlineData("mini", "mini")]
    [InlineData("map&redirect=evil", "mini")]
    public void ModeIsNormalized(string input, string expected) =>
        Assert.Equal("https://app.cclabs.app/embed/spiral?mode=" + expected, SpiralEmbedBridge.BuildUrl(input));

    [Theory]
    [InlineData("https://app.cclabs.app/embed/spiral", true)]
    [InlineData("https://APP.CCLABS.APP/another", true)]
    [InlineData("http://app.cclabs.app/embed/spiral", false)]
    [InlineData("https://app.cclabs.app.evil.test/embed/spiral", false)]
    [InlineData("https://app.cclabs.app:444/embed/spiral", false)]
    [InlineData("https://user@app.cclabs.app/embed/spiral", false)]
    public void NavigationStaysAtEmbedOrigin(string input, bool expected) =>
        Assert.Equal(expected, SpiralEmbedBridge.AllowsNavigation(new Uri(input)));

    [Fact]
    public void ReadyFlushesOnlyTheLatestState_AndResetDropsIt()
    {
        var bridge = new SpiralEmbedBridge();
        Assert.Null(bridge.Post("first"));
        Assert.Null(bridge.Post("latest"));
        Assert.Null(bridge.Receive("{\"type\":\"ready\"}"));
        Assert.Null(bridge.Receive("garbage"));
        Assert.Null(bridge.Receive("{\"type\":{}}"));
        Assert.False(bridge.IsReady);
        Assert.Equal("latest", bridge.Receive("{\"type\":\"spiral:ready\"}"));
        Assert.True(bridge.IsReady);
        Assert.Null(bridge.Receive("{\"type\":\"spiral:ready\"}"));
        Assert.Equal("live", bridge.Post("live"));
        bridge.Reset();
        Assert.False(bridge.IsReady);
        Assert.Null(bridge.Post("discarded"));
        bridge.Reset();
        Assert.Null(bridge.Receive("{\"type\":\"spiral:ready\"}"));
    }

    [Fact]
    public void StatePreservesRawServerFieldsAndDateStrings()
    {
        const string raw = "{\"when\":\"2026-08-16T00:00:00Z\",\"stations\":[{\"id\":\"first\"}],\"future\":42}";
        var block = new DescentBlock
        {
            DevotionDays = 14, Stage = new DescentStage { N = 3 },
            Relapse = new DescentRelapse { Multiplier = 1.2 },
            Vat = new DescentVat { FillPct = 125 }, RawJson = raw,
        };
        var state = SpiralEmbedBridge.BuildState(block, true, PerformanceTier.Balanced);
        Assert.Equal("spiral:state", (string?)state["type"]);
        Assert.Equal(14, (int)state["devotion_days"]!);
        Assert.Equal(3, (int)state["stage"]!);
        Assert.Equal(1.2, (double)state["relapse_multiplier"]!);
        Assert.Equal(125, (double)state["vat_fill_pct"]!);
        Assert.True((bool)state["reduced_motion"]!);
        Assert.Equal("mid", (string?)state["perf_tier"]);
        Assert.Equal(JTokenType.String, state["descent"]!["when"]!.Type);
        Assert.Equal("2026-08-16T00:00:00Z", (string?)state["descent"]!["when"]);
        Assert.Equal(42, (int)state["descent"]!["future"]!);
        Assert.Equal("first", (string?)state["descent"]!["stations"]![0]!["id"]);
        Assert.Null(state["lit_station_ids"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    public void MissingOrUnusableRawKeepsV1Contract(string? raw)
    {
        var state = SpiralEmbedBridge.BuildState(new DescentBlock { RawJson = raw }, false, PerformanceTier.Quality);
        Assert.Null(state["descent"]);
        Assert.Equal(0, (int)state["devotion_days"]!);
        Assert.Equal(1.0, (double)state["relapse_multiplier"]!);
        Assert.Equal("high", (string?)state["perf_tier"]);
    }
}
