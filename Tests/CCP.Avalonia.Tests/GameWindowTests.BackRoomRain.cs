using System;
using System.Linq;
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
        Assert.Equal("gif-rain", (string?)ack["fired"]!.Single());
        Assert.Contains(ack["skipped"]!, x => (string?)x["prim"] == "flash-burst" && (string?)x["why"] == "unknown");
        Assert.Contains(ack["skipped"]!, x => (string?)x["prim"] == "glitch-bubbles" && (string?)x["why"] == "unknown");
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

    private sealed class FxClock : IFxScheduler
    {
        public readonly List<Action> Pending = new();
        public long NowMs => 100;
        public IDisposable After(int delayMs, Action action) { Pending.Add(action); return new Noop(); }
        public void RunAll() { foreach (var action in Pending.ToArray()) action(); Pending.Clear(); }
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
