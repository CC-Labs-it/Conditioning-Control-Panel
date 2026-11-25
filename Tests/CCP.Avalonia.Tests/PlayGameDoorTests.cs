using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PlayGameDoorTests
{
    [Theory]
    [InlineData("goon", "BtnPlayGoon")]
    [InlineData("backroom", "BtnPlayBackRoom")]
    [InlineData("arcademy", "BtnPlayArcademy")]
    public void UnregisteredDoorIsDisabled(string id, string name) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        Assert.False(GameWindow.Games.ContainsKey(id));
        var view = new PlayTabView();
        var button = view.FindControl<Button>(name)!;
        Assert.False(button.IsEnabled);
        Assert.Equal(Loc.Get("exclusives_not_on_this_build"), ToolTip.GetTip(button));
    });

    [Theory]
    [InlineData("goon", "BtnPlayGoon")]
    [InlineData("arcademy", "BtnPlayArcademy")]
    public void SignedOutOpensLoginBeforeLaunch(string id, string name) => WithDoor(id, name, (shell, button, calls) =>
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(shell.OwnedWindows.OfType<LoginDialog>());
        Assert.Empty(calls);
    });

    [Fact]
    public void BackRoomLaunchesSignedOut() => WithDoor("backroom", "BtnPlayBackRoom", (shell, button, calls) =>
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("backroom", Assert.Single(calls));
    });

    [Fact]
    public void ArcademyGateRunsAfterSignIn() => WithDoor("arcademy", "BtnPlayArcademy", (shell, button, calls) =>
    {
        bool gateCalled = false;
        GameWindow.Games["arcademy"] = new("arcademy", "launcher_game_arcademy_title", "", () => { gateCalled = true; return false; });
        PlayTabView.LaunchGame = id => { calls.Add(id); GameWindow.Launch(id); };
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(gateCalled);
        Assert.Empty(calls);
        foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
        CoreAccount.IsLoggedInProvider = () => true;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(gateCalled);
        Assert.Equal("arcademy", Assert.Single(calls));
    });

    [Fact]
    public void BackRoomFailureShowsDialog() => WithDoor("backroom", "BtnPlayBackRoom", (shell, button, calls) =>
    {
        PlayTabView.LaunchGame = _ => throw new InvalidOperationException("door failure");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.Contains("door failure", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
    });

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void WithDoor(string id, string name, Action<MainShellWindow, Button, System.Collections.Generic.List<string>> test)
        => AvaloniaTestDispatcher.Run(() =>
        {
            Setup();
            var oldLogin = CoreAccount.IsLoggedInProvider;
            var oldLaunch = PlayTabView.LaunchGame;
            CoreAccount.IsLoggedInProvider = () => false;
            GameWindow.Games.Add(id, new(id, "launcher_game_" + id + "_title", ""));
            var calls = new System.Collections.Generic.List<string>();
            PlayTabView.LaunchGame = calls.Add;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("play");
                Dispatcher.UIThread.RunJobs();
                var button = shell.Named<Control>("PlayTab")!.FindControl<Button>(name)!;
                Assert.True(button.IsEnabled);
                Assert.Null(ToolTip.GetTip(button));
                test(shell, button, calls);
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
