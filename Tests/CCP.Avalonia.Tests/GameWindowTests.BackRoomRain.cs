using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using ConditioningControlPanel.Services.BackRoom;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.BackRoom;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void BreakoutFxCarrierStartsRainAndExitStopsIt() => Run((_, _) =>
    {
        var w = GameWindow.Launch("breakoutdemo")!;
        var scheduler = new FxClock();
        w.RoomFxSchedulerFactory = () => scheduler;
        double[]? shown = null;
        int closed = 0;
        w.RainBridgeFactory = () => new BackRoomRainBridge(
            (a, b, c, d, e, f) => shown = [a, b, c, d, e, f],
            () => closed++, () => false, () => false, () => 100);
        JObject? ack = null;
        w.Posted += json => ack = JObject.Parse(json);
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"fx\",\"token\":\"rain\",\"fxId\":\"fx.gif_storm\",\"station\":\"breakout\"}");
        Assert.Equal("rain", (string?)ack!["token"]);
        Assert.Contains(ack["fired"]!, x => (string?)x == "gif-rain");
        Assert.Contains(ack["fired"]!, x => (string?)x == "glitch-bubbles");
        Assert.Contains(ack["skipped"]!, x => (string?)x["prim"] == "flash-burst" && (string?)x["why"] == "unknown");

        scheduler.RunAll();
        Assert.NotNull(shown);
        Assert.Equal(14.0 / 3, shown![0], 6);
        Assert.Equal(3, shown[1]);
        Assert.Equal(GifCascadePayload.GIF_SIZE, shown[2]);
        w.Web.OnWebMessage("{\"type\":\"exit\"}");
        Assert.Equal(1, closed);
        w.Web.OnWebMessage("{\"type\":\"fx\",\"token\":\"late\",\"fxId\":\"fx.gif_storm\",\"station\":\"breakout\"}");
        Assert.Equal("busy", (string?)ack!["skipped"]!.Single()["why"]);
        w.Close();
        Assert.Equal(1, closed);
    });

    [Fact]
    public void BreakoutExitCancelsPendingJackpotRain() => Run((_, _) =>
    {
        var w = GameWindow.Launch("breakoutdemo")!;
        var scheduler = new FxClock();
        w.RoomFxSchedulerFactory = () => scheduler;
        int shown = 0;
        w.RainBridgeFactory = () => new BackRoomRainBridge(
            (a, b, c, d, e, f) => shown++, () => { }, () => false, () => false, () => 100);
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"fx\",\"token\":\"jackpot\",\"fxId\":\"fx.jackpot\",\"station\":\"breakout\"}");
        Assert.NotEmpty(scheduler.Pending);
        w.Close();
        scheduler.RunAll(); // even a callback already queued by the dispatcher must be harmless
        Assert.Equal(0, shown);
    });
    [Fact]
    public void MinimizeStopsRainAndRefusesRequestsUntilRestored() => Run((_, _) =>
    {
        var w = GameWindow.Launch("breakoutdemo")!;
        var scheduler = new FxClock();
        w.RoomFxSchedulerFactory = () => scheduler;
        int shown = 0, closed = 0;
        w.RainBridgeFactory = () => new BackRoomRainBridge(
            (a, b, c, d, e, f) => shown++, () => closed++, () => false, () => false, () => 100);
        JObject? ack = null;
        w.Posted += json => ack = JObject.Parse(json);
        w.Web.OnNavigationCompleted(w.PageUrl!);
        const string fire = "{\"type\":\"fx\",\"fxId\":\"fx.gif_storm\",\"station\":\"breakout\"}";
        w.Web.OnWebMessage(fire);
        scheduler.RunAll();
        Assert.Equal(1, shown);
        w.RoomMinimizedChanged(true);
        Assert.Equal(1, closed);
        w.Web.OnWebMessage(fire);
        Assert.Equal("busy", (string?)ack!["skipped"]!.Single()["why"]);
        w.RoomMinimizedChanged(false);
        w.Web.OnWebMessage(fire);
        scheduler.RunAll();
        Assert.Equal(2, shown);
        w.Close();
    });

    [Theory]
    [InlineData("fx-release")]
    [InlineData("station-close")]
    public void ReleaseCancelsPendingRainForTheStation(string type) => Run((_, _) =>
    {
        var w = GameWindow.Launch("breakoutdemo")!;
        var scheduler = new FxClock();
        w.RoomFxSchedulerFactory = () => scheduler;
        int shown = 0;
        w.RainBridgeFactory = () => new BackRoomRainBridge(
            (a, b, c, d, e, f) => shown++, () => { }, () => false, () => false, () => 100);
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"fx\",\"token\":\"jackpot\",\"fxId\":\"fx.jackpot\",\"station\":\"breakout\"}");
        Assert.NotEmpty(scheduler.Pending);
        w.Web.OnWebMessage(new JObject { ["type"] = type, ["token"] = "jackpot", ["station"] = "breakout" }.ToString());
        scheduler.RunAll();
        Assert.Equal(0, shown);
        w.Close();
    });

    [Fact]
    public void StormDispatchesGlitchWash() => Run((_, _) =>
    {
        var w = GameWindow.Launch("breakoutdemo")!;
        var clock = new FxClock(); w.RoomFxSchedulerFactory = () => clock;
        int duration = 0; double opacity = 0;
        w.FlashBridgeFactory = () => new BackRoomFlashBridge((ms, alpha) => { duration = ms; opacity = alpha; },
            (_, _, _, _, _) => { }, () => { }, () => { }, () => false, () => 100, "/tmp", "/tmp");
        JObject? ack = null; w.Posted += json => ack = JObject.Parse(json);
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"fx\",\"fxId\":\"fx.gif_storm\",\"station\":\"breakout\"}");
        Assert.Contains(ack!["fired"]!, x => (string?)x == "glitch-bubbles");
        clock.RunAll();
        Assert.Equal(BackRoomFxPlan.GlitchPulseMs, duration);
        Assert.Equal(BackRoomFxPlan.GlitchOpacity, opacity);
        w.Close();
    });

    [Theory]
    [InlineData("local", true)]
    [InlineData("remote", false)]
    [InlineData("escape", false)]
    public void DealtGifFullReachesOnlyLocalHero(string kind, bool expected) => Run((_, _) =>
    {
        var file = Path.GetTempFileName();
        try
        {
            var w = GameWindow.Launch("breakoutdemo")!;
            var clock = new FxClock(); w.RoomFxSchedulerFactory = () => clock;
            int heroes = 0;
            w.FlashBridgeFactory = () => new BackRoomFlashBridge((_, _) => { },
                (_, _, _, _, _) => heroes++, () => { }, () => { }, () => false, () => 100,
                Path.GetDirectoryName(file)!, Path.GetDirectoryName(file)!);
            var url = kind == "local" ? "https://ccp.assets/" + Path.GetFileName(file) :
                kind == "remote" ? "https://example.com/" + Path.GetFileName(file) : "https://ccp.assets/%2e%2e%2f" + Path.GetFileName(file);
            w.RoomFxDealFactory = () => new BackRoomMediaDeal(0, [new BackRoomGif("g0", url, 0, 0, "pool")], Array.Empty<BackRoomWord>());
            JObject? ack = null; w.Posted += json => ack = JObject.Parse(json);
            w.Web.OnNavigationCompleted(w.PageUrl!);
            w.Web.OnWebMessage("{\"type\":\"fx\",\"fxId\":\"fx.sub_cascade\",\"station\":\"breakout\"}");
            Assert.Contains(ack!["fired"]!, x => (string?)x == "gif-full");
            clock.RunAll(); Assert.Equal(expected ? 1 : 0, heroes); w.Close();
        }
        finally { File.Delete(file); }
    });

    [Theory]
    [InlineData("close", false, 100L, 1)]
    [InlineData("minimize", false, 100L, 1)]
    [InlineData("exit", false, 100L, 1)]
    [InlineData("close", true, 100L, 0)]
    [InlineData("close", false, 12100L, 0)]
    public void TeardownStopsFlashAndRespectsWashOwnership(string route, bool chaos, long end, int closes) => Run((_, _) =>
    {
        var w = GameWindow.Launch("breakoutdemo")!;
        var clock = new FxClock(); w.RoomFxSchedulerFactory = () => clock;
        int stopped = 0, closed = 0; long now = 100;
        w.FlashBridgeFactory = () => new BackRoomFlashBridge((_, _) => { }, (_, _, _, _, _) => { },
            () => stopped++, () => closed++, () => chaos, () => now, "/tmp", "/tmp");
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"fx\",\"fxId\":\"fx.gif_storm\",\"station\":\"breakout\"}");
        clock.RunAll(); now = end;
        if (route == "close") w.Close();
        else if (route == "minimize") w.RoomMinimizedChanged(true);
        else w.Web.OnWebMessage("{\"type\":\"exit\"}");
        Assert.Equal(1, stopped); Assert.Equal(closes, closed);
        w.Close();
    });

    [Fact]
    public void WordEffectUsesAuthoredEnvelopeAndStopsOnMinimize() => Run((_, _) =>
    {
        var w = GameWindow.Launch("breakoutdemo")!;
        var clock = new FxClock(); w.RoomFxSchedulerFactory = () => clock;
        w.RoomFxDealFactory = () => new BackRoomMediaDeal(0, Array.Empty<BackRoomGif>(), [new BackRoomWord("k", "WORD", "pool")]);
        var words = new List<(string, int, int, bool, int, int)>();
        int stops = 0;
        w.RoomSubliminal = (text, opacity, hold, suppress, fadeIn, fadeOut) => words.Add((text, opacity, hold, suppress, fadeIn, fadeOut));
        w.StopRoomSubliminal = () => stops++;
        JObject? ack = null; w.Posted += json => ack = JObject.Parse(json);
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"fx\",\"fxId\":\"fx.sub_cascade\",\"station\":\"breakout\"}");
        Assert.Contains(ack!["fired"]!, x => (string?)x == "sub-burst9");
        clock.RunAll();
        Assert.NotEmpty(words);
        Assert.All(words, word => { Assert.Equal("WORD", word.Item1); Assert.Equal(100, word.Item2); Assert.Equal(400, word.Item3); Assert.True(word.Item4); Assert.Equal(80, word.Item5); Assert.Equal(350, word.Item6); });
        w.RoomMinimizedChanged(true);
        Assert.Equal(1, stops);
        w.Close();
        Assert.Equal(1, stops);
    });

    private sealed class FxClock : IFxScheduler
    {
        public readonly List<Action> Pending = new();
        public long NowMs => 100;
        public IDisposable After(int delayMs, Action action) { Pending.Add(action); return new Noop(); }
        public void RunAll() { foreach (var action in Pending.ToArray()) action(); Pending.Clear(); }
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
