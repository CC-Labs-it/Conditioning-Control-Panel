using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HeroXpFxTests
{
    [Fact]
    public Task MeniscusRidesFillAndParksAtRestInReducedMotion() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.PlayerLevel, s.PlayerXP, s.MotionLevel) = (10, 100, MotionLevel.Off);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var bar = shell.FindControl<Border>("XPBar")!;
            var dot = shell.FindControl<Border>("XPMeniscus")!;
            Assert.True(bar.Width >= 5);
            Assert.Equal(0.55, dot.Opacity, 3);
            Assert.Equal(Math.Max(0, bar.Width - 6), ((TranslateTransform)dot.RenderTransform!).X, 3);

            SaveFrame(shell, "HeroXpMeniscus");

            s.PlayerXP = 0;
            shell.UpdateLevelDisplay();
            Assert.Equal(0, dot.Opacity);
            Assert.Equal(0, ((TranslateTransform)dot.RenderTransform!).X);

            s.PlayerXP = 100;
            s.MotionLevel = MotionLevel.Reduced;
            shell.UpdateLevelDisplay();
            Assert.Equal(0.55, dot.Opacity, 3);
            Assert.NotNull(((TranslateTransform)dot.RenderTransform!).Transitions);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task OdometerPopAndPulseUseSharedBeatAndStopAtMotionGateOrClose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.PlayerLevel, s.PlayerXP, s.MotionLevel, s.PerformanceMode) = (10, 100, MotionLevel.Full, false);
        s.OfflineMode = true;
        s.OfflineUsername = "hero-xp-test";
        var time = new SteppedTime();
        var oldTime = BeatLoop.Time;
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var dot = shell.FindControl<Border>("XPMeniscus")!;
            Assert.True(shell.XpMeniscusPulsing);
            Assert.Contains(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, dot) && loop.OnFrameBeat);
            time.Advance(TimeSpan.FromSeconds(2));
            BeatLoop.StepAll();
            Assert.Equal(0.95, dot.Opacity, 3);

            s.PlayerXP = 200;
            shell.UpdateLevelDisplay();
            var readout = shell.FindControl<TextBlock>("TxtXP")!;
            Assert.StartsWith("100 / ", readout.Text);
            time.Advance(TimeSpan.FromSeconds(0.35));
            BeatLoop.StepAll();
            Assert.StartsWith("175 / ", readout.Text);
            time.Advance(TimeSpan.FromSeconds(0.35));
            BeatLoop.StepAll();
            Assert.StartsWith("200 / ", readout.Text);
            Assert.False(shell.XpReadoutCounting);

            // A new level restarts its numerator at zero, rather than counting from the old level.
            s.PlayerXP = 20;
            ProgressionBank.Add(XpCurve.GetXPForLevel(10, XpCurve.EpochOf(s)), "Quest");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(11, s.PlayerLevel);
            Assert.True(shell.LevelChipPopping);
            Assert.StartsWith("0 / ", readout.Text);
            var chip = shell.FindControl<Border>("LevelChip")!;
            var group = (TransformGroup)chip.RenderTransform!;
            var scale = (ScaleTransform)group.Children[0];
            var rotate = (RotateTransform)group.Children[1];
            time.Advance(TimeSpan.FromSeconds(0.18));
            BeatLoop.StepAll();
            Assert.Equal(1.35, scale.ScaleX, 3);
            Assert.Equal(-4, rotate.Angle, 3);
            SaveFrame(shell, "HeroXpLevelChipPop");
            time.Advance(TimeSpan.FromSeconds(0.42));
            BeatLoop.StepAll();
            Assert.Equal(1, scale.ScaleX, 3);
            Assert.Equal(0, rotate.Angle, 3);
            Assert.False(shell.LevelChipPopping);

            s.PlayerXP = 100;
            shell.UpdateLevelDisplay();
            s.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.XpMeniscusPulsing);
            Assert.Equal(0.55, dot.Opacity, 3);
            s.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(shell.XpMeniscusPulsing);
            s.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.XpMeniscusPulsing);
            s.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            shell.WindowState = WindowState.Minimized;
            Assert.False(shell.XpMeniscusPulsing);
            shell.WindowState = WindowState.Normal;
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.True(shell.XpMeniscusPulsing);

            shell.PopLevelChip();
            s.PlayerXP = 40;
            shell.UpdateLevelDisplay();
            s.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.XpReadoutCounting);
            Assert.False(shell.LevelChipPopping);
            Assert.StartsWith("40 / ", readout.Text);
            Assert.Equal(1, scale.ScaleX, 3);
            shell.PopLevelChip();
            Assert.False(shell.LevelChipPopping);
            s.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            shell.PopLevelChip();
            s.PlayerXP = 60;
            shell.UpdateLevelDisplay();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.XpMeniscusPulsing);
            Assert.False(shell.XpReadoutCounting);
            Assert.False(shell.LevelChipPopping);
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, dot)
                || ReferenceEquals(loop.Owner, chip) || ReferenceEquals(loop.Owner, readout));
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
    private static void SaveFrame(MainShellWindow shell, string name)
    {
        var dir = Environment.GetEnvironmentVariable("CCP_GLOW_PROOF_DIR")
            ?? System.IO.Path.Combine(AppContext.BaseDirectory, "hero-xp-proof");
        System.IO.Directory.CreateDirectory(dir);
        Dispatcher.UIThread.RunJobs();
        using var frame = shell.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(System.IO.Path.Combine(dir, name + ".png"));
    }
}
