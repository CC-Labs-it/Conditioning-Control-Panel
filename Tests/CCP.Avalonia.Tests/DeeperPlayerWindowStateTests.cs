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
