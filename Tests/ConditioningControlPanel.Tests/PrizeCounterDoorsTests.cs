using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.2 lane L5: every desktop door opens the Prize Counter on the right tab, a game page asks for it with
/// contract 1 (<c>open-prize-counter</c>), the Back Room opens it over the suspended room and hears the close,
/// and panic never leaves the counter on screen over a paused game.
/// </summary>
public class PrizeCounterDoorsTests
{
    [Theory]
    [InlineData("backroom", "backroom")]
    [InlineData("kart", "kart")]
    [InlineData("KART ", "kart")]
    [InlineData("rain", "rain-clothes")]   // contract 1's Rain tab is the clothes tab
    [InlineData("rain-hair", "rain-hair")]
    [InlineData("prime", "prime")]
    [InlineData("garage", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ADoorsTab_IsOneThePageKnows(string? asked, string? tab)
        => Assert.Equal(tab, PrizeCounterHostService.CounterTab(asked));

    [Fact]
    public void EveryContractTab_LandsOnARealTab()
    {
        foreach (var t in new[] { "kart", "chess", "profile", "desk", "prime", "rain", "backroom" })
            Assert.Contains(PrizeCounterHostService.CounterTab(t), PrizeCounterHostService.Tabs);
    }

    [Theory]
    [InlineData("backroom")]
    [InlineData("glassroad")]
    [InlineData("piecebypiece")]
    [InlineData("rain-wardrobe")]
    public void AGamePagesAsk_IsRead(string from)
    {
        var m = JObject.FromObject(new { type = "open-prize-counter", tab = "backroom", from });
        Assert.True(PrizeCounterHostService.TryReadOpenRequest(m, out var f, out var tab));
        Assert.Equal(from, f);
        Assert.Equal("backroom", tab);
    }

    [Fact]
    public void AnythingElse_IsNotACounterAsk()
    {
        Assert.False(PrizeCounterHostService.TryReadOpenRequest(null, out _, out _));
        Assert.False(PrizeCounterHostService.TryReadOpenRequest(JObject.Parse("{\"type\":\"station-request\"}"), out _, out _));
        Assert.False(PrizeCounterHostService.TryReadOpenRequest(JObject.Parse("{\"type\":\"open-prize-counter\",\"from\":\"evil\"}"), out _, out _));
        Assert.False(PrizeCounterHostService.TryReadOpenRequest(JObject.Parse("{\"type\":\"open-prize-counter\",\"from\":5}"), out _, out _));
        Assert.True(PrizeCounterHostService.TryReadOpenRequest(JObject.Parse("{\"type\":\"open-prize-counter\",\"from\":\"backroom\",\"tab\":7}"), out _, out var tab));
        Assert.Null(tab);
    }

    [Theory]
    [InlineData(null, "backroom", true)]       // opened from the wallet on the first tab, then the Back Room asks
    [InlineData("kart", "backroom", true)]
    [InlineData("backroom", "backroom", false)]
    [InlineData("kart", null, false)]          // a door with no tab only brings it forward
    public void AnOpenCounter_ReloadsOnlyForAnotherTab(string? current, string? wanted, bool reload)
        => Assert.Equal(reload, PrizeCounterHostService.ReopensOnTab(current, wanted));

    [Theory]
    [InlineData("panic", "panic")]
    [InlineData("back", "back")]
    [InlineData(null, "back")]
    [InlineData("whatever", "back")]
    public void ThePagesClose_KeepsOnlyPanic(string? page, string reason)
        => Assert.Equal(reason, PrizeCounterHostService.PageCloseReason(page));

    [Fact]
    public void EveryGameUnderTheCounter_HearsTheCloseOnce_EvenIfOneThrows()
    {
        var heard = new List<string>();
        PrizeCounterHostService.NotifyOpeners(new List<Action<string>>
        {
            r => heard.Add("a:" + r),
            _ => throw new InvalidOperationException("boom"),
            r => heard.Add("b:" + r),
        }, PrizeCounterHostService.ClosePanic);
        Assert.Equal(new[] { "a:panic", "b:panic" }, heard);
    }

    [Theory]
    [InlineData("back", false, true, true)]      // Escape through the counter's ladder: back in the room, keyboard and all
    [InlineData("failed", false, true, true)]
    [InlineData("wear", false, true, false)]     // Rain's dressing room takes the front
    [InlineData("panic", false, false, false)]   // a panic close never wakes the room
    [InlineData("app-exit", false, false, false)]
    [InlineData("back", true, false, false)]     // a panic press holds the room until its window is activated
    public void TheRoom_ComesBackOnlyOnACalmClose(string reason, bool panicSuspended, bool resume, bool focus)
    {
        var r = BackRoomHostService.CounterReturn(reason, panicSuspended);
        Assert.Equal(resume, r.Resume);
        Assert.Equal(focus, r.Focus);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // a counter on its own is the registry rung's
    [InlineData(false, true, false)]
    public void TheLegacyLadder_ClosesTheCounterBeforeAGamesOwnRung(bool counter, bool game, bool close)
        => Assert.Equal(close, PanicPolicy.CounterClosesBeforeHandOff(counter, game));

    [Fact]
    public void Panic_ClosesTheCounterWithThePanicReason()
    {
        var src = Read("ConditioningControlPanel", "Services", "Safety", "GameSurfaces.cs");
        Assert.Contains("PrizeCounterHostService.CloseFor(PrizeCounter.PrizeCounterHostService.ClosePanic)", src);
        var main = Read("ConditioningControlPanel", "MainWindow", "MainWindow.xaml.cs");
        int counterFirst = main.IndexOf("PanicPolicy.CounterClosesBeforeHandOff(", StringComparison.Ordinal);
        int roomRung = main.IndexOf("BackRoomHostService.HandlePanicPress();", StringComparison.Ordinal);
        int descentRung = main.IndexOf("handed off to the Rabbit Hole descent", StringComparison.Ordinal);
        Assert.True(counterFirst > 0 && counterFirst < descentRung && descentRung < roomRung,
            "the counter closes before the descent, DtRH, Arcademy and Back Room rungs return");
    }

    [Fact]
    public void TheBackRoom_OpensTheCounterOverTheRoom_AndHandsTheGrantsBack()
    {
        var src = Read("ConditioningControlPanel", "Services", "BackRoom", "BackRoomHostService.cs");
        Assert.Contains("PrizeCounterHostService.TryReadOpenRequest(message, out _, out var counterTab)", src);
        Assert.Contains("_bridge.Suspend(true, \"counter\");", src);
        Assert.Contains("PrizeCounter.PrizeCounterHostService.Launch(\"backroom\", tab ?? \"backroom\",", src);
        Assert.Contains("Post(new { type = \"owned\", grants =", src);
        Assert.Contains("prizeCounter = !_breakoutPage,", src);
        // The ask is read BEFORE the bridge, which would log it as unhandled.
        Assert.True(src.IndexOf("TryReadOpenRequest(message", StringComparison.Ordinal)
            < src.IndexOf("_bridge?.Handle(message)", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryDesktopDoor_OpensTheCounterOnItsTab()
    {
        Assert.Contains("PrizeCounterHostService.Launch(from: \"wallet\")",
            Read("ConditioningControlPanel", "MainWindow", "MainWindow.SparkleWallet.cs"));
        Assert.Contains("PrizeCounterHostService.Launch(from: \"launcher\")",
            Read("ConditioningControlPanel", "Windows", "Launcher", "LauncherWindow.xaml.cs"));
        var counter = Read("ConditioningControlPanel", "Services", "PrizeCounter", "PrizeCounterHostService.cs");
        Assert.Contains("CounterLauncher = (from, tab) => Launch(from, tab);", counter);
        Assert.Contains("CloseFor(CloseWear);", counter);
        Assert.Contains("OpenWardrobe(key)", counter);
        Assert.Contains("launch(\"rain-wardrobe\",",
            Read("ConditioningControlPanel", "Services", "Companion", "RainWardrobeHostService.cs"));
        // Every Rain row the wardrobe sends to the counter lands on a Rain tab.
        var items = Read("ConditioningControlPanel", "Resources", "web", "rain-wardrobe", "items.json");
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(items, "\"tab\"\\s*:\\s*\"([^\"]+)\""))
            Assert.StartsWith("rain-", PrizeCounterHostService.CounterTab(m.Groups[1].Value));
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return File.ReadAllText(Path.Combine(dir!.FullName, Path.Combine(parts)));
    }
}
