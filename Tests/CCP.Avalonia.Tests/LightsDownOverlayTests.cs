using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LightsDownOverlayTests
{
    [Fact]
    public void RefusesUnsafePlatformsBeforeMapping() => AvaloniaTestDispatcher.Run(() =>
    {
        var video = new Window();
        var shapeCalls = 0;
        Assert.False(LightsDownOverlay.TryOpen(video, new PixelRect(0, 0, 100, 100), 1,
            _ => false, _ => { shapeCalls++; return true; }));
        Assert.Equal(0, shapeCalls);
        Assert.False(LightsDownOverlay.TryOpen(video, new PixelRect(0, 0, 100, 100), 1,
            _ => true, _ => false));
        Assert.Equal(0, LightsDownOverlay.Count);
        video.Close();
    });

    [Fact]
    public void RefusesWhenWindowManagerCannotKeepVideoAbove() => AvaloniaTestDispatcher.Run(() =>
    {
        var video = new Window();
        var old = X11Overlay.WmCannotKeepAboveForTests;
        var shapeCalls = 0;
        try
        {
            X11Overlay.WmCannotKeepAboveForTests = () => true;
            Assert.False(LightsDownOverlay.TryOpen(video, new PixelRect(0, 0, 100, 100), 1,
                _ => true, _ => { shapeCalls++; return true; }, _ => true, (_, _) => true));
            Assert.Equal(0, shapeCalls);
            Assert.Equal(0, LightsDownOverlay.Count);
        }
        finally { X11Overlay.WmCannotKeepAboveForTests = old; LightsDownOverlay.CloseAll(); video.Close(); }
    });

    [Fact]
    public void RefusesTransparencyAndStackingFailures() => AvaloniaTestDispatcher.Run(() =>
    {
        var video = new Window();
        Assert.False(LightsDownOverlay.TryOpen(video, new PixelRect(0, 0, 100, 100), 1,
            _ => true, _ => true, _ => false));
        Assert.False(LightsDownOverlay.TryOpen(video, new PixelRect(0, 0, 100, 100), 1,
            _ => true, _ => true, _ => true, (_, _) => false));
        Assert.Equal(0, LightsDownOverlay.Count);
        video.Close();
    });

    [Fact]
    public void PanicClosesAndCloseAllIsIdempotent() => AvaloniaTestDispatcher.Run(() =>
    {
        var video = new Window();
        Window? overlay = null;
        try
        {
            Assert.True(LightsDownOverlay.TryOpen(video, new PixelRect(0, 0, 100, 100), 1,
                _ => true, w => { overlay = w; return true; }, _ => true, (_, _) => true));
            Assert.False(overlay!.ShowActivated);
            Assert.False(overlay.Focusable);
            Assert.False(overlay.IsHitTestVisible);
            var frame = new LightsDownState().Step(0.2, true, LightsDownMotion.Full, false);
            LightsDownOverlay.Apply(frame);
            Assert.Equal(frame.DimAlpha, overlay.Opacity);
            PanicSurfaces.All.Single(s => s.Id == "lights-down").Stop(null);
            Assert.Equal(0, LightsDownOverlay.Count);
            Assert.False(overlay.IsVisible);
            LightsDownOverlay.CloseAll();
            Assert.True(LightsDownOverlay.TryOpen(video, new PixelRect(0, 0, 100, 100), 1,
                _ => true, _ => true, _ => true, (_, _) => true));
            MandatoryVideoOverlay.Instance.CloseAll();
            Assert.Equal(0, LightsDownOverlay.Count);
        }
        finally { LightsDownOverlay.CloseAll(); video.Close(); }
    });
    [Fact]
    public void VideoTimerStopsOnPanicAndVideoTeardown() => AvaloniaTestDispatcher.Run(() =>
    {
        var video = new MandatoryVideoOverlay(null);
        try
        {
            LightsDownOverlay.StartVideo(video);
            Assert.True(LightsDownOverlay.VideoTimerRunning);
            PanicSurfaces.All.Single(s => s.Id == "lights-down").Stop(null);
            Assert.False(LightsDownOverlay.VideoTimerRunning);
            LightsDownOverlay.StartVideo(video);
            video.CloseAll();
            Assert.False(LightsDownOverlay.VideoTimerRunning);
        }
        finally { LightsDownOverlay.CloseAll(); video.CloseAll(); }
    });

    [Fact]
    public void EachVideoWindowGetsItsOwnDimWindowOnItsOwnScreen() => AvaloniaTestDispatcher.Run(() =>
    {
        // WPF LightsDownService.cs:60-67: one overlay per video window, on its screen, under it.
        var video = new MandatoryVideoOverlay(null);
        var surfaces = (System.Collections.Generic.List<MandatoryVideoOverlay.Surface>)video.Surfaces;
        var calls = new System.Collections.Generic.List<(Window Video, Window Below, PixelRect Bounds)>();
        try
        {
            var a = new PixelRect(0, 0, 1920, 1080);
            var b = new PixelRect(1920, 0, 1280, 1024);
            surfaces.Add(video.Build(MultiMonitorResultTests.MakeScreen(1, a, a, true), false));
            surfaces.Add(video.Build(MultiMonitorResultTests.MakeScreen(1, b, b, false), false));
            Assert.True(LightsDownOverlay.OpenForVideo(video, (v, bounds, scale) =>
                LightsDownOverlay.TryOpen(v, bounds, scale, _ => true, _ => true, _ => true,
                    (_, under) => { calls.Add((v, under, bounds)); return true; })));
            Assert.Equal(2, LightsDownOverlay.Count);
            Assert.Equal(new[] { surfaces[0].Window, surfaces[1].Window }, calls.Select(c => c.Below));
            Assert.All(calls, c => Assert.Same(c.Video, c.Below));
            Assert.Equal(new[] { a, b }, calls.Select(c => c.Bounds));
        }
        finally { LightsDownOverlay.CloseAll(); foreach (var s in surfaces) s.Window.Close(); surfaces.Clear(); }
    });

    [Fact]
    public void VideoFrameDrawsDimOutsidePictureAndStreakInside() => AvaloniaTestDispatcher.Run(() =>
    {
        using var frame = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(200, 100), new Vector(96, 96));
        var image = new Image { Source = frame };
        var light = new LightsDownOverlay.VideoLight(image) { IsHitTestVisible = false };
        light.Measure(new Size(100, 100));
        light.Arrange(new Rect(0, 0, 100, 100));
        Assert.Equal(new Rect(0, 25, 100, 50), LightsDownOverlay.VideoLight.Picture(new Size(100, 100), frame));
        light.SetFrame(new LightsDownFrame { DimAlpha = 0.5, StreakAlpha = 0.7, GlowPulse = 1 });
        using var rendered = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(100, 100));
        rendered.Render(light);
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            byte Alpha(int x, int y)
            {
                rendered.CopyPixels(new PixelRect(x, y, 1, 1), buffer, 4, 4);
                return System.Runtime.InteropServices.Marshal.ReadByte(buffer, 3);
            }
            Assert.InRange(Alpha(50, 10), 120, 130);
            Assert.Equal(0, Alpha(50, 40));
            Assert.InRange(Alpha(50, 51), 170, 185);
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
    });
}
