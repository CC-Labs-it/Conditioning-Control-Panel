using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class BubbleSharedStageTests
{
    private static readonly PixelRect Bounds = new(0, 0, 300, 200);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bubbles_share_card_stage_keep_input_and_release_independently(bool cardFirst) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var (configure, transparent, raise) = (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent, SharedOverlayHost.Raise);
        var (regions, frame, schedule) = (BubbleOverlayWindow.SetInputRects, SubliminalOverlayWindow.RequestFrame, SubliminalOverlay.Schedule);
        Window? stage = null;
        PixelRect[] lastRects = Array.Empty<PixelRect>();
        var w = new BubbleOverlayWindow(0, Bounds, 1) { Width = 300, Height = 200 };
        SharedOverlayHost.Configure = (window, _) => { stage = window; window.Width = 300; window.Height = 200; return true; };
        SharedOverlayHost.IsTransparent = _ => true;
        SharedOverlayHost.Raise = _ => true;
        SubliminalOverlayWindow.RequestFrame = (_, _) => { };
        SubliminalOverlay.Schedule = (_, _) => { };
        BubbleOverlayWindow.SetInputRects = (_, rects, count) => { lastRects = rects.Take(count).ToArray(); return true; };
        var ambient = new AmbientBubble { Screen = 0, X = 10, Y = 10, Size = 40, Clickable = true };
        var chaos = new AmbientBubble { Screen = 0, X = 100, Y = 10, Size = 40, Clickable = true, Spec = new EffectBubbleSpec() };
        var other = new AmbientBubble { Screen = 1, X = 200, Y = 10, Size = 40, Clickable = true };
        BubbleOverlay.Field.Bubbles.Clear();
        BubbleOverlay.Field.Bubbles.AddRange(new[] { ambient, chaos, other });
        try
        {
            w.Show();
            Assert.True(SubliminalOverlay.ShowHosted(Bounds, "TEST", 0.8, TimeSpan.FromSeconds(5)));
            Assert.True(w.TryHost(true, Bounds));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            var card = Assert.Single(canvas.Children.OfType<Grid>());
            var bubbles = Assert.Single(canvas.Children, c => c.ZIndex == CompositorLayers.Bubbles);
            Assert.Equal(CompositorLayers.Subliminal, card.ZIndex);
            Assert.True(bubbles.ZIndex > card.ZIndex);
            Assert.Null(w.Content);
            w.Sync();
            Assert.Equal(new[] { new PixelRect(10, 10, 40, 40), new PixelRect(100, 10, 40, 40) }, lastRects);
            w.MouseDown(new Point(120, 30), MouseButton.Left);
            Assert.True(chaos.Popping);
            w.Sync();
            Assert.Equal(new[] { new PixelRect(10, 10, 40, 40) }, lastRects);
            w.MouseDown(new Point(30, 30), MouseButton.Right);
            Assert.True(ambient.Popping);
            w.Sync();
            Assert.Empty(lastRects);
            if (cardFirst)
            {
                SubliminalOverlay.CloseAll();
                Assert.True(stage.IsVisible);
                Assert.Same(bubbles, Assert.Single(canvas.Children));
                w.Close();
            }
            else
            {
                w.Close();
                Assert.True(stage.IsVisible);
                Assert.Same(card, Assert.Single(canvas.Children));
                SubliminalOverlay.CloseAll();
            }
            Assert.False(stage.IsVisible);
        }
        finally
        {
            w.Close(); SubliminalOverlay.CloseAll(); BubbleOverlay.Field.Bubbles.Clear();
            (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent, SharedOverlayHost.Raise) = (configure, transparent, raise);
            (BubbleOverlayWindow.SetInputRects, SubliminalOverlayWindow.RequestFrame, SubliminalOverlay.Schedule) = (regions, frame, schedule);
        }
    });

    [Fact]
    public void Disabled_outside_refused_and_closed_stage_keep_classic_drawing() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var (configure, transparent) = (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent);
        Window? stage = null;
        var w = new BubbleOverlayWindow(0, Bounds, 1) { Width = 300, Height = 200 };
        try
        {
            w.Show();
            var original = w.Content;
            SharedOverlayHost.Configure = (_, _) => false;
            Assert.False(w.TryHost(false, Bounds));
            Assert.False(w.TryHost(true, new PixelRect(300, 0, 300, 200)));
            Assert.False(w.TryHost(true, Bounds));
            Assert.Same(original, w.Content);
            SharedOverlayHost.Configure = (window, _) => { stage = window; window.Width = 300; window.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            Assert.True(w.TryHost(true, Bounds));
            stage!.Close();
            Assert.Same(original, w.Content);
        }
        finally
        {
            w.Close();
            (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent) = (configure, transparent);
        }
    });
}
