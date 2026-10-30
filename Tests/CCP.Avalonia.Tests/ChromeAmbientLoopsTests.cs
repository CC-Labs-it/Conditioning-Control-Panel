using System;
using System.Linq;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

// Adapted from 878d25f5dec6 ChromeAmbientLoopsTests; elapsed time never depends on sleeps.
public sealed class ChromeAmbientLoopsTests
{
    private static void Run(Action<MainShellWindow, Clock> check) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        s.AutoPerformanceMode = false;
        s.OfflineMode = true;
        var oldClock = BeatLoop.Time;
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
        }
    });

    // WPF MainWindow.ChromeFx.cs:535-568.
    [Fact]
    public void StartGlowBreathesOnTheSharedBeat() => Run((shell, clock) =>
    {
        var glow = shell.Named<Border>("StartGlowLayer")!;
        Assert.True(shell.StartChromeBreathing);
        Assert.True(glow.IsVisible);
        Assert.Null(glow.Effect);
        Assert.Equal(22, glow.BoxShadow[0].Blur);
        Assert.Equal(0.32, glow.Opacity, 5);
        clock.Step(4.6);
        Assert.Equal(0.72, glow.Opacity, 5);
        clock.Step(4.6);
        Assert.Equal(0.32, glow.Opacity, 5);
    });

    // WPF MainWindow.ChromeFx.cs:110-114,590-632.
    [Fact]
    public void StartSheenWaitsEightSecondsAndCompletesOnePass() => Run((shell, clock) =>
    {
        var band = shell.Named<Border>("StartSheen")!;
        var host = shell.Named<Grid>("StartSheenHost")!;
        Assert.True(host.Bounds.Width > 0);
        clock.Step(7);
        Assert.Equal(0, band.Opacity);
        clock.Step(1.3);
        Assert.Equal(0.30, band.Opacity, 5);
        var slide = ((TransformGroup)band.RenderTransform!).Children.OfType<TranslateTransform>().Single();
        double eased = (1 - Math.Cos(Math.PI * (0.3 / 0.85))) / 2;
        Assert.Equal(-band.Width + (host.Bounds.Width + band.Width * 1.25) * eased, slide.X, 4);
        clock.Step(0.6);
        Assert.Equal(0, band.Opacity);
    });

    // WPF MainWindow.ChromeFx.cs:595-598.
    [Fact]
    public void RunningSessionSuppressesTheStartSheen() => Run((shell, clock) =>
    {
        var running = typeof(CoreEngine).GetField("_running", BindingFlags.NonPublic | BindingFlags.Static)!;
        var before = CoreEngine.IsRunning;
        running.SetValue(null, true);
        try
        {
            clock.Step(8.3);
            Assert.Equal(0, shell.Named<Border>("StartSheen")!.Opacity);
        }
        finally { running.SetValue(null, before); }
    });

    // WPF MainWindow.ChromeFx.cs:725-752.
    [Fact]
    public void XpGlossTravelsAllThreeStopsAndRepeatsAfterSixSeconds() => Run((shell, clock) =>
    {
        var gloss = shell.Named<Border>("XPBarSheen")!;
        var stops = ((LinearGradientBrush)gloss.Background!).GradientStops;
        Assert.True(shell.XpGlossRunning);
        Assert.Equal(1, gloss.Opacity);
        clock.Step(3);
        for (int i = 0; i < 3; i++) Assert.Equal(-0.30 + i * 0.15 + 0.65, stops[i].Offset, 5);
        clock.Step(3);
        for (int i = 0; i < 3; i++) Assert.Equal(-0.30 + i * 0.15, stops[i].Offset, 5);
    });

    // WPF MainWindow.ChromeFx.cs:158-184,541-558,732-737.
    [Theory]
    [InlineData(MotionLevel.Reduced, false, true)]
    [InlineData(MotionLevel.Off, false, false)]
    [InlineData(MotionLevel.Full, true, false)]
    public void MotionAndPerformanceChangesParkChrome(MotionLevel motion, bool performance, bool glowVisible) => Run((shell, clock) =>
    {
        clock.Step(8.3);
        CoreSettings.Current.MotionLevel = motion;
        CoreSettings.Current.PerformanceMode = performance;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(shell.StartChromeBreathing);
        Assert.False(shell.XpGlossRunning);
        var glow = shell.Named<Border>("StartGlowLayer")!;
        Assert.Equal(glowVisible, glow.IsVisible);
        Assert.Equal(glowVisible ? 0.32 : 0, glow.Opacity, 5);
        Assert.Equal(0, shell.Named<Border>("StartSheen")!.Opacity);
        Assert.Equal(0, shell.Named<Border>("XPBarSheen")!.Opacity);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.True(shell.StartChromeBreathing);
        Assert.True(shell.XpGlossRunning);
    });

    // WPF MainWindow.ChromeFx.cs:106-108,130-159; closed shells must also release static hooks.
    [Fact]
    public void FocusMinimizeHideAndCloseStopChrome() => Run((shell, clock) =>
    {
        // Headless permits two active windows; deliver the platform callback explicitly.
        typeof(WindowBase).GetMethod("HandleDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shell, null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.StartChromeBreathing);
        Assert.False(shell.XpGlossRunning);
        shell.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.StartChromeBreathing);
        shell.WindowState = WindowState.Minimized;
        Assert.False(shell.StartChromeBreathing);
        Assert.False(shell.XpGlossRunning);
        shell.WindowState = WindowState.Normal;
        shell.Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.StartChromeBreathing);
        shell.Hide();
        Assert.False(shell.StartChromeBreathing);
        Assert.False(shell.XpGlossRunning);
        shell.Close();
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(shell.StartChromeBreathing);
        Assert.False(shell.XpGlossRunning);
        var hooks = (Delegate?)typeof(AmbientFxCanvas.Env).GetField("MotionGateChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        Assert.DoesNotContain(hooks?.GetInvocationList() ?? Array.Empty<Delegate>(), d => ReferenceEquals(d.Target, shell));
    });

    // WPF MainWindow.ChromeFx.cs:142-154: a mod switch re-tints chrome live.
    [Fact]
    public void ModPaletteRefreshRetintsTheStartHalo() => Run((shell, clock) =>
    {
        var before = AmbientFxCanvas.Env.GlowColor;
        try
        {
            Application.Current!.Resources["FxGlowColor"] = Colors.CornflowerBlue;
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colors.CornflowerBlue, shell.Named<Border>("StartGlowLayer")!.BoxShadow[0].Color);
        }
        finally { Application.Current!.Resources["FxGlowColor"] = before; }
    });

    private sealed class Clock : TimeProvider
    {
        private long _now;
        public override long GetTimestamp() => _now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(double seconds) { _now += TimeSpan.FromSeconds(seconds).Ticks; BeatLoop.StepAll(); }
    }
}
