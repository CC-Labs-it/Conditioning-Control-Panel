using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
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
}
