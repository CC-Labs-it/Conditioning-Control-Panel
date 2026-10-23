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
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.ChromeFx.cs ApplyStartButtonGlow / SweepStartSheen / ApplyXpSheen: the START glow
/// breathes 0.32-0.72 over 4.6 s, the START sheen passes every 8 s, the XP gloss travels, all on the shared
/// beat and only while the window is focused with ambient loops allowed; Off hides the glow outright.</summary>
public sealed class ChromeAmbientLoopsTests
{
    [Fact]
    public void StartGlowSheenAndXpGlossFollowTheChromeGate() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldClock = BeatLoop.Time;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var clock = new Clock();
        BeatLoop.Time = clock;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            shell.UpdateLayout();
            var glow = shell.Named<Border>("StartGlowLayer")!;
            var band = shell.Named<Border>("StartSheen")!;
            var host = shell.Named<Grid>("StartSheenHost")!;
            var gloss = shell.Named<Border>("XPBarSheen")!;
            var stops = ((LinearGradientBrush)gloss.Background!).GradientStops;
            Assert.True(shell.ChromeAmbientAllowed);
            Assert.True(glow.IsVisible);
            Assert.Null(glow.Effect);                      // halo is a BoxShadow, not a re-blurred Effect (P82)
            Assert.Equal(22, glow.BoxShadow[0].Blur);
            Assert.True(shell.StartChromeBreathing);
            Assert.True(shell.XpGlossRunning);
            Assert.Equal(1, gloss.Opacity);
            Assert.Equal(0.32, glow.Opacity, 5);

            void Step(double seconds) { clock.Now += TimeSpan.FromSeconds(seconds).Ticks; BeatLoop.StepAll(); }
            Step(4.6);                                     // breath peak
            Assert.Equal(0.72, glow.Opacity, 5);
            Assert.Equal(0, band.Opacity);                 // no sheen before the first 8 s tick
            Assert.Equal(-0.30 + 1.30 * 4.6 / 6.0, stops[0].Offset, 5);
            Step(8.0 + 0.3 - 4.6);                         // 0.3 s into the first sheen pass
            Assert.Equal(0.30, band.Opacity, 5);
            Assert.True(host.Bounds.Width > 0);
            Step(0.6);                                     // pass over
            Assert.Equal(0, band.Opacity);

            // A session runs (the CTA is STOP): the pass is skipped. The flag only; no real engine.
            var running = typeof(CoreEngine).GetField("_running", BindingFlags.NonPublic | BindingFlags.Static)!;
            running.SetValue(null, true);
            try
            {
                Step(8.0 - 0.6);                           // 16.3 s: second pass skipped
                Assert.Equal(0, band.Opacity);
            }
            finally { running.SetValue(null, false); }

            s.MotionLevel = MotionLevel.Reduced;           // ambient stops, glow rests at its floor
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell.StartChromeBreathing);
            Assert.False(shell.XpGlossRunning);
            Assert.True(glow.IsVisible);
            Assert.Equal(0.32, glow.Opacity, 5);
            Assert.Equal(0, gloss.Opacity);
            Assert.Equal(-0.30, stops[0].Offset, 5);

            s.MotionLevel = MotionLevel.Off;               // glow layer collapsed outright
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(glow.IsVisible);

            s.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(shell.StartChromeBreathing);
            s.MotionLevel = MotionLevel.Off;               // Full -> Off -> Reduced: the glow rests at its floor again
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            s.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(glow.IsVisible);
            Assert.Equal(0.32, glow.Opacity, 5);

            s.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(shell.StartChromeBreathing);
            shell.WindowState = WindowState.Minimized;     // focus/minimise parks every loop
            Assert.False(shell.StartChromeBreathing);
            Assert.False(shell.XpGlossRunning);
        }
        finally
        {
            shell.Close();
            CoreSettings.ServiceProvider = oldSettings;
            BeatLoop.Time = oldClock;
        }
    });

    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero).AddTicks(Now);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
