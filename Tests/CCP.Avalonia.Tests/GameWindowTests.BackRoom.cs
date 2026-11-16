using System;
using System.Collections.Generic;
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
}
