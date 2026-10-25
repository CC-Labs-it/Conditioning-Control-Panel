using System;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalSolidHostTests
{
    [Fact]
    public void Host_is_passive_shared_and_closes_at_last_release() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        Window? stage = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try
        {
            var a = SharedOverlayHost.Acquire(new PixelRect(0, 0, 300, 200))!;
            var b = SharedOverlayHost.Acquire(new PixelRect(0, 0, 300, 200))!;
            Assert.Same(a.Canvas, b.Canvas);
            Assert.False(stage!.ShowActivated);
            Assert.False(stage.IsHitTestVisible);
            a.Dispose(); a.Dispose();
            Assert.True(stage.IsVisible);
            b.Dispose();
            Assert.False(stage.IsVisible);
        }
        finally { SharedOverlayHost.Configure = configure; }
    });

    [Fact]
    public void Hosted_card_is_removed_by_stop_and_native_failure_allows_fallback() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        Window? stage = null;
        var bounds = new PixelRect(0, 0, 300, 200);
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try
        {
            Assert.True(SubliminalOverlay.ShowHosted(bounds, "TEST", 0.8, TimeSpan.FromSeconds(1)));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            Assert.Single(canvas.Children);
            SubliminalOverlay.CloseAll(); // existing engine/panic surface owns the host lease
            Assert.Empty(canvas.Children);
            Assert.False(stage.IsVisible);
            SharedOverlayHost.Configure = (_, _) => false;
            Assert.False(SubliminalOverlay.ShowHosted(bounds, "TEST", 0.8, TimeSpan.FromSeconds(1)));
        }
        finally { SubliminalOverlay.CloseAll(); SharedOverlayHost.Configure = configure; }
    });
}
