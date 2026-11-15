using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF BubbleService.cs:2353/:3544: under the unified host bubbles draw on the shared stage at
/// CompositorLayers.Bubbles while the hook keeps the hit discs (:3033). Re-cut of #4260.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class BubbleSharedStageTests
{
    private static readonly PixelRect Bounds = new(0, 0, 300, 200);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bubbles_draw_on_stage_keep_input_pop_and_release_independently(bool cardFirst) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var s = CoreSettings.Current;
        var (dayKey, paid) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday);
        var play = CoreAudio.PlayOneShotProvider;
        CoreAudio.PlayOneShotProvider = (_, _, _, _, _) => { };
        Window? stage = null, cardStage = null;
        var w = new BubbleOverlayWindow(0, Bounds, 1) { Width = 300, Height = 200 };
        var ambient = new AmbientBubble { Screen = 0, X = 10, Y = 10, Size = 40, Clickable = true };
        var chaos = new AmbientBubble { Screen = 0, X = 100, Y = 10, Size = 40, Clickable = true, Spec = new EffectBubbleSpec() };
        BubbleOverlay.Field.Bubbles.Clear();
        BubbleOverlay.Field.Bubbles.AddRange(new[] { ambient, chaos });
        try
        {
            w.Show();
            SharedOverlayHost.Configure = (window, _) => { cardStage = window; window.Width = 300; window.Height = 200; return true; };
            Assert.True(SubliminalOverlay.ShowHosted(Bounds, "TEST", 0.8, TimeSpan.FromSeconds(5), (_, _) => { }));
            SharedOverlayHost.Configure = (window, _) => { stage = window; window.Width = 300; window.Height = 200; return true; };
            var layer = w.Content;
            Assert.True(w.TryHost(true, Bounds));
            Assert.Same(stage, w.StageWindow);
            var canvas = Assert.IsType<Grid>(stage!.Content);
            Assert.Same(layer, Assert.Single(canvas.Children));
            Assert.Equal(CompositorLayers.Bubbles, ((Control)layer!).ZIndex);
            Assert.True(CompositorLayers.Bubbles > CompositorLayers.Subliminal);
            Assert.Null(w.Content);   // the input window draws nothing
            w.Sync();
            var both = w.InputRectCount;   // ...but keeps the hit region of ambient and chaos bubbles
            Assert.True(both > 0);
            w.MouseDown(new Point(30, 30), MouseButton.Right);
            Assert.True(ambient.Popping);
            Assert.False(chaos.Popping);
            w.Sync();
            Assert.InRange(w.InputRectCount, 1, both - 1);
            Assert.All(w.Rects.Take(w.InputRectCount), r => Assert.True(r.X >= 100));   // only the chaos disc is left
            if (cardFirst)
            {
                SubliminalOverlay.CloseAll();
                Assert.False(cardStage!.IsVisible);
                Assert.True(stage.IsVisible);
                w.Close();
            }
            else
            {
                w.Close();
                Assert.True(cardStage!.IsVisible);
                SubliminalOverlay.CloseAll();
            }
            Assert.False(stage.IsVisible);
            Assert.Empty(canvas.Children);
        }
        finally
        {
            w.Close(); SubliminalOverlay.CloseAll(); BubbleOverlay.Field.Bubbles.Clear();
            SharedOverlayHost.Configure = configure;
            (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday) = (dayKey, paid);
            CoreAudio.PlayOneShotProvider = play;
        }
    });

    [Fact]
    public void Disabled_mismatched_refused_and_closed_stage_keep_classic_drawing() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        Window? stage = null;
        var w = new BubbleOverlayWindow(0, Bounds, 1) { Width = 300, Height = 200 };
        try
        {
            w.Show();
            var original = w.Content;
            SharedOverlayHost.Configure = (window, _) => { stage = window; return true; };
            Assert.False(w.TryHost(false, Bounds));
            Assert.False(w.TryHost(true, new PixelRect(300, 0, 300, 200)));
            Assert.Null(stage);
            SharedOverlayHost.Configure = (_, _) => false;
            Assert.False(w.TryHost(true, Bounds));
            Assert.Same(original, w.Content);
            SharedOverlayHost.Configure = (window, _) => { stage = window; window.Width = 300; window.Height = 200; return true; };
            Assert.True(w.TryHost(true, Bounds));
            Assert.Null(w.Content);
            stage!.Close();   // the stage goes mid-run: drawing falls back to the input window
            Assert.Same(original, w.Content);
            Assert.Null(w.StageWindow);
            Assert.True(w.TryHost(true, Bounds));   // a later stage can take it again
        }
        finally
        {
            w.Close();
            SharedOverlayHost.Configure = configure;
        }
    });
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Start_hosts_drawing_only_with_the_unified_host_and_Stop_releases_it(bool unified) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var (checks, setting) = (BubbleOverlay.SkipPlatformChecksForTests, CoreSettings.Current.UnifiedOverlayHost);
        var stages = new System.Collections.Generic.List<Window>();
        var host = new Window();
        try
        {
            BubbleOverlay.SkipPlatformChecksForTests = true;
            CoreSettings.Current.UnifiedOverlayHost = unified;
            SharedOverlayHost.Configure = (window, bounds) => { stages.Add(window); window.Width = bounds.Width; window.Height = bounds.Height; return true; };
            host.Show();
            BubbleOverlay.Start(host);
            Assert.True(BubbleOverlay.WindowCount > 0);
            var raised = new System.Collections.Generic.List<Window>();
            BubbleOverlay.RaiseAll(raised.Add);
            var inputs = raised.OfType<BubbleOverlayWindow>().ToArray();
            Assert.Equal(inputs.Length, BubbleOverlay.WindowCount);
            if (unified)
            {
                Assert.Equal(inputs.Length, stages.Count);
                // Each screen raises its stage first so the input window stays on top of the drawing.
                Assert.Equal(inputs.SelectMany(w => new[] { w.StageWindow!, w }), raised);
            }
            else
            {
                Assert.Empty(stages);
                Assert.All(inputs, w => Assert.NotNull(w.Content));
            }
            BubbleOverlay.Stop();
            Assert.All(stages, w => Assert.False(w.IsVisible));
        }
        finally
        {
            BubbleOverlay.Stop();
            host.Close();
            SharedOverlayHost.Configure = configure;
            (BubbleOverlay.SkipPlatformChecksForTests, CoreSettings.Current.UnifiedOverlayHost) = (checks, setting);
        }
    });
}
