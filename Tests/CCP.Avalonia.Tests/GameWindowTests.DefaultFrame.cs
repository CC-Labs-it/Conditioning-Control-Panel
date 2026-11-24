using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private static global::Avalonia.Platform.Screen DefaultFrameScreen(PixelRect bounds, bool primary, double scale = 1)
    {
        var screen = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(bounds.X + 1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(screen, scale);
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(screen, primary);
        return screen;
    }

    [Fact]
    public void DefaultFrameUsesLaunchingScreenAndPanicStillClosesGame() => Run((shell, launcher) =>
    {
        var old = ScreenList.QueryOverride;
        var lifetimeField = typeof(Application).GetField("_applicationLifetime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var previousLifetime = lifetimeField.GetValue(Application.Current);
        lifetimeField.SetValue(Application.Current, new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
            { MainWindow = shell, ShutdownMode = ShutdownMode.OnExplicitShutdown });
        ScreenList.QueryOverride = _ => new[] { DefaultFrameScreen(new PixelRect(0, 0, 1600, 900), true),
            DefaultFrameScreen(new PixelRect(1600, 0, 2000, 1000), false, 2) };
        try
        {
            launcher.Hide(); shell.Show(); shell.Position = new PixelPoint(1800, 100);
            var game = GameWindow.Launch("breakoutdemo")!;
            Assert.Equal(850, game.Width); Assert.Equal(425, game.Height);
            Assert.Equal(new PixelPoint(1750, 75), game.Position);
            Assert.Equal(WindowStartupLocation.Manual, game.WindowStartupLocation);
            GameWindow.CloseAllForPanic(); Assert.False(game.IsVisible);
            shell.Hide(); launcher.Show(); launcher.Position = new PixelPoint(1800, 100); launcher.Activate();
            var launched = GameWindow.Launch("breakoutdemo")!;
            Assert.Equal(850, launched.Width); Assert.Equal(425, launched.Height);
            Assert.Equal(new PixelPoint(1750, 75), launched.Position);
        }
        finally { ScreenList.QueryOverride = old; lifetimeField.SetValue(Application.Current, previousLifetime); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleInactiveLauncherTakesPrecedenceOverPanelScreen(bool counter) => Run((shell, launcher) =>
    {
        var old = ScreenList.QueryOverride;
        var lifetimeField = typeof(Application).GetField("_applicationLifetime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var previousLifetime = lifetimeField.GetValue(Application.Current);
        lifetimeField.SetValue(Application.Current, new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
            { MainWindow = shell, ShutdownMode = ShutdownMode.OnExplicitShutdown });
        ScreenList.QueryOverride = _ => new[] { DefaultFrameScreen(new PixelRect(0, 0, 1600, 900), true),
            DefaultFrameScreen(new PixelRect(1600, 0, 2000, 1000), false) };
        try
        {
            launcher.Show(); launcher.Position = new PixelPoint(1800, 100);
            shell.Show(); shell.Position = new PixelPoint(100, 100); shell.Activate();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            // Headless does not deactivate the previous window when another is activated.
            typeof(WindowBase).GetMethod("HandleDeactivated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(launcher, null);
            Assert.True(launcher.IsVisible); Assert.True(shell.IsVisible); Assert.False(launcher.IsActive);
            var game = counter ? GameWindow.LaunchPrizeCounter()! : GameWindow.Launch("breakoutdemo")!;
            Assert.Equal(1700, game.Width); Assert.Equal(850, game.Height);
            if (!counter) Assert.Equal(new PixelPoint(1750, 75), game.Position);
            else
            {
                var size = shell.FrameSize ?? shell.ClientSize;
                Assert.Equal(new PixelPoint((int)Math.Round(Math.Clamp(100 + (size.Width * shell.RenderScaling - 1700) / 2, 0, 1900)),
                    (int)Math.Round(Math.Clamp(100 + (size.Height * shell.RenderScaling - 850) / 2, 0, 150))), game.Position);
            }
        }
        finally { ScreenList.QueryOverride = old; lifetimeField.SetValue(Application.Current, previousLifetime); }
    });

    [Fact]
    public void CounterFrameCentersOnVisiblePanel() => Run((shell, launcher) =>
    {
        var old = ScreenList.QueryOverride;
        var lifetimeField = typeof(Application).GetField("_applicationLifetime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var previousLifetime = lifetimeField.GetValue(Application.Current);
        lifetimeField.SetValue(Application.Current, new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
            { MainWindow = shell, ShutdownMode = ShutdownMode.OnExplicitShutdown });
        ScreenList.QueryOverride = _ => new[] { DefaultFrameScreen(new PixelRect(0, 0, 1600, 900), true) };
        try
        {
            launcher.Hide(); shell.Show(); shell.Position = new PixelPoint(200, 100);
            var game = GameWindow.LaunchPrizeCounter()!;
            var size = shell.FrameSize ?? shell.ClientSize;
            Assert.Equal(1360, game.Width); Assert.Equal(765, game.Height);
            Assert.Equal(new PixelPoint((int)Math.Round(Math.Clamp(200 + (size.Width * shell.RenderScaling - 1360) / 2, 0, 240)),
                (int)Math.Round(Math.Clamp(100 + (size.Height * shell.RenderScaling - 765) / 2, 0, 135))), game.Position);
        }
        finally { ScreenList.QueryOverride = old; lifetimeField.SetValue(Application.Current, previousLifetime); }
    });
}
