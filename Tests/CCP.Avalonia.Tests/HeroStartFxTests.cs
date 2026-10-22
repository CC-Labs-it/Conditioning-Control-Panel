using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.HeroFx.cs START charge/exhale/ignition/heartbeat + FlashSaveAbsorb, and
/// OnLevelUp's burst, notification and rank title, driven through the shell's own buttons and events.</summary>
public sealed class HeroStartFxTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task StartChargesIgnitesBeatsAndSaveAbsorbsOnTheSharedBeat() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.PlayerLevel, s.PlayerXP, s.MotionLevel, s.PerformanceMode) = (10, 100, MotionLevel.Full, false);
        (s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled) = (false, false, false, false);
        var time = new SteppedTime();
        var oldTime = BeatLoop.Time;
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        void Step(double seconds) { time.Advance(TimeSpan.FromSeconds(seconds)); BeatLoop.StepAll(); }
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            var b = shell.FindControl<Button>("BtnStart")!;
            var ring = shell.FindControl<Border>("StartRingExhale")!;
            var ringScale = (ScaleTransform)ring.RenderTransform!;

            // Idle: the charge (glow, -27 deg, +38 deg) drifts and the ring exhales.
            Assert.True(shell.StartChargeApplied);
            var brush = Assert.IsType<LinearGradientBrush>(b.Background);
            var glow = AmbientFxCanvas.Env.GlowColor.ToHsv().H;
            AssertHue(glow, brush.GradientStops[0].Color);
            AssertHue(glow - 27, brush.GradientStops[1].Color);
            AssertHue(glow + 38, brush.GradientStops[2].Color);
            Assert.True(shell.StartChargeDrifting);
            Assert.True(shell.StartExhaling);
            Step(1.35 * 0.35);
            Assert.Equal(0.70, ring.Opacity, 3);
            Step(1.35 - 1.35 * 0.35);
            Assert.Equal(55.0 / 50, ringScale.ScaleY, 3);
            Step(4.5 - 1.35);
            Assert.Equal(0.66, brush.GradientStops[1].Offset, 3);

            // Reduced motion parks the ambient loops at their rest frame; the charge stays painted.
            s.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.StartChargeDrifting);
            Assert.False(shell.StartExhaling);
            Assert.Equal(0.5, brush.GradientStops[1].Offset, 3);
            Assert.Equal(0, ring.Opacity);
            Assert.Same(brush, b.Background);
            s.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();

            // The press: dip + wash + rings; the engine starts, the charge hands back to red STOP and
            // the heartbeat waits for the dip to finish.
            var press = (ScaleTransform)b.RenderTransform!;
            var flash = shell.FindControl<Border>("StartIgnitionFlash")!;
            var burstB = shell.FindControl<Border>("StartRingBurstB")!;
            b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(CoreEngine.IsRunning);
            Assert.True(shell.StartIgniting);
            Assert.False(shell.StartChargeApplied);
            Assert.NotSame(brush, b.Background);
            Assert.False(shell.StartExhaling);
            Assert.False(shell.StartHeartbeating);
            Step(0.04);
            Assert.Equal(0.97, press.ScaleX, 3);
            Assert.Equal(0.55 * (1 - (1 - Math.Pow(1 - 0.04 / 0.3, 2))), flash.Opacity, 3);
            Assert.Equal(0, burstB.Opacity);
            Step(0.06);
            Assert.True(shell.StartHeartbeating);
            Assert.Equal(1, press.ScaleX, 3);
            Step(0.74);
            Assert.False(shell.StartIgniting);
            Assert.Equal(0, flash.Opacity);
            Assert.Equal(0, burstB.Opacity);
            Step(2.4 - 0.74);
            Assert.Equal(50.6 / 50, press.ScaleY, 3);
            s.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.StartHeartbeating);
            Assert.Equal(1, press.ScaleY, 3);
            s.PerformanceMode = false;

            // Stop: idle again, so the charge comes back.
            CoreEngine.Stop();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.StartChargeApplied);
            Assert.Same(brush, b.Background);

            // Save: the tick draws in 400 ms and is gone at 1.86 s; the ripple grows 14px.
            var tick = shell.FindControl<Path>("SaveTick")!;
            var ripple = shell.FindControl<Border>("SaveRipple")!;
            shell.FindControl<Button>("BtnSaveAll")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(shell.SaveAbsorbing);
            Step(0.2);
            Assert.Equal(9 * 0.25, tick.StrokeDashOffset, 3);
            Assert.Equal(1, tick.Opacity, 3);
            Assert.Equal(0.75 * Math.Pow(1 - 0.2 / 0.52, 2), ripple.Opacity, 3);
            Step(1.7);
            Assert.False(shell.SaveAbsorbing);
            Assert.Equal(0, tick.Opacity, 3);

            // Motion Off: both one-shots are skipped.
            s.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            shell.FlashSaveAbsorb();
            shell.FlashStartIgnition();
            Assert.False(shell.SaveAbsorbing);
            Assert.False(shell.StartIgniting);
            s.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(shell.StartExhaling);

            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.StartExhaling || shell.StartChargeDrifting || shell.StartHeartbeating);
        }
        finally
        {
            CoreEngine.Stop();
            CoreEngine.StoppedHook = null;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = oldTime;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LevelUpBurstsNotifiesAndRetitlesTheRank() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.PlayerLevel, s.PlayerXP, s.MotionLevel, s.PerformanceMode) = (19, 20, MotionLevel.Full, false);
        var (oldOffline, oldOfflineName) = (s.OfflineMode, s.OfflineUsername);
        (s.OfflineMode, s.OfflineUsername) = (true, "hero-start-test");
        var oldSink = OsNotifications.Sink;
        var sent = new List<(string Title, string Body)>();
        OsNotifications.Sink = (t, b) => sent.Add((t, b));
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var title = shell.FindControl<TextBlock>("TxtPlayerTitle")!;
            Assert.Equal(CoreMods.MakeModAware("BASIC BIMBO"), title.Text);

            ProgressionBank.Add(XpCurve.GetXPForLevel(19, XpCurve.EpochOf(s)), "Quest");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(20, s.PlayerLevel);
            Assert.Equal(1, shell.LevelUpBursts);
            Assert.Contains((Loc.Get("toast_level_up_title"), Loc.GetF("toast_level_up_body", 20)), sent);
            Assert.Equal(CoreMods.MakeModAware("DUMB AIRHEAD"), title.Text);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            OsNotifications.Sink = oldSink;
            (s.OfflineMode, s.OfflineUsername) = (oldOffline, oldOfflineName);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    private static void AssertHue(double expected, Color actual)
    {
        double diff = Math.Abs((((actual.ToHsv().H - expected) % 360) + 540) % 360 - 180);
        Assert.True(diff < 1.5, $"hue {actual.ToHsv().H:F1} vs {expected:F1}");
    }
}
