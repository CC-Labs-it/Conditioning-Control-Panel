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
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HeroXpFxTests
{
    // WPF MainWindow.ChromeFx.cs:681,690. The motion gate must settle at the
    // rounded animation target, while an award made with motion off still truncates.
    [Fact]
    public Task FractionalOdometerKeepsRoundedTargetAcrossRepaintAndMotionGate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.PlayerLevel, s.PlayerXP, s.MotionLevel, s.PerformanceMode) = (10, 10, MotionLevel.Full, false);
        s.OfflineMode = true;
        s.OfflineUsername = "fractional-xp-test";
        s.AutoPerformanceMode = false;
        s.GentleVisuals = false;
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromSeconds(1)); BeatLoop.StepAll();
            var txt = shell.FindControl<TextBlock>("TxtXP")!;
            ProgressionBank.Add(.8, "Mantra"); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.XpReadoutCounting);
            Assert.StartsWith("10 / ", txt.Text);
            shell.UpdateLevelDisplay(); // duplicate repaint must retain the same count
            Assert.True(shell.XpReadoutCounting);
            time.Advance(TimeSpan.FromSeconds(1)); BeatLoop.StepAll();
            Assert.StartsWith("11 / ", txt.Text);
            s.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.StartsWith("11 / ", txt.Text);
            shell.UpdateLevelDisplay();
            Assert.StartsWith("10 / ", txt.Text);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = oldTime;
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    // WPF MainWindow.ChromeFx.cs:677-690 selects formatting independently of visibility.
    [Theory]
    [InlineData(false, MotionLevel.Full, .8, "11 / ")]
    [InlineData(true, MotionLevel.Full, .8, "11 / ")]
    [InlineData(false, MotionLevel.Reduced, .8, "11 / ")]
    [InlineData(false, MotionLevel.Off, .8, "10 / ")]
    [InlineData(false, MotionLevel.Full, .2, "10 / ")]
    public Task ParkedAwardUsesWpfRounding(bool minimized, MotionLevel motion, double amount, string expected)
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previousService = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        (s.PlayerLevel, s.PlayerXP, s.MotionLevel, s.PerformanceMode) = (10, 10, motion, false);
        s.OfflineMode = true;
        s.OfflineUsername = "parked-xp-test";
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.AutoPerformanceMode = false;
        s.GentleVisuals = false;
        var oldTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate(); Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromSeconds(1)); BeatLoop.StepAll();
            bool closed = false;
            shell.Closed += (_, _) => closed = true;
            if (minimized) shell.WindowState = WindowState.Minimized;
            else shell.Hide();
            if (minimized) Assert.Equal(WindowState.Minimized, shell.WindowState);
            else Assert.False(shell.IsVisible);
            Assert.False(closed);
            ProgressionBank.Add(amount, "Mantra"); Dispatcher.UIThread.RunJobs();
            Assert.False(closed);
            Assert.Equal(10 + amount, s.PlayerXP, 6);
            Assert.False(shell.XpReadoutCounting);
            var text = shell.FindControl<TextBlock>("TxtXP")!;
            Assert.StartsWith(expected, text.Text);
            // A later gate refresh must retain the same settled target.
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.StartsWith(expected, text.Text);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = oldTime;
            CoreSettings.ServiceProvider = previousService;
        }
        return Task.CompletedTask;
    });

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
        var (oldOffline, oldOfflineName) = (s.OfflineMode, s.OfflineUsername);
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
            // Tray Hide/Show re-fires Opened; the static motion-gate subscription must not stack.
            int Hooks()
            {
                var field = typeof(AmbientFxCanvas.Env).GetField("MotionGateChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var list = (field.GetValue(null) as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
                return list.Count(d => ReferenceEquals(d.Target, shell) && d.Method.Name == "RefreshHeroXpFx");
            }
            var hooks = Hooks();
            Assert.Equal(1, hooks);
            // Replay Opened as a tray Show does (a headless Hide/Show re-show throws here).
            typeof(Window).GetMethod("OnOpened", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(shell, new object[] { EventArgs.Empty });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(hooks, Hooks());
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
            (s.OfflineMode, s.OfflineUsername) = (oldOffline, oldOfflineName);
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
    private static void SaveFrame(MainShellWindow shell, string name)
    {
        // Proof frames are opt-in: a normal run writes nothing.
        var dir = Environment.GetEnvironmentVariable("CCP_HERO_XP_PROOF_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        Dispatcher.UIThread.RunJobs();
        using var frame = shell.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(System.IO.Path.Combine(dir, name + ".png"));
    }
}
