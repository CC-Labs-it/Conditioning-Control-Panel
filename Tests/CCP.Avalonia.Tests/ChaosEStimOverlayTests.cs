using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
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

    // WPF ChaosModeService.cs:453, ChaosEStimOverlay.cs:329/353/375/399.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equipped_run_precreates_hidden_windows_and_reuses_them(bool equipped) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var meta = ChaosMeta.State;
        bool had = meta.ActiveLifetimeBoons.Contains("e_stim");
        bool hadLevel = meta.LifetimeBoonLevels.TryGetValue("e_stim", out int level);
        meta.LifetimeBoonLevels["e_stim"] = 1;
        var canShow = ChaosEStimOverlay.CanShow;
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimOverlay.CanShow = () => true;
        try
        {
            ChaosRunHost.ForceShutdown();
            ChaosEStimOverlay.CloseActive();
            if (equipped) meta.ActiveLifetimeBoons.Add("e_stim");
            else meta.ActiveLifetimeBoons.Remove("e_stim");
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            Assert.Null(ChaosEStimGlow.Active); // countdown does not precreate
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            if (!equipped)
            {
                Assert.Null(ChaosEStimOverlay.Active);
                Assert.Null(ChaosEStimGlow.Active);
                return;
            }
            var bolt = Assert.IsType<ChaosEStimOverlay>(ChaosEStimOverlay.Active);
            var halo = Assert.IsType<ChaosEStimGlow>(ChaosEStimGlow.Active);
            Assert.False(bolt.IsVisible);
            Assert.False(halo.IsVisible);
            Assert.False(halo.IsFollowing);
            ChaosEStimOverlay.EnsureCreated();
            ChaosEStimOverlay.Arm();
            Assert.Same(halo, ChaosEStimGlow.Active);
            Assert.True(halo.IsFollowing);
            ChaosEStimOverlay.Disarm();
            ChaosEStimOverlay.Strike(new[] { (new PixelPoint(1, 1), new PixelPoint(10, 10)) });
            Assert.Same(bolt, ChaosEStimOverlay.Active);
            ChaosRunHost.RequestStop();
            Assert.Null(ChaosEStimOverlay.Active);
            Assert.Null(ChaosEStimGlow.Active);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosEStimOverlay.CloseActive();
            if (had) meta.ActiveLifetimeBoons.Add("e_stim"); else meta.ActiveLifetimeBoons.Remove("e_stim");
            if (hadLevel) meta.LifetimeBoonLevels["e_stim"] = level; else meta.LifetimeBoonLevels.Remove("e_stim");
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
        }
    });

    // WPF ChaosWindowZ.cs:35-60, ChaosModeService.cs:745-762.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Video_start_and_handled_click_restack_only_live_pinned_windows(bool pinned) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var settings = CoreSettings.Current;
        bool previousPin = settings.ChaosPinOnTop;
        var canShow = ChaosEStimOverlay.CanShow;
        var raise = ChaosEStimOverlay.RaiseWindow;
        var raised = new List<Window>();
        ChaosEStimOverlay.SkipPlatformChecksForTests = true;
        ChaosEStimOverlay.CanShow = () => true;
        ChaosEStimOverlay.RaiseWindow = raised.Add;
        var video = new Window();
        try
        {
            ChaosRunHost.ForceShutdown();
            ChaosEStimOverlay.CloseActive();
            settings.ChaosPinOnTop = pinned;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosEStimOverlay.EnsureCreated();
            var bolt = ChaosEStimOverlay.Active!;
            var halo = ChaosEStimGlow.Active!;
            Assert.Equal(pinned, bolt.Topmost);
            Assert.Equal(pinned, halo.Topmost);
            MandatoryVideoOverlay.RestoreChaosLayer();
            Assert.Empty(raised); // precreation must not unhide or raise idle windows
            ChaosEStimOverlay.Arm();
            ChaosEStimOverlay.Strike(new[] { (new PixelPoint(1, 1), new PixelPoint(10, 10)) });
            raised.Clear();
            MandatoryVideoOverlay.RestoreChaosLayer();
            Assert.Equal(pinned ? new Window[] { bolt, halo } : Array.Empty<Window>(), raised);
            raised.Clear();
            MandatoryVideoOverlay.WireChaosRestacking(video);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            video.RaiseEvent(new PointerPressedEventArgs(video, pointer, video, new Point(1, 1), 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
                KeyModifiers.None, 1) { Handled = true });
            Assert.Equal(pinned ? new Window[] { bolt, halo } : Array.Empty<Window>(), raised);
            ChaosEStimOverlay.Disarm();
            raised.Clear();
            ChaosEStimOverlay.RaiseActive();
            Assert.DoesNotContain(halo, raised);
            ChaosRunHost.RequestStop();
            raised.Clear();
            MandatoryVideoOverlay.RestoreChaosLayer();
            Assert.Empty(raised); // recap is outside the gameplay band
        }
        finally
        {
            video.Close();
            ChaosRunHost.ForceShutdown();
            ChaosEStimOverlay.CloseActive();
            settings.ChaosPinOnTop = previousPin;
            ChaosEStimOverlay.CanShow = canShow;
            ChaosEStimOverlay.RaiseWindow = raise;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unpinned_preparation_never_requests_native_topmost(bool pinned) => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var click = ChaosEStimOverlay.MakeClickThrough;
        var pin = ChaosEStimOverlay.PinWindow;
        bool skip = ChaosEStimOverlay.SkipPlatformChecksForTests;
        int clicks = 0, pins = 0;
        var w = new Window { Topmost = pinned };
        try
        {
            ChaosEStimOverlay.SkipPlatformChecksForTests = false;
            ChaosEStimOverlay.MakeClickThrough = _ => { clicks++; return true; };
            ChaosEStimOverlay.PinWindow = (_, _) => { pins++; return true; };
            Assert.True(ChaosEStimOverlay.PrepareWindow(w, new PixelRect(20, 30, 92, 92)));
            Assert.Equal(1, clicks);
            Assert.Equal(pinned ? 1 : 0, pins);
            if (!pinned) Assert.Equal(new PixelPoint(20, 30), w.Position);
            // A refused click-through must never be reported as safe to show.
            ChaosEStimOverlay.MakeClickThrough = _ => false;
            Assert.False(ChaosEStimOverlay.PrepareWindow(w, new PixelRect(20, 30, 92, 92)));
        }
        finally
        {
            w.Close();
            ChaosEStimOverlay.MakeClickThrough = click;
            ChaosEStimOverlay.PinWindow = pin;
            ChaosEStimOverlay.SkipPlatformChecksForTests = skip;
        }
    });

    [Fact]
    public void Video_builder_keeps_start_and_click_callers()
    {
        // Fail-proof for the native video path without starting a decoder in a headless test.
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("repository root missing");
        var source = File.ReadAllText(Path.Combine(root, "CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs"));
        Assert.Contains("WireChaosRestacking(w);", source);
        Assert.Contains("RestoreChaosLayer();\n            return new Surface", source);
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
