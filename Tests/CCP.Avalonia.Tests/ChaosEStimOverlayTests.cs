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
            ChaosEStimOverlay.Strike(bolt);
            ChaosEStimOverlay.Arm();
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);

            ChaosEStimOverlay.CanShow = () => true;
            // A refused window is remembered for the run (no rebuild per strike); run end forgets it.
            ChaosEStimOverlay.Refused = true;
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
