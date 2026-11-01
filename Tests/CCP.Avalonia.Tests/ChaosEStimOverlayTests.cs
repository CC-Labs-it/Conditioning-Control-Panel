using System;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
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
    public void Equipped_run_precreates_hidden_windows_reuses_them_and_shutdown_closes_them() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var canShow = ChaosEStimOverlay.CanShow;
        var meta = ConditioningControlPanel.Services.Chaos.ChaosMeta.State;
        bool equipped = meta.ActiveLifetimeBoons.Contains("e_stim");
        bool hadLevel = meta.LifetimeBoonLevels.TryGetValue("e_stim", out int level);
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimOverlay.CanShow = () => true;
        try
        {
            meta.ActiveLifetimeBoons.Remove("e_stim");
            ChaosRunHost.StartRun();
            ChaosRunHost.Overlay!.FinishCountdown();
            Assert.Null(ChaosEStimOverlay.Active);
            ChaosRunHost.ForceShutdown();

            meta.ActiveLifetimeBoons.Add("e_stim");
            meta.LifetimeBoonLevels["e_stim"] = 1;
            ChaosRunHost.StartRun();
            Assert.Null(ChaosEStimOverlay.Active); // countdown has not completed
            ChaosRunHost.Overlay!.FinishCountdown();
            var bolt = Assert.IsType<ChaosEStimOverlay>(ChaosEStimOverlay.Active);
            var halo = Assert.IsType<ChaosEStimGlow>(ChaosEStimGlow.Active);
            Assert.False(bolt.IsVisible);
            Assert.False(halo.IsVisible);
            Assert.Equal(0, bolt.BoltCount);
            ChaosEStimOverlay.EnsureCreated();
            Assert.Same(bolt, ChaosEStimOverlay.Active);
            Assert.Same(halo, ChaosEStimGlow.Active);
            ChaosEStimOverlay.Strike(new[] { (new PixelPoint(100, 100), new PixelPoint(200, 200)) });
            ChaosEStimOverlay.Arm();
            Assert.Same(bolt, ChaosEStimOverlay.Active);
            Assert.Same(halo, ChaosEStimGlow.Active);
            Assert.True(bolt.IsVisible);
            Assert.True(halo.IsVisible);
            ChaosRunHost.ForceShutdown();
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);

            ChaosEStimOverlay.CanShow = () => false;
            ChaosEStimOverlay.EnsureCreated();
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosEStimOverlay.CloseActive();
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
            if (!equipped) meta.ActiveLifetimeBoons.Remove("e_stim");
            if (hadLevel) meta.LifetimeBoonLevels["e_stim"] = level;
            else meta.LifetimeBoonLevels.Remove("e_stim");
        }
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
