using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StartHeroTests
{
    [Fact]
    public Task ChargeIgnitionHeartbeatAndMotionGatesPaintTheRealShell() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.MotionLevel = MotionLevel.Full; s.PerformanceMode = false;
        s.FlashEnabled = s.SubliminalEnabled = s.BubblesEnabled = s.BouncingTextEnabled = false;
        s.MandatoryVideosEnabled = s.LockCardEnabled = s.BubbleCountEnabled = s.PopQuizEnabled = false;
        s.MindWipeEnabled = s.IntensityRampEnabled = s.AutonomyModeEnabled = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs();
            var button = shell.Named<Button>("BtnStart")!;
            var exhale = shell.Named<Border>("StartRingExhale")!;
            shell.PaintStartHero(shell.StartHeroEpoch + .4725);
            Assert.IsType<LinearGradientBrush>(button.Background);
            Assert.InRange(exhale.Opacity, .69, .71);
            Assert.True(((ScaleTransform)exhale.RenderTransform!).ScaleY > 1);
            shell.PaintStartHero(shell.StartHeroEpoch + 2);
            Assert.Equal(0, exhale.Opacity);

            shell.FlashStartIgnition();
            Assert.Equal(.55, shell.Named<Border>("StartIgnitionFlash")!.Opacity, 5);
            Assert.Equal(.95, shell.Named<Border>("StartRingBurstA")!.Opacity, 5);
            Assert.Equal(0, shell.Named<Border>("StartRingBurstB")!.Opacity);
            shell.PaintStartHero(100); // both one-shots expire even with a delayed frame
            Assert.Equal(0, shell.Named<Border>("StartIgnitionFlash")!.Opacity);
            Assert.Equal(0, shell.Named<Border>("StartRingBurstA")!.Opacity);
            Assert.Equal(0, shell.Named<Border>("StartRingBurstB")!.Opacity);

            CoreEngine.Start(); shell.UpdateStartButton(); shell.PaintStartHero(shell.StartHeroEpoch + 2.4);
            Assert.Equal(0, exhale.Opacity);
            Assert.IsType<SolidColorBrush>(button.Background); // red STOP survives the FX funnel
            Assert.Equal(1.012, ((ScaleTransform)button.RenderTransform!).ScaleY, 5);
            shell.UpdateStartButtonForRemoteControl(true); shell.PaintStartHero(shell.StartHeroEpoch + 2.4);
            Assert.Equal(1, ((ScaleTransform)button.RenderTransform!).ScaleY);
            shell.UpdateStartButtonForRemoteControl(false);
            CoreEngine.Stop(); shell.UpdateStartButton();
            s.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            shell.FlashStartIgnition(); shell.PaintStartHero(shell.StartHeroEpoch + .4725);
            Assert.Equal(0, exhale.Opacity);
            Assert.Equal(0, shell.Named<Border>("StartIgnitionFlash")!.Opacity);
            Assert.Equal(1, ((ScaleTransform)button.RenderTransform!).ScaleY);
            Assert.IsType<LinearGradientBrush>(button.Background); // static charge at reduced motion
            s.MotionLevel = MotionLevel.Full;
            shell.WindowState = WindowState.Minimized; shell.PaintStartHero(shell.StartHeroEpoch + .4725);
            Assert.Equal(0, exhale.Opacity);
        }
        finally { CoreEngine.Stop(); shell.Close(); CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });
}
