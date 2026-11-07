using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Linux beta fix round, lane perf (owner, WSLg on the CPU, 2026-11-06): a CPU-only Skia backend takes WPF's
/// Performance mode tier (the marquee keeps scrolling), and the ambient governor stops flapping between
/// fog-only and recovered.
/// </summary>
public sealed class SoftwareRenderTierTests
{
    [Theory]
    [InlineData(PerformanceTier.Quality, null, PerformanceTier.Quality)]
    [InlineData(PerformanceTier.Quality, false, PerformanceTier.Quality)]
    [InlineData(PerformanceTier.Quality, true, PerformanceTier.Performance)]
    [InlineData(PerformanceTier.Balanced, true, PerformanceTier.Performance)]
    [InlineData(PerformanceTier.Performance, false, PerformanceTier.Performance)]   // PerformanceMode unchanged
    public void SoftwareRenderingTakesThePerformanceTier(PerformanceTier tier, bool? software, PerformanceTier expected)
        => Assert.Equal(expected, AmbientFxCanvas.Env.WithSoftwareFloor(tier, software));

    [Theory]
    [InlineData("1", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("maybe", null)]
    public void OverrideParses(string? value, bool? expected) => Assert.Equal(expected, SoftwareRender.ParseOverride(value));

    [Theory]
    [InlineData(LighterEffectsMode.Auto, null, false)]
    [InlineData(LighterEffectsMode.Auto, false, false)]
    [InlineData(LighterEffectsMode.Auto, true, true)]
    [InlineData(LighterEffectsMode.On, false, true)]
    [InlineData(LighterEffectsMode.On, null, true)]
    [InlineData(LighterEffectsMode.Off, true, false)]
    public void LighterEffectsSettingDecides(LighterEffectsMode mode, bool? detected, bool applied)
        => Assert.Equal(applied, SoftwareRender.Decide(mode, detected));

    /// <summary>Settings ▸ Performance ▸ Lighter effects (owner decision): Auto / On / Off, seeded from the
    /// settings, persisted, and live through the motion gate.</summary>
    [Fact]
    public Task LighterEffectsComboDrivesTheTierLive() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.LighterEffects, s.PerformanceMode, s.AutoPerformanceMode, s.MotionLevel);
        (s.PerformanceMode, s.AutoPerformanceMode, s.MotionLevel) = (false, false, MotionLevel.Full);
        s.LighterEffects = LighterEffectsMode.Off;
        SoftwareRender.ResetForTests();
        int gates = 0;
        void OnGate() => gates++;
        AmbientFxCanvas.Env.MotionGateChanged += OnGate;
        Window? w = null;
        try
        {
            var section = new global::ConditioningControlPanel.Avalonia.Views.Controls.AppSettings.PerformanceSettingsSection();
            w = new Window { Content = section, Width = 700, Height = 1400 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var combo = section.FindControl<ComboBox>("CmbLighterEffects")!;
            Assert.Equal(2, combo.SelectedIndex);                       // seeded from the file: Off
            SoftwareRender.Report(true);                                 // the probe says CPU...
            Assert.Equal(PerformanceTier.Quality, AmbientFxCanvas.Env.CurrentTier);   // ...but Off wins
            gates = 0;
            combo.SelectedIndex = 0;                                     // Auto: follow the probe
            Assert.Equal(LighterEffectsMode.Auto, s.LighterEffects);
            Assert.Equal(1, gates);
            Assert.Equal(PerformanceTier.Performance, AmbientFxCanvas.Env.CurrentTier);
            SoftwareRender.ResetForTests();                              // a GPU machine
            Assert.Equal(PerformanceTier.Quality, AmbientFxCanvas.Env.CurrentTier);
            combo.SelectedIndex = 1;                                     // On: forced
            Assert.Equal(LighterEffectsMode.On, s.LighterEffects);
            Assert.Equal(PerformanceTier.Performance, AmbientFxCanvas.Env.CurrentTier);
            Assert.True(AmbientFxCanvas.Env.SoftwareFloorHoldsIdleLoops);
        }
        finally
        {
            AmbientFxCanvas.Env.MotionGateChanged -= OnGate;
            w?.Close();
            SoftwareRender.ResetForTests();
            (s.LighterEffects, s.PerformanceMode, s.AutoPerformanceMode, s.MotionLevel) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void FogOnlyHasHysteresis()
    {
        Assert.False(AmbientFxCanvas.NextFogOnly(false, 11.9, 100));
        Assert.True(AmbientFxCanvas.NextFogOnly(false, 12.0, 0));
        Assert.True(AmbientFxCanvas.NextFogOnly(true, 1.0, 4.9));    // held for the minimum time
        Assert.True(AmbientFxCanvas.NextFogOnly(true, 11.0, 60));    // just under the drop line is not recovered
        Assert.False(AmbientFxCanvas.NextFogOnly(true, 3.9, 5.0));
    }

    /// <summary>The owner's log: a render thread that stays slow keeps the score on the drop line, and the
    /// old single threshold flipped several times a second. Replays the governor's own score arithmetic.</summary>
    [Fact]
    public void ASteadilySlowRenderThreadFlipsOnceNotEverySecond()
    {
        int old = Flips((_, score, _) => score >= 12);
        Assert.True(old > 20, $"the replay must reproduce the flapping ({old} flips)");
        Assert.Equal(1, Flips(AmbientFxCanvas.NextFogOnly));

        static int Flips(Func<bool, double, double, bool> rule)
        {
            double score = 0, since = 0;
            bool fog = false;
            int flips = 0;
            var rng = new Random(7);
            for (int tick = 0; tick < 30 * 60; tick++)          // a minute of 30 fps ticks
            {
                double now = tick / 30.0;
                score = Math.Max(0, score * 0.93);
                if (rng.NextDouble() < 0.85) score += 1.0;       // most frames over 2.5x the budget
                bool next = rule(fog, score, now - since);
                if (next != fog) { fog = next; since = now; flips++; }
            }
            return flips;
        }
    }

    [Fact]
    public Task DisarmedProbeLeavesHeadlessWindowsAlone() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        SoftwareRender.ResetForTests();
        var host = new Border();
        var w = new Window { Width = 80, Height = 80, Content = host };
        w.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var clock = new FrameClock(host) { Interval = TimeSpan.FromMilliseconds(1) };
            clock.Start();
            Assert.Null(ElementComposition.GetElementChildVisual(w));
            Assert.Null(SoftwareRender.Active);
            clock.Stop();
        }
        finally { w.Close(); SoftwareRender.ResetForTests(); }
        return Task.CompletedTask;
    });

    /// <summary>Headless Skia draws on the CPU, so an armed probe must answer "software" from a real frame,
    /// and a canvas built before the answer re-reads its budget at the Performance tier (and stops ticking),
    /// while the marquee's gate still lets it scroll.</summary>
    [Fact]
    public Task ArmedProbeDetectsCpuSkiaAndCanvasesRereadTheirTier() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.PerformanceMode = false;
        CoreSettings.Current.AutoPerformanceMode = false;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        SoftwareRender.ResetForTests();
        var canvas = new AmbientFxCanvas();
        var host = new Border();
        var w = new Window { Width = 120, Height = 120, Content = new Grid { Children = { canvas, host } } };
        bool? seen = null;
        void OnDetected(bool sw) => seen = sw;
        SoftwareRender.Detected += OnDetected;
        w.Show();
        try
        {
            Assert.Null(SoftwareRender.Active);   // CCP_SOFTWARE_RENDER must not be set for this test
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PerformanceTier.Quality, canvas.Tier);
            Assert.True(AmbientFxCanvas.Env.AllowMarqueeScroll);
            SoftwareRender.Armed = true;
            var clock = new FrameClock(host) { Interval = TimeSpan.FromMilliseconds(1) };
            clock.Tick += (_, _) => host.InvalidateVisual();
            clock.Start();
            Assert.NotNull(ElementComposition.GetElementChildVisual(w));
            for (int i = 0; i < 20 && seen == null; i++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(seen, "headless Skia is a raster (CPU) surface");
            Assert.True(SoftwareRender.Active);
            Assert.Equal(PerformanceTier.Performance, AmbientFxCanvas.Env.CurrentTier);
            Assert.Equal(PerformanceTier.Performance, canvas.Tier);
            Assert.False(AmbientFxCanvas.Env.AllowAmbientLoops);
            Assert.True(AmbientFxCanvas.Env.AllowMarqueeScroll);
            CoreSettings.Current.PerformanceMode = true;                 // the user's own Performance mode
            Assert.False(AmbientFxCanvas.Env.AllowMarqueeScroll);       // still parks it, as in WPF
            CoreSettings.Current.PerformanceMode = false;
            Assert.Null(ElementComposition.GetElementChildVisual(w));   // the probe visual left
            clock.Stop();
        }
        finally
        {
            SoftwareRender.Detected -= OnDetected;
            w.Close();
            SoftwareRender.ResetForTests();
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });

    private static void EnsureApp()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
