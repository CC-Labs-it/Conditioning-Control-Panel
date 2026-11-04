using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FypPanicLadderTests
{
    // WPF MainWindow.xaml.cs:1776: in ladder mode the feed consumes the panic key.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PanicKeyHonoursFeedLadderAndOverride(bool overridesAll) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldSurfaces = PanicSurfaces.All;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.PanicKeyEnabled = true;
        service.Current.PanicKey = "F8";
        service.Current.PanicOverridesAll = overridesAll;
        var stops = new List<string>();
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stops.Add("stop")) };
        var shell = new MainShellWindow();
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        try
        {
            feed.Show();
            Assert.True(GameWindow.IsForYouOnScreen());
            shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
            Assert.Equal(overridesAll, feed.IsVisible);
            Assert.Equal(overridesAll ? 1 : 0, stops.Count);
        }
        finally
        {
            feed.Close();
            shell.Close();
            PanicSurfaces.All = oldSurfaces;
            CoreSettings.ServiceProvider = oldProvider;
        }
    });
}
