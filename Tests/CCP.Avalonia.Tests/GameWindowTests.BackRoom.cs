using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Safety;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void BackRoom_RegistersInitializesAndSuspendsBeforeClosing() => Run((shell, launcher) =>
    {
        Assert.True(ExclusivesTabView.IsOnThisBuild("backroom"));
        var oldLogin = ConditioningControlPanel.CoreAccount.IsLoggedInProvider;
        var oldLaunch = PlayTabView.LaunchGame;
        string? launched = null;
        try
        {
            ConditioningControlPanel.CoreAccount.IsLoggedInProvider = () => true;
            PlayTabView.LaunchGame = id => launched = id;
            shell.OpenExclusiveFeature("backroom");
            Assert.Equal("backroom", launched);
        }
        finally
        {
            ConditioningControlPanel.CoreAccount.IsLoggedInProvider = oldLogin;
            PlayTabView.LaunchGame = oldLaunch;
        }
        Assert.Equal("backroom/index.html", GameWindow.Games["backroom"].Page);
        var w = new GameWindow(GameWindow.Games["backroom"]);
        var sent = new List<string>();
        w.Posted += sent.Add;
        w.Show();
        w.HandleMessage("{\"type\":\"ready\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("init", (string?)JObject.Parse(Assert.Single(sent))["type"]);
        var now = DateTime.Now;
        Assert.True(GameWindow.HandleBackRoomPanic(now));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("suspend", (string?)JObject.Parse(sent[^1])["type"]);
        Assert.True(GameWindow.HandleBackRoomPanic(now.AddSeconds(1)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("close", (string?)JObject.Parse(sent[^1])["type"]);
        w.Close();
    });

    private static GameWindow OpenReadyRoom(List<JObject> sent)
    {
        var w = new GameWindow(GameWindow.Games["backroom"]);
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.Show();
        w.Activate();
        w.HandleMessage("{\"type\":\"ready\"}");
        Dispatcher.UIThread.RunJobs();
        sent.Clear();
        return w;
    }

    /// <summary>WPF MainWindow.xaml.cs:1527,1535,1589-1590: a first Escape over a ready room is its pause
    /// (kept-escape to the page, the window stays), never the default override's full stop.</summary>
    [Fact]
    public void BackRoom_FirstEscapeOverAReadyRoom_IsItsPause() => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        var (oldKey, oldEnabled) = (s.PanicKey, s.PanicKeyEnabled);
        var sent = new List<JObject>();
        try
        {
            (s.PanicKey, s.PanicKeyEnabled) = ("Escape", true);
            launcher.Hide();
            var w = OpenReadyRoom(sent);
            Assert.Equal(PanicPolicy.KeptEscapePage.Room, w.KeptEscapePage);
            Assert.Same(w, GameWindow.EscapePauseInFront());
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            shell.HandlePanicKeyPress(t0);
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen());
            Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);
        }
        finally { (s.PanicKey, s.PanicKeyEnabled) = (oldKey, oldEnabled); }
    });

    /// <summary>WPF BackRoomHostService.cs:520-535: the race portal is always answered, so the page's held
    /// scene is released: "prime" below tier 2 (plus the refusal), else "elsewhere" and Glassroad opens.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackRoom_RacePortal_IsAlwaysAnswered(bool lab) => RunGlassroad(lab ? "1" : null, (_, _, _) =>
    {
        var oldDenied = CoreEntitlement.ShowDeniedHandler;
        int refusals = 0;
        CoreEntitlement.ShowDeniedHandler = _ => refusals++;
        var sent = new List<JObject>();
        try
        {
            var w = OpenReadyRoom(sent);
            w.HandleMessage("{\"type\":\"game-open\",\"game\":\"race\"}");
            Dispatcher.UIThread.RunJobs();
            var reply = Assert.Single(sent, m => (string?)m["type"] == "game-open-result");
            Assert.Equal(("race", false, lab ? "elsewhere" : "prime"),
                ((string?)reply["game"], (bool)reply["ok"]!, (string?)reply["reason"]));
            Assert.Equal(lab ? 0 : 1, refusals);
            Assert.Equal(lab, GameWindow.OpenWindowsForTests().Any(g => g.Spec.Id == GameWindow.GlassroadId));
        }
        finally { CoreEntitlement.ShowDeniedHandler = oldDenied; }
    }, lab);

    /// <summary>WPF BackRoomHostService.cs:630-638: minimise suspends the room (its music stops), restore
    /// resumes it; a restore under a panic hold keeps it held.</summary>
    [Fact]
    public void BackRoom_MinimiseSuspendsTheRoom() => Run((_, launcher) =>
    {
        launcher.Hide();
        var sent = new List<JObject>();
        var w = OpenReadyRoom(sent);
        try
        {
            w.WindowState = global::Avalonia.Controls.WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            var frame = Assert.Single(sent);
            Assert.Equal(("suspend", true, "minimise"), ((string?)frame["type"], (bool)frame["on"]!, (string?)frame["reason"]));
            sent.Clear();
            w.WindowState = global::Avalonia.Controls.WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            frame = Assert.Single(sent, m => (string?)m["type"] == "suspend");
            Assert.Equal((false, "minimise"), ((bool)frame["on"]!, (string?)frame["reason"]));
            w.WindowState = global::Avalonia.Controls.WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.HandleBackRoomPanic(DateTime.Now));
            Dispatcher.UIThread.RunJobs();
            sent.Clear();
            w.WindowState = global::Avalonia.Controls.WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(sent, m => (string?)m["type"] == "suspend" && (bool?)m["on"] == false);
        }
        finally { w.Close(); }
    });
}
