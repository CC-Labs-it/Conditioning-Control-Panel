using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF OverlayService.cs:1589 / SpiralLayer.cs:31: GIF spirals draw on the shared stage at
/// CompositorLayers.Spiral, a refused stage falls back to the classic window, and gentle fades reverse.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class SpiralSharedStageTests
{
    // 4x4, three 30 ms frames.
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    private static PixelRect _bounds;

    private static Task WithSpiral(bool stage, Func<Window, AppSettings, Func<Window?>, Func<int>, Task> body)
        => AvaloniaTestDispatcher.RunAsync(async () =>
        {
            SubliminalSilentHintTests.EnsureAvalonia();
            var dir = Directory.CreateTempSubdirectory("ccp-spiral-stage-");
            var gif = Path.Combine(dir.FullName, "tiny.gif");
            File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
            var provider = CoreSettings.ServiceProvider;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            var configure = SharedOverlayHost.Configure;
            var engine = CoreSession.IsEngineRunningProvider;
            Window? stageWindow = null;
            var acquired = 0;
            SharedOverlayHost.Configure = (w, b) => { _bounds = b; acquired++; stageWindow = w; w.Width = 300; w.Height = 200; return stage; };
            var host = new Window();
            try
            {
                host.Show();
                SpiralOverlay.CloseAll();
                if (SpiralOverlay.Decoding is { } pending) await pending;
                SpiralOverlay.SkipPlatformChecksForTests = true;
                CoreSession.IsEngineRunningProvider = () => true;
                (s.SpiralEnabled, s.SpiralPath, s.SpiralRandomize, s.SpiralOpacity, s.UnifiedOverlayHost) = (true, gif, false, 40, true);
                SpiralOverlay.Refresh(host);
                while (SpiralOverlay.Decoding is { IsCompleted: false } d) await d;
                await body(host, s, () => stageWindow, () => acquired);
            }
            finally
            {
                SubliminalOverlay.CloseAll();   // a failed body must not leak a hosted card's stage lease
                SpiralOverlay.CloseAll();
                SpiralOverlay.SkipPlatformChecksForTests = false;
                CoreSession.IsEngineRunningProvider = engine;
                SharedOverlayHost.Configure = configure;
                CoreSettings.ServiceProvider = provider;
                host.Close();
                dir.Delete(true);
            }
        });

    private static (Grid Root, Image Image) Hosted(Window stage)
    {
        var root = Assert.IsType<Grid>(Assert.Single(Assert.IsType<Grid>(stage.Content).Children));
        return (root, Assert.IsType<Image>(Assert.Single(root.Children)));
    }

    [Fact]
    public Task Gif_lands_on_the_stage_repaints_live_and_stop_releases_the_lease() => WithSpiral(true, async (host, s, stage, acquired) =>
    {
        Assert.True(SpiralOverlay.IsShowing && SpiralOverlay.IsAnimating);
        Assert.Empty(SpiralOverlay.Shown);
        var (root, image) = Hosted(stage()!);
        Assert.Equal(CompositorLayers.Spiral, root.ZIndex);
        Assert.Equal(SpiralFrames.Paint(0.4), image.Opacity, 10);

        var first = image.Source;
        SpiralOverlay.Tick();
        Assert.NotSame(first, image.Source);

        s.SpiralOpacity = 80;
        SpiralOverlay.Refresh(host);
        Assert.Equal(SpiralFrames.Paint(0.8), image.Opacity, 10);
        (s.GentleVisuals, s.GentleSpeed) = (true, 0.5);
        SpiralOverlay.Tick();
        Assert.Equal(TimeSpan.FromMilliseconds(60), SpiralOverlay.FrameDelay);
        s.GentleVisuals = false;
        Assert.Same(root, Hosted(stage()!).Root);
        Assert.Equal(1, acquired());

        SpiralOverlay.Stop();
        Assert.False(SpiralOverlay.IsShowing);
        Assert.False(stage()!.IsVisible);
        await Task.CompletedTask;
    });

    // WPF CompositorLayers: SpiralLayer (60) draws over SubliminalLayer (40) on one host, so a subliminal
    // shown later must join the spiral's stage (not raise its own window above it) and keep it alive.
    [Fact]
    public Task Subliminal_shares_the_spiral_stage_under_it_and_the_last_lease_closes_it() => WithSpiral(true, async (host, s, stage, acquired) =>
    {
        var spiralRoot = Hosted(stage()!).Root;
        Assert.True(SubliminalOverlay.ShowHosted(_bounds, "TEST", 0.8, TimeSpan.FromSeconds(5)));
        Assert.Equal(1, acquired());
        var canvas = Assert.IsType<Grid>(stage()!.Content);
        Assert.Equal(2, canvas.Children.Count);
        var card = Assert.Single(canvas.Children, c => c != spiralRoot);
        Assert.Equal(CompositorLayers.Subliminal, card.ZIndex);
        Assert.True(card.ZIndex < spiralRoot.ZIndex);

        SubliminalOverlay.CloseAll();
        Assert.True(stage()!.IsVisible);   // the spiral still holds its lease
        Assert.Same(spiralRoot, Assert.Single(canvas.Children));
        SpiralOverlay.Stop();
        Assert.False(stage()!.IsVisible);
        await Task.CompletedTask;
    });

    // WPF keeps tint, spiral and cards on one host: a card's raise cannot lift the spiral over an "over" tint
    // (OverlayService.RefreshPinkFilterOrder), and ReassertZOrder pins the host below a live video (#497).
    [Fact]
    public Task Stage_raise_keeps_an_over_tint_on_top_and_a_mid_video_stage_goes_under_the_video() => WithSpiral(true, async (host, s, stage, _) =>
    {
        var saved = (s.PinkFilterEnabled, s.PinkFilterUnderEffects);
        var (raiseTint, skipTint) = (PinkFilterOverlay.RaiseWindow, PinkFilterOverlay.SkipPlatformChecksForTests);
        var (raiseStage, stackBelow, videos) = (SharedOverlayHost.Raise, SharedOverlayHost.StackBelow, SharedOverlayHost.LiveVideos);
        var raised = new List<Window>();
        var stacked = new List<(Window, IReadOnlyList<Window>)>();
        var video = new Window();
        try
        {
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            PinkFilterOverlay.RaiseWindow = w => { raised.Add(w); return true; };
            SharedOverlayHost.Raise = w => { raised.Add(w); return true; };
            SharedOverlayHost.StackBelow = (w, v) => { stacked.Add((w, v)); return true; };
            (s.PinkFilterEnabled, s.PinkFilterUnderEffects) = (true, false);
            PinkFilterOverlay.Refresh(host);
            Assert.NotEmpty(PinkFilterOverlay.Shown);

            raised.Clear();
            Assert.True(SubliminalOverlay.ShowHosted(_bounds, "TEST", 0.8, TimeSpan.FromSeconds(5)));
            Assert.Equal(new Window[] { stage()! }.Concat(PinkFilterOverlay.Shown), raised);   // the tint ends on top
            Assert.Empty(stacked);

            SubliminalOverlay.CloseAll();
            SpiralOverlay.CloseAll();
            Assert.False(stage()!.IsVisible);
            raised.Clear();
            var live = new Window[] { video };
            SharedOverlayHost.LiveVideos = () => live;
            SpiralOverlay.Refresh(host);
            while (SpiralOverlay.Decoding is { IsCompleted: false } d) await d;
            Assert.True(stage()!.IsVisible);
            var (w, anchors) = stacked[0];
            Assert.Same(stage(), w);
            Assert.Same(live, anchors);
            Assert.All(stacked, e => Assert.Same(live, e.Item2));   // everything goes under the video
            Assert.Empty(raised);   // neither raised over the video nor the tint lifted over it
        }
        finally
        {
            PinkFilterOverlay.CloseAll();
            (s.PinkFilterEnabled, s.PinkFilterUnderEffects) = saved;
            (PinkFilterOverlay.RaiseWindow, PinkFilterOverlay.SkipPlatformChecksForTests) = (raiseTint, skipTint);
            (SharedOverlayHost.Raise, SharedOverlayHost.StackBelow, SharedOverlayHost.LiveVideos) = (raiseStage, stackBelow, videos);
            video.Close();
        }
    });

    // WPF compositor order: video > over-tint > spiral host (OverlayService.cs:3071,3389, RefreshPinkFilterOrder).
    // A stage created mid-video must not end up between the tint and the video, or the spiral covers the
    // tint once the video closes.
    [Fact]
    public Task Mid_video_stage_stays_under_an_over_tint_after_the_video_closes() => WithSpiral(true, async (host, s, stage, _) =>
    {
        var saved = (s.PinkFilterEnabled, s.PinkFilterUnderEffects);
        var (raiseTint, skipTint) = (PinkFilterOverlay.RaiseWindow, PinkFilterOverlay.SkipPlatformChecksForTests);
        var (raiseStage, stackBelow, videos) = (SharedOverlayHost.Raise, SharedOverlayHost.StackBelow, SharedOverlayHost.LiveVideos);
        var z = new List<Window>();   // bottom -> top
        bool Top(Window w) { z.Remove(w); z.Add(w); return true; }
        bool Below(Window w, IReadOnlyList<Window> anchors)
        {
            z.Remove(w);
            var i = z.FindIndex(anchors.Contains);
            if (i < 0) z.Add(w); else z.Insert(i, w);
            return true;
        }
        var video = new Window();
        try
        {
            SubliminalOverlay.CloseAll();
            SpiralOverlay.CloseAll();
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            PinkFilterOverlay.RaiseWindow = Top;
            SharedOverlayHost.Raise = Top;
            SharedOverlayHost.StackBelow = Below;
            (s.PinkFilterEnabled, s.PinkFilterUnderEffects) = (true, false);
            PinkFilterOverlay.Refresh(host);
            var tint = Assert.Single(PinkFilterOverlay.Shown);
            z.Add(tint);
            z.Add(video);   // a video plays over the tint
            var live = new Window[] { video };
            SharedOverlayHost.LiveVideos = () => live;

            // The new stage maps on top of everything, then Seat restacks it.
            var configure = SharedOverlayHost.Configure;
            SharedOverlayHost.Configure = (w, b) => { var ok = configure(w, b); z.Add(w); return ok; };
            try
            {
                SpiralOverlay.Refresh(host);
                while (SpiralOverlay.Decoding is { IsCompleted: false } d) await d;
            }
            finally { SharedOverlayHost.Configure = configure; }
            Assert.True(stage()!.IsVisible);
            Assert.Same(video, z[^1]);   // the video stays on top

            z.Remove(video);              // the video closes
            live = Array.Empty<Window>();
            Assert.True(z.IndexOf(tint) > z.IndexOf(stage()!), "the over tint must stay above the spiral stage");
        }
        finally
        {
            PinkFilterOverlay.CloseAll();
            (s.PinkFilterEnabled, s.PinkFilterUnderEffects) = saved;
            (PinkFilterOverlay.RaiseWindow, PinkFilterOverlay.SkipPlatformChecksForTests) = (raiseTint, skipTint);
            (SharedOverlayHost.Raise, SharedOverlayHost.StackBelow, SharedOverlayHost.LiveVideos) = (raiseStage, stackBelow, videos);
            video.Close();
        }
    });

    // WPF OverlayService.PulseOverlays pulses the spiral layer too: a spiral-only pulse with the spiral on
    // the stage (no classic window, tint off) must still double it.
    [Fact]
    public Task Overlay_pulse_doubles_a_hosted_spiral_with_the_tint_off() => WithSpiral(true, async (_, s, stage, _) =>
    {
        var saved = (s.PinkFilterEnabled, s.GentleVisuals);
        try
        {
            (s.PinkFilterEnabled, s.GentleVisuals) = (false, false);
            Assert.Empty(SpiralOverlay.Shown);
            var (_, image) = Hosted(stage()!);
            var before = image.Opacity;
            OverlayPulse.PulseOverlays();
            Assert.True(OverlayPulse.IsPulsing);
            Assert.True(image.Opacity > before);
            OverlayPulse.Cancel();
            Assert.Equal(before, image.Opacity, 10);
        }
        finally
        {
            OverlayPulse.Cancel();
            (s.PinkFilterEnabled, s.GentleVisuals) = saved;
        }
        await Task.CompletedTask;
    });

    [Fact]
    public Task Refused_stage_falls_back_to_the_classic_window() => WithSpiral(false, async (_, _, stage, acquired) =>
    {
        Assert.Equal(1, acquired());
        Assert.False(stage()!.IsVisible);
        Assert.IsType<SpiralOverlayWindow>(Assert.Single(SpiralOverlay.Shown));
        await Task.CompletedTask;
    });

    [Fact]
    public Task Gentle_fade_out_reverses_on_the_same_stage() => WithSpiral(true, async (host, s, stage, acquired) =>
    {
        (s.GentleVisuals, s.GentleFadeSeconds) = (true, 0.25);
        var (root, _) = Hosted(stage()!);
        s.SpiralEnabled = false;
        SpiralOverlay.Refresh(host);
        Assert.Equal(0, root.GetBaseValue(Visual.OpacityProperty).Value);
        s.SpiralEnabled = true;
        SpiralOverlay.Refresh(host);
        Assert.Equal(1, root.GetBaseValue(Visual.OpacityProperty).Value);
        await Task.Delay(400);   // past the fade: a non-reversed fade would have released the stage
        Assert.True(SpiralOverlay.IsShowing);
        Assert.True(stage()!.IsVisible);
        Assert.Same(root, Hosted(stage()!).Root);
        Assert.Equal(1, acquired());

        s.SpiralEnabled = false;
        SpiralOverlay.Refresh(host);
        await Task.Delay(400);
        Assert.False(SpiralOverlay.IsShowing);
        Assert.False(stage()!.IsVisible);
        s.GentleVisuals = false;
    });
}
