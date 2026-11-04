using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class FlashSharedStageTests
{
    private sealed class Scheduled : IDisposable
    {
        internal Action? Callback;
        internal TimeSpan Delay;
        internal bool Cancelled;
        internal Scheduled(Action callback, TimeSpan delay) => (Callback, Delay) = (callback, delay);
        internal void Fire() { var callback = Callback; Callback = null; callback?.Invoke(); }
        public void Dispose() { Cancelled = true; Callback = null; }
    }

    [Theory]
    [InlineData(true, false, false, FlashMotionStyle.Still, true)]
    [InlineData(false, false, false, FlashMotionStyle.Still, false)]
    [InlineData(true, true, false, FlashMotionStyle.Still, false)]
    [InlineData(true, false, true, FlashMotionStyle.Still, false)]
    [InlineData(true, false, false, FlashMotionStyle.DriftBounce, false)]
    public void Only_passive_still_unglowed_flashes_use_stage(bool unified, bool clickable, bool glow, FlashMotionStyle motion, bool expected) =>
        Assert.Equal(expected, FlashOverlay.CanHost(unified, clickable, glow, motion));

    [Fact]
    public void Flashes_share_order_and_expire_independently() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var bounds = new PixelRect(0, 0, 300, 200);
        var configure = SharedOverlayHost.Configure;
        var transparent = SharedOverlayHost.IsTransparent;
        var schedule = FlashOverlay.ScheduleHosted;
        var actions = new List<Scheduled>();
        Window? stage = null;
        try
        {
            SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            FlashOverlay.ScheduleHosted = (action, delay) => { var timer = new Scheduled(action, delay); actions.Add(timer); return timer; };
            Assert.True(PinkFilterOverlay.ShowHosted(bounds, 1, 2, 3, 0.4));
            Assert.True(SubliminalOverlay.ShowHosted(bounds, "TEST", 0.8, TimeSpan.FromSeconds(5)));
            Assert.True(FlashOverlay.ShowHosted(new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96)), new PixelRect(10, 20, 40, 50), bounds, 1, 1, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            Assert.True(FlashOverlay.ShowHosted(new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96)), new PixelRect(60, 20, 40, 50), bounds, 1, 1, TimeSpan.Zero, TimeSpan.FromSeconds(2)));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            var flashes = canvas.Children.OfType<Image>().ToArray();
            Assert.Equal(2, flashes.Length);
            Assert.All(flashes, image => { Assert.False(image.IsHitTestVisible); Assert.Equal(CompositorLayers.Flash, image.ZIndex); });
            Assert.True(canvas.Children.OfType<Grid>().Single().ZIndex > flashes[0].ZIndex);
            Assert.True(canvas.Children.OfType<Border>().Single().ZIndex > flashes[0].ZIndex);
            Assert.False(stage.IsHitTestVisible);
            actions.Single(x => x.Delay == TimeSpan.FromSeconds(1)).Fire();
            actions.Last().Fire(); // zero-length fade completes cleanup
            Assert.Single(FlashOverlay.Hosted);
            Assert.True(stage.IsVisible);
            FlashOverlay.CloseHosted();
            Assert.Empty(FlashOverlay.Hosted);
            Assert.True(stage.IsVisible);
            SubliminalOverlay.CloseAll();
            PinkFilterOverlay.CloseAll();
            Assert.False(stage.IsVisible);
        }
        finally
        {
            FlashOverlay.CloseHosted(); SubliminalOverlay.CloseAll(); PinkFilterOverlay.CloseAll();
            SharedOverlayHost.Configure = configure; SharedOverlayHost.IsTransparent = transparent; FlashOverlay.ScheduleHosted = schedule;
        }
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Show_routes_to_stage_or_classic_on_refusal_and_click(bool clickable, bool refused, bool gaze) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedOverlayHost, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.FlashGlowEnabled, s.FlashMotionStyle);
        var configure = SharedOverlayHost.Configure;
        var transparent = SharedOverlayHost.IsTransparent;
        var present = FlashOverlay.Present;
        try
        {
            s.UnifiedOverlayHost = true; s.FlashGazePopEnabled = gaze; s.FlashGazeLingerEnabled = false;
            s.FlashGlowEnabled = false; s.FlashMotionStyle = FlashMotionStyle.Still;
            SharedOverlayHost.Configure = (w, _) => { w.Width = 300; w.Height = 200; return !refused; };
            SharedOverlayHost.IsTransparent = _ => true;
            var classic = false;
            FlashOverlay.Present = (_, _, click) => { classic = true; Assert.Equal(clickable, click); return true; };
            var screen = new Window().Screens.All[0];
            Assert.True(FlashOverlay.Show(new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96)),
                new PixelRect(10, 20, 40, 50), "test.png", screen, 1, TimeSpan.Zero, TimeSpan.FromSeconds(10), clickable, false, 0));
            Assert.Equal(clickable || refused || gaze, classic);
            Assert.Equal(clickable || refused || gaze ? 0 : 1, FlashOverlay.Hosted.Count);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            (s.UnifiedOverlayHost, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.FlashGlowEnabled, s.FlashMotionStyle) = saved;
            SharedOverlayHost.Configure = configure; SharedOverlayHost.IsTransparent = transparent; FlashOverlay.Present = present;
        }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Hosted_corners_follow_setting_and_entitlement(bool rounded, bool owned) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedOverlayHost, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.FlashGlowEnabled, s.FlashMotionStyle, s.FlashRoundedCorners);
        var skills = s.UnlockedSkills.ToArray();
        var (configure, transparent, granted) = (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent, PrizeOwnership.IsGranted);
        try
        {
            s.UnifiedOverlayHost = true; s.FlashGazePopEnabled = false; s.FlashGazeLingerEnabled = false;
            s.FlashGlowEnabled = false; s.FlashMotionStyle = FlashMotionStyle.Still; s.FlashRoundedCorners = rounded;
            s.UnlockedSkills.Clear();
            PrizeOwnership.IsGranted = _ => owned;
            SharedOverlayHost.Configure = (w, _) => { w.Width = 300; w.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            var screen = new Window().Screens.All[0];
            Assert.True(FlashOverlay.Show(new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96)),
                new PixelRect(10, 20, 40, 50), "test.png", screen, 1, TimeSpan.Zero, TimeSpan.FromSeconds(10), false, false, 0));
            var image = Assert.Single(FlashOverlay.Hosted).Image;
            if (rounded && owned)
            {
                var clip = Assert.IsType<RectangleGeometry>(image.Clip);
                Assert.Equal(40 / screen.Scaling * 0.07, clip.RadiusX, 6);
                Assert.Equal(clip.RadiusX, clip.RadiusY);
            }
            else Assert.Null(image.Clip);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            (s.UnifiedOverlayHost, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.FlashGlowEnabled, s.FlashMotionStyle, s.FlashRoundedCorners) = saved;
            s.UnlockedSkills.Clear(); s.UnlockedSkills.AddRange(skills);
            (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent, PrizeOwnership.IsGranted) = (configure, transparent, granted);
        }
    });

    [Fact]
    public void Stop_cancels_each_pending_fade_and_expiry_without_closing_other_layers() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var bounds = new PixelRect(0, 0, 300, 200);
        var (configure, transparent, schedule) = (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent, FlashOverlay.ScheduleHosted);
        var timers = new List<Scheduled>();
        Window? stage = null;
        try
        {
            SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            FlashOverlay.ScheduleHosted = (action, delay) => { var timer = new Scheduled(action, delay); timers.Add(timer); return timer; };
            Assert.True(PinkFilterOverlay.ShowHosted(bounds, 1, 2, 3, 0.4));
            Assert.True(FlashOverlay.ShowHosted(new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96)),
                new PixelRect(10, 20, 40, 50), bounds, 1, 1, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10)));
            Assert.Contains(timers, t => t.Delay == TimeSpan.FromMinutes(10));
            Assert.True(timers.Count > 1);
            FlashOverlay.CloseAll(final: false);
            Assert.Empty(FlashOverlay.Hosted);
            Assert.All(timers, t => { Assert.True(t.Cancelled); Assert.Null(t.Callback); });
            Assert.True(stage!.IsVisible);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false); PinkFilterOverlay.CloseAll();
            (SharedOverlayHost.Configure, SharedOverlayHost.IsTransparent, FlashOverlay.ScheduleHosted) = (configure, transparent, schedule);
        }
    });

    [Fact]
    public void Refused_stage_leaves_bitmap_for_classic_fallback() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        try
        {
            SharedOverlayHost.Configure = (_, _) => false;
            using var bitmap = new WriteableBitmap(new PixelSize(4, 4), new Vector(96, 96));
            Assert.False(FlashOverlay.ShowHosted(bitmap, new PixelRect(0, 0, 4, 4), new PixelRect(0, 0, 300, 200), 1, 1, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            Assert.Equal(4, bitmap.PixelSize.Width);
            Assert.Empty(FlashOverlay.Hosted);
        }
        finally { SharedOverlayHost.Configure = configure; }
    });
}
