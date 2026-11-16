using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private static readonly Action<MainShellWindow>[] BackRoomDoors =
    {
        shell => shell.LaunchPlayBackRoom(),
        shell => shell.OpenExclusiveFeature("backroom"),
        shell => typeof(MainShellWindow)
            .GetMethod("VisitFromSparkleWallet", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(shell, new object?[] { null, EventArgs.Empty }),
    };

    [Fact]
    public void BackRoomDoorsShareTheSignedInLaunchWrapper() => Run((shell, _) =>
    {
        var old = PlayTabView.LaunchGame;
        var doors = new List<string>();
        PlayTabView.LaunchGame = doors.Add;
        CoreAccount.IsLoggedInProvider = () => true;   // Run restores it
        try
        {
            foreach (var door in BackRoomDoors) door(shell);
            Assert.Equal(new[] { "backroom", "backroom", "backroom" }, doors);
        }
        finally { PlayTabView.LaunchGame = old; }
    });

    // WPF BackRoomHostService.cs:310-315: LaunchCore refuses a signed-out room and asks for sign-in.
    [Fact]
    public void BackRoomDoorsSignedOutOpenSignInAndNoRoom() => Run((shell, _) =>
    {
        CoreAccount.IsLoggedInProvider = () => false;
        shell.Show();   // the doors live on the shell; Run leaves it behind the launcher
        Dispatcher.UIThread.RunJobs();
        foreach (var door in BackRoomDoors)
        {
            door(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.OwnedWindows.OfType<LoginDialog>()).Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(GameWindow.OpenForTests("backroom"));
            Assert.DoesNotContain(GameWindow.OpenWindowsForTests(), w => w.Spec.Id == "backroom");
        }
    });

    [Fact]
    public void BackRoomOpensSignedInInitializesAndPanicCloses() => Run((shell, _) =>
    {
        CoreAccount.IsLoggedInProvider = () => true;   // Run restores it
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        shell.LaunchPlayBackRoom();
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(shell.OwnedWindows.OfType<LoginDialog>());
        Assert.True(ExclusivesTabView.IsOnThisBuild("backroom"));
        var w = GameWindow.OpenForTests("backroom")!;
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
