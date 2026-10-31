using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;
namespace CCP.Avalonia.Tests;
[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalSolidHostTests {
    [Fact]
    public System.Threading.Tasks.Task Hosted_card_fades_holds_and_retires() => AvaloniaTestDispatcher.RunAsync(async () => {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        Window? stage = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try {
            Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "TEST", 0.8, TimeSpan.FromMilliseconds(500))); var canvas = Assert.IsType<Grid>(stage!.Content); var card = Assert.Single(canvas.Children);
            Assert.Equal(0, card.Opacity);
            await System.Threading.Tasks.Task.Delay(150);
            Assert.Equal(0.8, card.Opacity, 3);
            await System.Threading.Tasks.Task.Delay(650);
            Assert.Empty(canvas.Children);
        }
        finally { SubliminalOverlay.CloseAll(); SharedOverlayHost.Configure = configure; }
    });
    [Fact]
    public void Host_is_passive_and_closes_on_release() => AvaloniaTestDispatcher.Run(() => {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        Window? stage = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try {
            var lease = SharedOverlayHost.Acquire(new PixelRect(0, 0, 300, 200))!; Assert.False(stage!.ShowActivated); Assert.False(stage.IsHitTestVisible); Assert.True(stage.IsVisible);
            lease.Dispose(); lease.Dispose();
            Assert.False(stage.IsVisible);
        }
        finally { SharedOverlayHost.Configure = configure; }
    });
    // WPF SubliminalService: solid mode uses the shared stage; focus requests and a live mandatory video
    // keep the classic cards. Show itself exits headless (no X11), so the decision it calls is pinned here.
    [Theory]
    [InlineData(true, false, 0, true)]
    [InlineData(false, false, 0, false)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, false, 1, false)]
    public void Show_routes_solid_mode_to_the_shared_stage_only_without_focus_or_video(bool solid, bool focus, int videos, bool hosted) =>
        Assert.Equal(hosted, SubliminalOverlay.UseSolidHost(solid, focus, videos));
    [Fact]
    public void Hosted_card_is_removed_by_stop_and_native_failure_allows_fallback() => AvaloniaTestDispatcher.Run(() => {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        Window? stage = null;
        var bounds = new PixelRect(0, 0, 300, 200); var raise = SharedOverlayHost.Raise; var raised = new List<Window>();
        SharedOverlayHost.Raise = w => { raised.Add(w); return true; };
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try {
            Assert.True(SubliminalOverlay.ShowHosted(bounds, "TEST", 0.8, TimeSpan.FromSeconds(1))); var canvas = Assert.IsType<Grid>(stage!.Content); Assert.Single(canvas.Children);
            var first = stage; var other = new Window(); other.Show(); raised.Clear(); Assert.True(SubliminalOverlay.ShowHosted(bounds, "AGAIN", 0.8, TimeSpan.FromSeconds(1))); Assert.Same(first, Assert.Single(raised));
            other.Close();
            SubliminalOverlay.CloseAll(); // existing engine/panic surface owns the host lease
            Assert.Empty(canvas.Children); Assert.False(stage.IsVisible);
            SharedOverlayHost.Configure = (_, _) => false;
            Assert.False(SubliminalOverlay.ShowHosted(bounds, "TEST", 0.8, TimeSpan.FromSeconds(1)));
        }
        finally { SubliminalOverlay.CloseAll(); SharedOverlayHost.Configure = configure; SharedOverlayHost.Raise = raise; }
    });
}
