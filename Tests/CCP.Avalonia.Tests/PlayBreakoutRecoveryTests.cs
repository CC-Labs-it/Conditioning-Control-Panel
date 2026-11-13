using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PlayBreakoutRecoveryTests
{
    [Fact]
    public void FullBreakoutCardOpensTheFullHost() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        var (oldSettings, oldLogin, oldLab, oldServer) =
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider, GameWindow.Server);
        var root = Directory.CreateTempSubdirectory("play-full-breakout-");
        using var server = new WebAssetServer(root.FullName);
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreEntitlement.HasLabProvider = () => true;
        GameWindow.Server = () => server;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("play");
            Dispatcher.UIThread.RunJobs();
            var button = shell.Named<Control>("PlayTab")!.FindControl<Button>("BtnPlayBreakout")!;
            Assert.True(button.IsEnabled);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var game = Assert.IsType<GameWindow>(GameWindow.OpenForTests("breakout"));
            Assert.Equal("/backroom/stations/breakout/play.html", game.PageUrl!.AbsolutePath);
            Assert.DoesNotContain("demo=1", game.PageUrl.Query, StringComparison.Ordinal);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            shell.Close();
            GameWindow.Server = oldServer;
            CoreAccount.IsLoggedInProvider = oldLogin;
            CoreEntitlement.HasLabProvider = oldLab;
            settings.SealForReset();
            CoreSettings.ServiceProvider = oldSettings;
            root.Delete(true);
        }
    });
}
