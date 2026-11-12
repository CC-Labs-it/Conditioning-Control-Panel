using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>E-Stim lightning + cursor halo (WPF Chaos/ChaosEStimOverlay.cs): refused without a
/// compositor, strike timeline on a stepped clock, halo centred on the pointer, panic closes both.</summary>
public sealed class ChaosEStimOverlayTests
{
    [Fact]
    public void Refuses_without_compositor_strikes_fades_follows_and_panic_closes_both() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var clock = new SteppedClock();
        var (canShow, read) = (ChaosEStimOverlay.CanShow, ChaosEStimGlow.ReadPointer);
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimOverlay.Clock = clock;
        ChaosEStimGlow.ReadPointer = () => (new PixelPoint(500, 300), false, false);
        try
        {
            var bolt = new[] { (new PixelPoint(100, 100), new PixelPoint(300, 100)) };

            // No compositor: nothing is created (an opaque screen-sized block otherwise).
            ChaosEStimOverlay.CanShow = () => false;
            ChaosEStimOverlay.EnsureCreated();
            ChaosEStimOverlay.Strike(bolt);
            ChaosEStimOverlay.Arm();
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);

            ChaosEStimOverlay.CanShow = () => true;
            // A refused window is remembered for the run (no rebuild per strike); run end forgets it.
            ChaosEStimOverlay.Refused = true;
            ChaosEStimOverlay.EnsureCreated();
            ChaosEStimOverlay.Strike(bolt);
            ChaosEStimOverlay.Arm();
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);
            ChaosEStimOverlay.CloseActive();
            Assert.False(ChaosEStimOverlay.Refused);
            ChaosEStimOverlay.Strike(bolt);
            var w = ChaosEStimOverlay.Active!;
            Assert.True(w.IsVisible);
            Assert.Equal(1, w.BoltCount);
            Assert.Equal(0.85, w.StrikeAlpha, 6);
            clock.Now = TimeSpan.FromMilliseconds(160).Ticks;
            w.Step();
            Assert.Equal(0.425, w.StrikeAlpha, 6);
            clock.Now = TimeSpan.FromMilliseconds(200).Ticks;
            w.Step();
            Assert.False(w.IsVisible);   // idles hidden between strikes

            ChaosEStimOverlay.Arm();
            var g = ChaosEStimGlow.Active!;
            Assert.True(g.IsVisible);
            Assert.Equal(new PixelPoint(500, 300) - new PixelPoint(46, 46), g.Position);
            ChaosEStimOverlay.Disarm();
            Assert.False(g.IsVisible);
            ChaosEStimOverlay.Arm();

            PanicSurfaces.All.Single(s => s.Id == "chaos-estim").Stop(null);
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);
        }
        finally
        {
            ChaosEStimOverlay.CloseActive();
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
            ChaosEStimOverlay.Clock = TimeProvider.System;
            ChaosEStimGlow.ReadPointer = read;
        }
    });

    [Fact]
    public void Precreation_is_hidden_idle_reused_and_restack_never_shows_hidden_windows() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var canShow = ChaosEStimOverlay.CanShow;
        var raise = ChaosEStimOverlay.RaiseWindow;
        var raised = new System.Collections.Generic.List<global::Avalonia.Controls.Window>();
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimOverlay.CanShow = () => true;
        ChaosEStimOverlay.RaiseWindow = w => raised.Add(w);
        try
        {
            ChaosEStimOverlay.EnsureCreated();
            var bolt = ChaosEStimOverlay.Active!;
            var halo = ChaosEStimGlow.Active!;
            Assert.NotNull(bolt);
            Assert.NotNull(halo);
            Assert.False(bolt.IsVisible);
            Assert.False(halo.IsVisible);
            Assert.False(bolt.IsTicking);
            Assert.False(halo.IsTracking);
            Assert.Equal(0, bolt.BoltCount);
            ChaosEStimOverlay.EnsureCreated();
            Assert.Same(bolt, ChaosEStimOverlay.Active);
            Assert.Same(halo, ChaosEStimGlow.Active);
            ChaosEStimOverlay.RaiseActive();
            Assert.Empty(raised);
            ChaosEStimOverlay.Strike(new[] { (new PixelPoint(0, 0), new PixelPoint(100, 100)) });
            ChaosEStimOverlay.Arm();
            raised.Clear();
            ChaosEStimOverlay.RaiseActive();
            Assert.Equal(new global::Avalonia.Controls.Window[] { bolt, halo }, raised);
            Assert.Same(bolt, ChaosEStimOverlay.Active);
            Assert.Same(halo, ChaosEStimGlow.Active);
            ChaosEStimOverlay.Disarm();
            raised.Clear();
            ChaosEStimOverlay.RaiseActive();
            Assert.Equal(new global::Avalonia.Controls.Window[] { bolt }, raised);
            PanicSurfaces.All.Single(s => s.Id == "chaos-estim").Stop(null);
            Assert.False(bolt.IsTicking);
            Assert.False(halo.IsTracking);
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);
        }
        finally
        {
            ChaosEStimOverlay.CloseActive();
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.RaiseWindow = raise;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bolt_preparation_receives_the_birth_stacking_band(bool pinned) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var canShow = ChaosEStimOverlay.CanShow;
        var clickThrough = ChaosEStimOverlay.SetClickThrough;
        var overrideRedirect = ChaosEStimOverlay.SetOverrideRedirect;
        var skipChecks = ChaosEStimOverlay.SkipPlatformChecksForTests;
        var raise = ChaosEStimOverlay.RaiseWindow;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.ChaosPinOnTop = pinned;
        var calls = 0;
        ChaosEStimOverlay.CanShow = () => true;
        ChaosEStimOverlay.SkipPlatformChecksForTests = false;
        ChaosEStimOverlay.SetClickThrough = (_, _) => true;
        ChaosEStimOverlay.SetOverrideRedirect = (window, bounds, passive, topmost) =>
        {
            calls++;
            Assert.Equal(pinned, topmost);
            Assert.Equal(window.Topmost, topmost);
            return true;
        };
        ChaosEStimOverlay.RaiseWindow = _ => { };
        try
        {
            ChaosEStimOverlay.Strike(new[] { (new PixelPoint(0, 0), new PixelPoint(100, 100)) });
            Assert.NotNull(ChaosEStimOverlay.Active);
            Assert.Equal(1, calls);
        }
        finally
        {
            ChaosEStimOverlay.CloseActive();
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SetClickThrough = clickThrough;
            ChaosEStimOverlay.SetOverrideRedirect = overrideRedirect;
            ChaosEStimOverlay.SkipPlatformChecksForTests = skipChecks;
            ChaosEStimOverlay.RaiseWindow = raise;
            CoreSettings.ServiceProvider = oldProvider;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bolt_pinning_controls_birth_and_raises(bool pinned) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var canShow = ChaosEStimOverlay.CanShow;
        var raise = ChaosEStimOverlay.RaiseWindow;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.ChaosPinOnTop = pinned;
        var raised = new System.Collections.Generic.List<global::Avalonia.Controls.Window>();
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimOverlay.CanShow = () => true;
        ChaosEStimOverlay.RaiseWindow = w => raised.Add(w);
        try
        {
            ChaosEStimOverlay.EnsureCreated();
            var bolt = ChaosEStimOverlay.Active!;
            Assert.NotNull(bolt);
            Assert.Equal(pinned, bolt.Topmost);
            ChaosEStimOverlay.Strike(new[] { (new PixelPoint(0, 0), new PixelPoint(100, 100)) });
            Assert.Equal(pinned ? 1 : 0, raised.Count);
            if (pinned) Assert.Same(bolt, raised[0]);
            raised.Clear();
            ChaosEStimOverlay.RaiseActive();
            Assert.Equal(pinned ? 1 : 0, raised.Count);
            if (pinned) Assert.Same(bolt, raised[0]);
            settings.Current.ChaosPinOnTop = false;
            bolt.Topmost = true;
            raised.Clear();
            ChaosEStimOverlay.RaiseActive();
            Assert.False(bolt.Topmost);
            Assert.Empty(raised);
            bolt.Topmost = true;
            ChaosEStimOverlay.Strike(new[] { (new PixelPoint(0, 0), new PixelPoint(100, 100)) });
            Assert.False(bolt.Topmost);
            Assert.Empty(raised);
        }
        finally
        {
            ChaosEStimOverlay.CloseActive();
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.RaiseWindow = raise;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
            CoreSettings.ServiceProvider = oldProvider;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_start_precreates_only_for_equipped_estim_and_panic_closes_it(bool equipped) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var canShow = ChaosEStimOverlay.CanShow;
        var meta = global::ConditioningControlPanel.Services.Chaos.ChaosMeta.State;
        var oldActive = meta.ActiveLifetimeBoons;
        var oldLevels = meta.LifetimeBoonLevels;
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimOverlay.CanShow = () => true;
        meta.ActiveLifetimeBoons = equipped ? new() { "e_stim" } : new();
        meta.LifetimeBoonLevels = new() { ["e_stim"] = 1 };
        try
        {
            ChaosRunHost.StartRun(new() { DurationSec = 60, WaveCount = 3 });
            Assert.Null(ChaosEStimOverlay.Active);   // precreation belongs after the countdown
            ChaosRunHost.BeginRun();
            ChaosRunHost.StopTimerForTests();
            Assert.Equal(equipped, ChaosEStimOverlay.Active != null);
            Assert.Equal(equipped, ChaosEStimGlow.Active != null);
            if (equipped)
            {
                Assert.False(ChaosEStimOverlay.Active!.IsVisible);
                Assert.False(ChaosEStimGlow.Active!.IsVisible);
            }
            ChaosRunHost.ForceShutdown();
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosEStimOverlay.CloseActive();
            meta.ActiveLifetimeBoons = oldActive;
            meta.LifetimeBoonLevels = oldLevels;
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
        }
    });

    [Fact]
    public void Gentle_strike_stays_static_and_new_strike_replaces_its_timeline() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldCanShow = ChaosEStimOverlay.CanShow;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.GentleVisuals = true;
        CoreSettings.Current.GentleFadeSeconds = 2;
        var clock = new SteppedClock();
        ChaosEStimOverlay.Clock = clock;
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimOverlay.CanShow = () => true;
        try
        {
            var bolt = new[] { (new PixelPoint(100, 100), new PixelPoint(300, 100)) };
            ChaosEStimOverlay.Strike(bolt);
            var w = ChaosEStimOverlay.Active!;
            var points = w.FirstBoltPoints.ToArray();
            int rise = GentleVisuals.RiseMs(0), fade = GentleVisuals.FadeMs(80);
            Assert.Equal(0, w.StrikeAlpha);
            clock.Now = TimeSpan.FromMilliseconds(40).Ticks;
            w.Step();
            Assert.Equal(points, w.FirstBoltPoints);
            clock.Now = TimeSpan.FromMilliseconds(rise / 2.0).Ticks;
            w.Step();
            Assert.Equal(0.425, w.StrikeAlpha, 6);
            Assert.Equal(points, w.FirstBoltPoints);
            clock.Now = TimeSpan.FromMilliseconds(rise + 120 + fade / 2.0).Ticks;
            w.Step();
            Assert.Equal(0.425, w.StrikeAlpha, 6);
            Assert.Equal(points, w.FirstBoltPoints);
            ChaosEStimOverlay.Strike(bolt);
            Assert.Equal(0, w.StrikeAlpha);
            clock.Now += TimeSpan.FromMilliseconds(fade / 2.0).Ticks;
            w.Step();
            Assert.True(w.IsVisible);
            clock.Now += TimeSpan.FromMilliseconds(rise + 120 + fade).Ticks;
            w.Step();
            Assert.False(w.IsVisible);
        }
        finally
        {
            ChaosEStimOverlay.CloseActive();
            ChaosEStimOverlay.Clock = TimeProvider.System;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
            ChaosEStimOverlay.CanShow = oldCanShow;
            CoreSettings.ServiceProvider = oldProvider;
        }
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
