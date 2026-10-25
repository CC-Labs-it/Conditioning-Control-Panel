using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HeroMomentsTests
{
    private static Task Run(Action<MainShellWindow, AppSettings, SteppedTime> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.MotionLevel, s.PerformanceMode, s.PlayerLevel, s.PlayerXP) = (MotionLevel.Full, false, 10, 100);
        var time = new SteppedTime();
        var oldTime = BeatLoop.Time;
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            test(shell, s, time);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = oldTime;
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ChargeDriftExhaleAndDisabledReleaseFollowAmbientGate() => Run((shell, s, time) =>
    {
        shell.UpdateStartButton();
        var button = shell.FindControl<Button>("BtnStart")!;
        var charge = Assert.IsType<LinearGradientBrush>(button.Background);
        Assert.Equal(new RelativePoint(1, 0.35, RelativeUnit.Relative), charge.EndPoint); // WPF HeroFx.cs:279 lean
        Assert.True(shell.StartHeroRunning);
        time.Advance(TimeSpan.FromSeconds(0.4725));
        BeatLoop.StepAll();
        Assert.Equal(0.70, shell.FindControl<Border>("StartRingExhale")!.Opacity, 3);
        time.Advance(TimeSpan.FromSeconds(4.5 - 0.4725));
        BeatLoop.StepAll();
        Assert.Equal(0.66, charge.GradientStops[1].Offset, 3);
        s.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(shell.StartHeroRunning);
        Assert.Equal(0.5, charge.GradientStops[1].Offset, 3);
        Assert.Equal(0, shell.FindControl<Border>("StartRingExhale")!.Opacity);
        button.IsEnabled = false;
        Assert.NotSame(charge, button.Background);
        button.IsEnabled = true;
        shell.UpdateStartButton();
        Assert.Same(charge, button.Background);
    });

    [Fact]
    public Task IgnitionDipFlashAndStaggeredRingsSettleAndCancel() => Run((shell, s, time) =>
    {
        shell.FlashStartIgnition();
        var scale = (ScaleTransform)shell.FindControl<Button>("BtnStart")!.RenderTransform!;
        Assert.Equal(0.55, shell.FindControl<Border>("StartIgnitionFlash")!.Opacity, 3);
        Assert.Equal(0, shell.FindControl<Border>("StartRingBurstB")!.Opacity);
        time.Advance(TimeSpan.FromSeconds(0.04));
        BeatLoop.StepAll();
        Assert.Equal(0.97, scale.ScaleX, 3);
        time.Advance(TimeSpan.FromSeconds(0.04));
        BeatLoop.StepAll();
        Assert.Equal(0.95, shell.FindControl<Border>("StartRingBurstB")!.Opacity, 3);
        time.Advance(TimeSpan.FromSeconds(0.75));
        BeatLoop.StepAll();
        Assert.False(shell.StartIgniting);
        Assert.Equal(1, scale.ScaleX, 3);
        shell.FlashStartIgnition();
        s.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(shell.StartIgniting);
        Assert.Equal(1, scale.ScaleX);
        shell.FlashStartIgnition();
        Assert.False(shell.StartIgniting);
    });

    [Fact]
    public Task SaveDrawHoldFadeAndCloseCleanUp() => Run((shell, s, time) =>
    {
        shell.FindControl<Button>("BtnSaveAll")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        var tick = shell.FindControl<global::Avalonia.Controls.Shapes.Path>("SaveTick")!;
        Assert.True(shell.SaveAbsorbing);
        Assert.Equal(9, tick.StrokeDashOffset);
        time.Advance(TimeSpan.FromSeconds(0.4));
        BeatLoop.StepAll();
        Assert.Equal(0, tick.StrokeDashOffset, 3);
        Assert.Equal(1, tick.Opacity);
        time.Advance(TimeSpan.FromSeconds(1.33));
        BeatLoop.StepAll();
        Assert.Equal(0.5, tick.Opacity, 3);
        time.Advance(TimeSpan.FromSeconds(0.14));
        BeatLoop.StepAll();
        Assert.False(shell.SaveAbsorbing);
        Assert.Equal(0, tick.Opacity);
        shell.FlashSaveAbsorb();
        shell.FlashStartIgnition();
        shell.Close();
        Assert.False(shell.SaveAbsorbing);
        Assert.False(shell.StartIgniting);
        Assert.False(shell.StartHeroRunning);
    });

    [Fact]
    public Task RunningHeartbeatUsesPixelGrowthAndStopsInPerformanceMode() => Run((shell, s, time) =>
    {
        var old = s.AudioOnlySession;
        s.AudioOnlySession = true;
        try
        {
            CoreEngine.Start();
            shell.UpdateStartButton();
            var button = shell.FindControl<Button>("BtnStart")!;
            var scale = (ScaleTransform)button.RenderTransform!;
            Assert.IsNotType<LinearGradientBrush>(button.Background);
            time.Advance(TimeSpan.FromSeconds(2.4));
            BeatLoop.StepAll();
            Assert.Equal(1.012, scale.ScaleY, 3);
            s.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Equal(1, scale.ScaleY);
            Assert.False(shell.StartHeroRunning);
        }
        finally { CoreEngine.Stop(); s.AudioOnlySession = old; }
    });

    [Theory]
    [InlineData(19, "BASIC BIMBO")]
    [InlineData(20, "DUMB AIRHEAD")]
    [InlineData(49, "DUMB AIRHEAD")]
    [InlineData(50, "SYNTHETIC BLOWDOLL")]
    [InlineData(100, "PERFECT FUCKPUPPET")]
    public Task RankUsesWpfThresholds(int level, string rank) => Run((shell, s, time) =>
    {
        s.PlayerLevel = level;
        shell.UpdateLevelDisplay();
        Assert.Equal(CoreMods.MakeModAware(rank), shell.FindControl<TextBlock>("TxtPlayerTitle")!.Text);
    });

    [Fact]
    public Task CelebrationRequestsLocalizedToastAndStoppableSoundEvenWithMotionOff() => Run((shell, s, time) =>
    {
        var sink = OsNotifications.Sink;
        var audio = CoreAudio.PlayStoppableProvider;
        int plays = 0, stops = 0, notices = 0;
        OsNotifications.Sink = (title, body) => { Assert.Contains("11", body); notices++; };
        CoreAudio.PlayStoppableProvider = (path, volume, tag, started, finished) =>
        {
            Assert.EndsWith("lvup.mp3", path);
            Assert.Equal("level-up", tag);
            Assert.Equal(Math.Max(0.01f, (float)Math.Pow(s.MasterVolume / 100f, 1.5) * 0.2625f), volume);
            plays++;
            return () => stops++;
        };
        try
        {
            shell.CelebrateHeroLevelUp(11);
            Assert.Equal(1, shell.LevelBurstCount);
            Assert.True(shell.LevelChipPopping);
            Assert.Equal(1, notices);
            Assert.Equal(1, plays);
            s.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            shell.CelebrateHeroLevelUp(11);
            Assert.Equal(1, shell.LevelBurstCount);
            Assert.Equal(2, notices);
            Assert.Equal(2, plays);
            Assert.Equal(1, stops);
            var surface = Assert.Single(PanicSurfaces.All, x => x.Id == "shell-hero");
            shell.FlashSaveAbsorb();
            surface.Stop(shell);
            Assert.Equal(2, stops);
            Assert.False(shell.SaveAbsorbing);
            shell.Close();
            Assert.Equal(2, stops);
        }
        finally { OsNotifications.Sink = sink; CoreAudio.PlayStoppableProvider = audio; }
    });
}
