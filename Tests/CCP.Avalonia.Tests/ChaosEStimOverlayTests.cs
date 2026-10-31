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
    public void Prewarm_reuses_hidden_halo_and_raise_never_reveals_it() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var canShow = ChaosEStimOverlay.CanShow;
        var raise = ChaosEStimGlow.RaiseWindow;
        int raised = 0;
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        try
        {
            ChaosEStimOverlay.CanShow = () => false;
            ChaosEStimGlow.EnsureCreated();
            Assert.Null(ChaosEStimGlow.Active);
            ChaosEStimOverlay.CanShow = () => true;
            ChaosEStimGlow.RaiseWindow = w => { Assert.Same(ChaosEStimGlow.Active, w); raised++; return true; };
            ChaosEStimGlow.EnsureCreated();
            var halo = ChaosEStimGlow.Active;
            Assert.NotNull(halo);
            Assert.False(halo.IsVisible);
            Assert.Equal(1, halo.Opacity);
            ChaosEStimGlow.RaiseActive();
            Assert.Equal(0, raised);
            ChaosEStimGlow.EnsureCreated();
            Assert.Same(halo, ChaosEStimGlow.Active);
            ChaosEStimGlow.Arm();
            Assert.Same(halo, ChaosEStimGlow.Active);
            Assert.True(halo.IsVisible);
            Assert.Equal(1, raised);
            ChaosEStimGlow.EnsureCreated();
            Assert.True(halo.IsVisible);
            ChaosEStimGlow.RaiseActive();
            Assert.Equal(2, raised);
            ChaosEStimGlow.Disarm();
            ChaosEStimGlow.RaiseActive();
            Assert.Equal(2, raised);
            Assert.False(halo.IsVisible);
            ChaosEStimOverlay.CloseActive();
            Assert.Null(ChaosEStimGlow.Active);
        }
        finally
        {
            ChaosEStimOverlay.CloseActive();
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
            ChaosEStimGlow.RaiseWindow = raise;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_prewarms_only_an_equipped_estim_and_tears_it_down(bool equipped) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var canShow = ChaosEStimOverlay.CanShow;
        ChaosEStimOverlay.CanShow = () => true;
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        var meta = ConditioningControlPanel.Services.Chaos.ChaosMeta.State;
        bool wasActive = meta.ActiveLifetimeBoons.Contains("e_stim");
        bool hadLevel = meta.LifetimeBoonLevels.TryGetValue("e_stim", out int level);
        meta.ActiveLifetimeBoons.Remove("e_stim");
        meta.LifetimeBoonLevels["e_stim"] = 1;
        if (equipped) meta.ActiveLifetimeBoons.Add("e_stim");
        try
        {
            ChaosRunHost.StartRun(new ConditioningControlPanel.Services.Chaos.ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            Assert.Null(ChaosEStimGlow.Active);
            ChaosRunHost.Overlay!.FinishCountdown();
            Assert.Equal(equipped, ChaosEStimGlow.Active != null);
            if (equipped) Assert.False(ChaosEStimGlow.Active!.IsVisible);
            ChaosRunHost.ForceShutdown();
            Assert.Null(ChaosEStimGlow.Active);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosEStimOverlay.CloseActive();
            if (wasActive) meta.ActiveLifetimeBoons.Add("e_stim"); else meta.ActiveLifetimeBoons.Remove("e_stim");
            if (hadLevel) meta.LifetimeBoonLevels["e_stim"] = level; else meta.LifetimeBoonLevels.Remove("e_stim");
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
        }
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
