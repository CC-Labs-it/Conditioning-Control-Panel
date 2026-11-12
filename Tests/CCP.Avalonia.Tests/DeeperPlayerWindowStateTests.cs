using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class DeeperPlayerWindowStateTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task RestoreAndSaveBounds() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.DeeperPlayerWindowLeft = 10; s.DeeperPlayerWindowTop = 10;
        s.DeeperPlayerWindowWidth = 700; s.DeeperPlayerWindowHeight = 550;
        var w = new EnhancementPlayerWindow(null, null);
        w.Show();
        Assert.Equal(700, w.Width); Assert.Equal(550, w.Height);
        Assert.Equal(new PixelPoint(10, 10), w.Position);
        w.Position = new PixelPoint(30, 40); w.Width = 720; w.Height = 560; w.Close();
        Assert.Equal  (30, s.DeeperPlayerWindowLeft); Assert.Equal(720, s.DeeperPlayerWindowWidth);
        s.DeeperPlayerWindowLeft = 100000; s.DeeperPlayerWindowTop = 100000;
        s.DeeperPlayerWindowWidth = 10; s.DeeperPlayerWindowHeight = 10;
        w = new EnhancementPlayerWindow(null, null);
        w.Show();
        Assert.Equal(900, w.Width); Assert.Equal(768, w.Height);
        Assert.NotEqual(new PixelPoint(100000, 100000), w.Position);
        w.WindowState = WindowState.Maximized; w.Close();
        Assert.Equal(10, s.DeeperPlayerWindowWidth);
        return Task.CompletedTask;
    });

    [Fact]
    public Task WindowUsesMonitorScaleWhenRestoringAndClosing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        EnhancementPlayerWindow? w = null;
        try
        {
            EnhancementPlayerWindow.WindowStateScaleOverride = 1.5;
            var settings = CoreSettings.Current;
            settings.DeeperPlayerWindowLeft = 100; settings.DeeperPlayerWindowTop = 80;
            settings.DeeperPlayerWindowWidth = 700; settings.DeeperPlayerWindowHeight = 550;
            w = new EnhancementPlayerWindow(null, null);
            w.Show();
            Assert.Equal(new PixelPoint(150, 120), w.Position);
            w.Position = new PixelPoint(300, 180);
            w.Close(); w = null;
            Assert.Equal(200, settings.DeeperPlayerWindowLeft);
            Assert.Equal(120, settings.DeeperPlayerWindowTop);
        }
        finally { w?.Close(); EnhancementPlayerWindow.WindowStateScaleOverride = null; }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1.0, 100, 80, 100, 80)]
    [InlineData(1.5, 100, 80, 150, 120)]
    [InlineData(2.0, 100, 80, 200, 160)]
    public void WpfCoordinatesRoundTripAtScreenScale(double scale, double x, double y, int pixelX, int pixelY)
    {
        var screens = new[] { (new PixelRect(0, 0, 2400, 1800), scale) };
        Assert.True(EnhancementPlayerWindow.TryRestorePosition(x, y, 700, 550, screens, out var position));
        Assert.Equal(new PixelPoint(pixelX, pixelY), position);
        Assert.Equal(new Point(x, y), EnhancementPlayerWindow.ToSavedPosition(position, scale));
    }

    [Fact]
    public void RestoreUsesTheScreenWhoseScaledCandidateIsVisible()
    {
        var screens = new[] { (new PixelRect(0, 0, 1920, 1080), 1.0),
            (new PixelRect(-2400, 0, 2400, 1800), 1.5) };
        Assert.True(EnhancementPlayerWindow.TryRestorePosition(-800, 100, 700, 550, screens, out var position));
        Assert.Equal(new PixelPoint(-1200, 150), position);
        Assert.Equal(new Point(-800, 100), EnhancementPlayerWindow.ToSavedPosition(position, 1.5));
        Assert.False(EnhancementPlayerWindow.TryRestorePosition(-3000, 100, 700, 550, screens, out _));
        // Only 199 DIPs of the window reach the scaled monitor.
        Assert.False(EnhancementPlayerWindow.TryRestorePosition(-2101, 100, 700, 550, screens, out _));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 4)]
    public Task TitleBarUsesWindowsSeam(bool windows, int expected) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var calls = 0;
        var w = new EnhancementPlayerWindow(null, null) { TitleBarWindowsOverride = windows,
            TitleBarHandleOverride = _ => new IntPtr(42), TitleBarAttributeOverride = (_, _, _) => calls++ };
        w.Show(); Assert.Equal(expected, calls); w.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task TutorialReusesShellPlayer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        var old = TutorialHeadHooks.ShellProvider;
        try
        {
            shell.Show(); TutorialHeadHooks.ShellProvider = () => shell;
            var player = shell.ShowOrActivateDeeperPlayer();
            TutorialHeadHooks.OpenDeeperPlayerWithLastSaved();
            Assert.Same(player, Assert.Single(shell.OwnedWindows.OfType<EnhancementPlayerWindow>()));
            player.Close();
        }
        finally { TutorialHeadHooks.ShellProvider = old; shell.Close(); }
        return Task.CompletedTask;
    });
}
