using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void BackRoomDoorsUseTheFreeLaunchWrapper() => Run((shell, _) =>
    {
        var old = PlayTabView.LaunchGame;
        var doors = new List<string>();
        PlayTabView.LaunchGame = doors.Add;
        try
        {
            shell.LaunchPlayBackRoom();
            shell.OpenExclusiveFeature("backroom");
            typeof(ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow)
                .GetMethod("VisitFromSparkleWallet", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shell, new object?[] { null, EventArgs.Empty });
            Assert.Equal(new[] { "backroom", "backroom", "backroom" }, doors);
        }
        finally { PlayTabView.LaunchGame = old; }
    });

    [Fact]
    public void BackRoomOpensWithoutAccountInitializesAndPanicCloses() => Run((_, _) =>
    {
        Assert.True(ExclusivesTabView.IsOnThisBuild("backroom"));
        var w = GameWindow.Launch("backroom")!;
        Assert.NotNull(w);
        Assert.EndsWith("/backroom/index.html", w.PageUrl!.AbsolutePath);
        var frames = new List<JObject>();
        w.Posted += json => frames.Add(JObject.Parse(json));
        w.Web.OnNavigationCompleted(w.PageUrl);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(frames, f => (string?)f["type"] == "init" && (int?)f["protocol"] == 1 && f["lex"] is JObject);
        GameWindow.CloseAllForPanic();
        Assert.DoesNotContain(w, GameWindow.OpenWindowsForTests());
    });
}
