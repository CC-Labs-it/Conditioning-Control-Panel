using System;
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
