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
    [Fact]
    public Task PanicQuietSuppressesNoticesAndSoundButPreservesLevelVisuals() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        (s.PlayerLevel, s.PlayerXP, s.MotionLevel, s.PerformanceMode) = (10, 20, MotionLevel.Full, false);
        s.OfflineMode = true;
        s.OfflineUsername = "hero-quiet-test";
        int notices = 0, sounds = 0;
        ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => notices++;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { sounds++; return () => { }; };
        var oldTime = BeatLoop.Time;
        BeatLoop.Time = new SteppedTime();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            notices = sounds = 0;
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            ProgressionBank.Add(XpCurve.GetXPForLevel(10, XpCurve.EpochOf(s)), "Quest");
            Dispatcher.UIThread.RunJobs();
            Assert.True(ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced());
            Assert.Equal(11, s.PlayerLevel);
            Assert.True(shell.LevelChipPopping);
            Assert.StartsWith("0 / ", shell.FindControl<TextBlock>("TxtXP")!.Text);
            Assert.Equal(0, notices);
            Assert.Equal(0, sounds);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            BeatLoop.Time = oldTime;
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
