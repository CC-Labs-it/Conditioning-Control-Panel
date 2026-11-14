using System;
using System.Reflection;
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

/// <summary>Restack b222 (hand-port of #4283 #4308 #4631): the banner's beat pop, ring flash, support
/// sparkles and glow breath (WPF MainWindow.BannerFx.cs:166-433). Time is a stepped clock, never a sleep.</summary>
public sealed class BannerFxTests
{
    private static void Run(MotionLevel motion, bool gentle, Action<MainShellWindow, Clock> check) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldClock = BeatLoop.Time;
        var oldProbe = UiMotionPreference.OsProbe;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.MotionLevel = motion;
        s.GentleVisuals = gentle;
        s.PerformanceMode = false;
        s.AutoPerformanceMode = false;
        s.OfflineMode = true;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var clock = new Clock();
        BeatLoop.Time = clock;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();
            check(shell, clock);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = oldClock;
            UiMotionPreference.OsProbe = oldProbe;
            UiMotionPreference.CacheStamp = long.MinValue;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    private static TextBlock Tick(MainShellWindow shell)
    {
        var index = (int)typeof(MainShellWindow).GetField("_bannerCurrentIndex", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        var beats = (TextBlock[])typeof(MainShellWindow).GetField("_bannerBeats", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
        var incoming = beats[(index + 1) % beats.Length];
        shell.BannerRotationTimer_Tick(null, EventArgs.Empty);
        return incoming;
    }

    private static TextBlock TickTo(MainShellWindow shell, Clock clock, string name)
    {
        for (int i = 0; i < 6; i++)
        {
            var beat = Tick(shell);
            if (beat.Name == name) return beat;
            clock.Step(2);
        }
        throw new InvalidOperationException(name + " never came round");
    }

    // WPF MainWindow.BannerFx.cs:236-290, 322-344: a rolled beat only widens; the ring and glow flare and return.
    [Fact]
    public void FullMotionBeatChangePopsWidthFlashesTheRingAndSettles() => Run(MotionLevel.Full, false, (shell, clock) =>
    {
        var beat = TickTo(shell, clock, "TxtBannerSecondary");
        var group = Assert.IsType<TransformGroup>(beat.RenderTransform);   // the drum rolled it in
        var scale = Assert.IsType<ScaleTransform>(group.Children[0]);
        Assert.True(shell.BannerPopping);
        Assert.Equal(MainShellWindow.BannerPopFrom, scale.ScaleX, 3);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(group.Children[1]).X);
        var ring = shell.Named<Border>("BannerFlashRing")!;
        var glow = shell.Named<Border>("HeaderBannerGlowHost")!;
        Assert.True(shell.BannerFlashing);
        clock.Step(.07);
        Assert.Equal(.95, ring.Opacity, 3);
        clock.Step(.02);
        Assert.Equal(MainShellWindow.BannerGlowFlareBlur, glow.BoxShadow[0].Blur, 3);
        clock.Step(.2);
        Assert.False(shell.BannerPopping);
        Assert.Equal(1, scale.ScaleX);
        clock.Step(.2);
        Assert.False(shell.BannerFlashing);
        Assert.Equal(0, ring.Opacity);
        Assert.Equal(MainShellWindow.BannerGlowBlur, glow.BoxShadow[0].Blur);
        Assert.Equal(0, shell.BannerSparkleSprites);   // only the support beat sparkles
    });

    // WPF PopBannerBeat(popY: true) without a roll: both axes, and the pop hands the transform back.
    [Fact]
    public void ReducedMotionPopsBothAxesAndClearsItsTransform() => Run(MotionLevel.Reduced, false, (shell, clock) =>
    {
        var beat = TickTo(shell, clock, "TxtBannerSecondary");
        var scale = Assert.IsType<ScaleTransform>(beat.RenderTransform);
        Assert.Equal(MainShellWindow.BannerPopFrom, scale.ScaleX, 3);
        Assert.Equal(MainShellWindow.BannerPopFrom, scale.ScaleY, 3);
        clock.Step(.13);
        Assert.InRange(scale.ScaleX, MainShellWindow.BannerPopFrom, 1.02);
        clock.Step(.2);
        Assert.Null(beat.RenderTransform);
        Assert.False(shell.BannerBreathing);   // the breath is ambient: Full motion only
    });

    // WPF RunBannerSparkles (:346): seven stars over the support beat, gone when the run ends.
    [Fact]
    public void SupportArrivalRunsOneSparkleRunThatRemovesItself() => Run(MotionLevel.Full, false, (shell, clock) =>
    {
        TickTo(shell, clock, "TxtBannerPrimary");
        Assert.Equal(MainShellWindow.BannerSparkleCount, shell.BannerSparkleSprites);
        clock.Step(1);
        Assert.Equal(MainShellWindow.BannerSparkleCount, shell.BannerSparkleSprites);
        clock.Step(1);
        Assert.Equal(0, shell.BannerSparkleSprites);
    });

    // House Gentle rule (GentleVisuals.RiseFloorMs): the 70 ms ring rise and the sparkle twinkle stay off.
    [Fact]
    public void GentleVisualsKeepThePopButNoRingOrSparkles() => Run(MotionLevel.Full, true, (shell, clock) =>
    {
        TickTo(shell, clock, "TxtBannerPrimary");
        Assert.True(shell.BannerPopping);
        Assert.False(shell.BannerFlashing);
        Assert.Equal(0, shell.Named<Border>("BannerFlashRing")!.Opacity);
        Assert.Equal(0, shell.BannerSparkleSprites);
    });

    [Fact]
    public void MotionOffOrAnInactiveWindowSkipsBeatFx() => Run(MotionLevel.Off, false, (shell, clock) =>
    {
        TickTo(shell, clock, "TxtBannerSecondary");
        Assert.False(shell.BannerPopping);
        Assert.False(shell.BannerFlashing);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
        Dispatcher.UIThread.RunJobs();
        TickTo(shell, clock, "TxtBannerPrimary");
        Assert.False(shell.BannerPopping);
        Assert.False(shell.BannerFlashing);
        Assert.Equal(0, shell.BannerSparkleSprites);
    });

    // WPF ApplyBannerFxLoops (:166): breath 0.25-0.6 over 3.2 s, the 12 s repeat on the support beat only,
    // parked to the 0.35 rest by focus loss and close.
    [Fact]
    public void GlowBreathesAndRepeatsSparklesOnlyUnderTheAmbientGate() => Run(MotionLevel.Full, false, (shell, clock) =>
    {
        var glow = shell.Named<Border>("HeaderBannerGlowHost")!;
        Assert.Null(glow.Effect);
        Assert.True(shell.BannerBreathing);
        clock.Step(0);
        Assert.Equal(MainShellWindow.BannerBreathMin, glow.Opacity, 3);
        clock.Step(MainShellWindow.BannerBreathSeconds);
        Assert.Equal(MainShellWindow.BannerBreathMax, glow.Opacity, 3);
        // The shell opens on the support beat: the repeat fires at 12 s with no rotation in between.
        clock.Step(MainShellWindow.BannerSparkleRepeatSeconds - MainShellWindow.BannerBreathSeconds + .01);
        Assert.Equal(MainShellWindow.BannerSparkleCount, shell.BannerSparkleSprites);
        clock.Step(2);
        Assert.Equal(0, shell.BannerSparkleSprites);
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.BannerBreathing);
        Assert.Equal(MainShellWindow.BannerGlowRest, glow.Opacity, 3);
        shell.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.BannerBreathing);
        shell.Close();
        Assert.False(shell.BannerBreathing);
    });

    private sealed class Clock : TimeProvider
    {
        private long _now;
        public override long GetTimestamp() => _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(double seconds) { _now += TimeSpan.FromSeconds(seconds).Ticks; BeatLoop.StepAll(); }
    }
}
