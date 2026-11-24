using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Glassroad_CounterDoor_ReturnsOwnedAndPanicClosesBoth() => RunGlassroad((shell, _, _) =>
    {
        var race = GameWindow.Launch(GameWindow.GlassroadId)!;
        var sent = new List<JObject>();
        race.Posted += json => sent.Add(JObject.Parse(json));
        race.HandleMessage("{\"type\":\"ready\"}");
        sent.Clear();
        race.HandleMessage("{\"type\":\"open-prize-counter\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.True((bool)sent.Single(o => (string?)o["type"] == "pause")["on"]!);
        var counter = GameWindow.OpenForTests(GameWindow.PrizeCounterId);
        Assert.NotNull(counter);
        var shop = new List<JObject>();
        counter!.Posted += json => shop.Add(JObject.Parse(json));
        counter.HandleMessage("{\"type\":\"ready\"}");
        var init = shop.Single(o => (string?)o["type"] == "init");
        Assert.Equal(("glassroad", "kart"), ((string?)init["from"], (string?)init["tab"]));
        race.HandleMessage("{\"type\":\"open-prize-counter\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(counter, GameWindow.OpenForTests(GameWindow.PrizeCounterId));
        sent.Clear();
        counter.HandleMessage("{\"type\":\"close\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.Single(sent.Where(o => (string?)o["type"] == "owned"));

        race.HandleMessage("{\"type\":\"open-prize-counter\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(GameWindow.OpenForTests(GameWindow.PrizeCounterId));
        sent.Clear();
        GameWindow.CloseCounterForExit();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(sent, o => (string?)o["type"] == "owned");

        race.HandleMessage("{\"type\":\"open-prize-counter\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(GameWindow.OpenForTests(GameWindow.PrizeCounterId));
        sent.Clear();
        PanicSurfaces.All.Single(p => p.Id == "games").Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.DoesNotContain(sent, o => (string?)o["type"] == "owned");
    });

    [Fact]
    public void Glassroad_LinuxShellCounterReturnFocusesShell() => RunShellRace((_, race, host, process, _) =>
    {
        host.Receive(JObject.Parse("{\"type\":\"open-prize-counter\"}"));
        Dispatcher.UIThread.RunJobs();
        string NextType()
        {
            var read = process.StandardOutput.ReadLineAsync();
            Assert.True(read.Wait(5000));
            return (string)JObject.Parse(read.Result!)["type"]!;
        }
        Assert.Equal("init", NextType());
        Assert.Equal("pause", NextType());
        GameWindow.OpenForTests(GameWindow.PrizeCounterId)!.CloseFor("back");
        Assert.Equal("owned", NextType());
        Assert.Equal("focus", NextType());
    });

    [Fact]
    public void Glassroad_QueuedCounterDoorCannotReopenAfterPanic() => RunGlassroad((shell, _, _) =>
    {
        var race = GameWindow.Launch(GameWindow.GlassroadId)!;
        race.HandleMessage("{\"type\":\"open-prize-counter\"}");
        PanicSurfaces.All.Single(p => p.Id == "games").Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
    });
}
