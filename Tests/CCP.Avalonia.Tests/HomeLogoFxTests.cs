using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HomeLogoFxTests
{
    [Fact]
    public Task StillLogoFollowsMotionVisibilityAndWindowState() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var face = tab.FindControl<Border>("LogoFaceLogo")!;
            var logo = tab.FindControl<AnimatedLogoImage>("ImgLogo")!;
            var band = tab.FindControl<Rectangle>("LogoSheen")!;
            var scale = (ScaleTransform)face.RenderTransform!;
            using var bitmap = new Bitmap(global::Avalonia.Platform.AssetLoader.Open(new Uri(AnimatedLogoImage.ArtworkUri)));
            logo.SetArtwork(bitmap);
            Assert.True(shell.LogoDrifting);
            Assert.True(shell.LogoSheenScheduled);
            time.Advance(TimeSpan.FromSeconds(40)); BeatLoop.StepAll();
            Assert.Equal(1.04, scale.ScaleX, 3);
            // Drive the timer callback directly, then advance the same clock as the compositor.
            typeof(MainShellWindow).GetMethod("OnLogoSheenTick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, new object?[] { null, EventArgs.Empty });
            time.Advance(TimeSpan.FromSeconds(.55)); BeatLoop.StepAll();
            Assert.Equal(.26, band.Opacity, 3);
            face.IsVisible = false;
            Assert.False(shell.LogoDrifting); Assert.False(shell.LogoSheenScheduled);
            Assert.Equal(1, scale.ScaleX); Assert.Equal(0, band.Opacity);
            face.IsVisible = true;
            Assert.True(shell.LogoDrifting);
            shell.WindowState = WindowState.Minimized;
            Assert.False(shell.LogoDrifting); Assert.False(shell.LogoSheenScheduled);
            shell.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.LogoDrifting);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.LogoDrifting); Assert.False(shell.LogoSheenScheduled);
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(shell.LogoDrifting);
            logo.SetArtwork(null);
            Assert.False(shell.LogoDrifting); Assert.False(shell.LogoSheenScheduled);
            Assert.Equal(1, scale.ScaleX); Assert.Equal(0, band.Opacity);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs(); BeatLoop.Time = TimeProvider.System;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
