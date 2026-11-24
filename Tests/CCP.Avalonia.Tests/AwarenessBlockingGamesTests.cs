using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessBlockingGamesTests
{
    [Theory]
    [InlineData("breakoutdemo", false)]
    [InlineData("dtrh", true)]
    [InlineData("arcademy", true)]
    public void OnlyTheTwoWpfGameHostsSuppressAwareness(string id, bool blocks) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var window = new GameWindow(new GameWindow.Game(id, "title", "index.html"));
        try
        {
            using var probe = new AvApp.AwarenessAppState();
            Assert.False(probe.Read(DateTime.Now).BlockingSurfaceActive);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(blocks, probe.Read(DateTime.Now).BlockingSurfaceActive);
            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(probe.Read(DateTime.Now).BlockingSurfaceActive);
        }
        finally
        {
            window.Close();
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
