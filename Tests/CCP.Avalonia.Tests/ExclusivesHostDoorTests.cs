using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow/MainWindow.Exclusives.cs:206-209: the Goon and Arcademy cards open their game,
/// sign-in first. Here they follow the registered host.</summary>
public sealed class ExclusivesHostDoorTests
{
    [Theory]
    [InlineData("goon")]
    [InlineData("arcademy")]
    public void CardFollowsHostAndOpensAfterSignIn(string id) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Assert.False(GameWindow.Games.ContainsKey(id));
        Assert.False(ExclusivesTabView.IsOnThisBuild(id));
        Assert.Equal(GameWindow.Games.ContainsKey("backroom"), ExclusivesTabView.IsOnThisBuild("backroom"));   // #5421: Back Room follows its host too
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldLaunch = PlayTabView.LaunchGame;
        var calls = new List<string>();
        GameWindow.Games.Add(id, new(id, "launcher_game_" + id + "_title", ""));
        PlayTabView.LaunchGame = calls.Add;
        CoreAccount.IsLoggedInProvider = () => false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Assert.True(ExclusivesTabView.IsOnThisBuild(id));
            shell.OpenExclusiveFeature(id);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.OwnedWindows.OfType<LoginDialog>());
            Assert.Empty(calls);
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            CoreAccount.IsLoggedInProvider = () => true;
            shell.OpenExclusiveFeature(id);
            Assert.Equal(id, Assert.Single(calls));
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.Close();
            GameWindow.Games.Remove(id);
            CoreAccount.IsLoggedInProvider = oldLogin;
            PlayTabView.LaunchGame = oldLaunch;
        }
    });
}
