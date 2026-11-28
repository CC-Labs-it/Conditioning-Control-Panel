using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF FlashService.cs:1697 (compositor first), :2485 SpawnLayerVisual, FlashLayer.cs:395-470 (backing, corners).
[Collection(RunsAloneCollection.Name)]
public sealed class FlashSharedStageTests
{
    private sealed class Timer : IDisposable
    {
        public TimeSpan At; public Action Fire = () => { }; public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    // FlashOverlay.Clock follows the fake timers, so the ramp's dt is the simulated frame time.
    private sealed class StageClock : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _now.Ticks;
    }

    private static Bitmap Picture() => new(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));

    /// <summary>Passive settings, a fake stage, recorded timers; restores everything after.</summary>
    private static void WithStage(Action<List<Timer>, Func<Window?>> body, bool stageAvailable = true)
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedOverlayHost, s.FlashSolidMode, s.FlashGlowEnabled, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled,
            s.FlashMotionStyle, s.FlashRoundedCorners, s.FlashClickable, s.FlashOpacity, s.FadeDuration);
        var (configure, timer, present, granted, clock) = (SharedOverlayHost.Configure, FlashOverlay.HostedTimer, FlashOverlay.Present, PrizeOwnership.IsGranted, FlashOverlay.Clock);
        Window? stage = null;
        var timers = new List<Timer>();
        _now = TimeSpan.Zero;
        try
        {
            (s.UnifiedOverlayHost, s.FlashSolidMode, s.FlashGlowEnabled, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled,
                s.FlashMotionStyle, s.FlashRoundedCorners, s.FlashClickable, s.FlashOpacity, s.FadeDuration) = (true, false, false, false, false, FlashMotionStyle.Still, false, false, 60, 100);
            FlashOverlay.Clock = new StageClock();
            SharedOverlayHost.Configure = (w, b) => { stage = w; w.Width = b.Width; w.Height = b.Height; return stageAvailable; };
            FlashOverlay.HostedTimer = (fire, at) => { var t = new Timer { At = _now + at, Fire = fire }; timers.Add(t); return t; };
            FlashOverlay.Present = (_, _, _) => true;
            body(timers, () => stage);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            SubliminalOverlay.CloseAll();
            (s.UnifiedOverlayHost, s.FlashSolidMode, s.FlashGlowEnabled, s.FlashGazePopEnabled, s.FlashGazeLingerEnabled,
                s.FlashMotionStyle, s.FlashRoundedCorners, s.FlashClickable, s.FlashOpacity, s.FadeDuration) = saved;
            (SharedOverlayHost.Configure, FlashOverlay.HostedTimer, FlashOverlay.Present, PrizeOwnership.IsGranted, FlashOverlay.Clock) = (configure, timer, present, granted, clock);
        }
    }

    private static TimeSpan _now;

    // Fires due timers in order, including the ones they schedule (the fade-out after expiry).
    private static void Advance(List<Timer> timers, TimeSpan to)
    {
        while (timers.Where(t => t.At <= to && !t.Disposed).OrderBy(t => t.At).FirstOrDefault() is { } t)
        {
            timers.Remove(t);
            _now = t.At;
            t.Fire();
        }
        _now = to;
    }

    // Alpha and fade match the stage settings (Opacity 60, Fade 100 = 1 s).
    private static bool ShowOne(bool clickable = false, double lifetimeSeconds = 5) =>
        FlashOverlay.Show(Picture(), new PixelRect(100, 100, 400, 300), "sample.png", new Window().Screens.All[0],
            0.6, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(lifetimeSeconds), clickable, ambient: false, hydraGeneration: 0);

    [Fact]
    public void A_flash_expiring_mid_fade_in_turns_around_and_never_brightens_again() => AvaloniaTestDispatcher.Run(() =>
        WithStage((timers, _) =>
        {
            // WPF Heartbeat_Tick: past ExpiresAt the target is 0, so the ramp reverses from the current alpha.
            Assert.True(ShowOne(lifetimeSeconds: 0.3));
            var item = Assert.Single(FlashOverlay.Hosted).Item;
            Advance(timers, TimeSpan.FromSeconds(0.3));
            var peak = item.Opacity;
            Assert.InRange(peak, 0.2, 0.35);
            for (var t = 0.31; t < 1.5 && FlashOverlay.Hosted.Count > 0; t += 0.01)
            {
                var before = item.Opacity;
                Advance(timers, TimeSpan.FromSeconds(t));
                Assert.True(item.Opacity <= before, $"brightened from {before} to {item.Opacity} at {t:0.00}s");
            }
            Assert.Empty(FlashOverlay.Hosted);
        }));

    [Fact]
    public void Opacity_and_fade_sliders_reach_a_flash_already_on_screen() => AvaloniaTestDispatcher.Run(() =>
        WithStage((timers, _) =>
        {
            // WPF FlashService.cs:3199-3202 reads FlashOpacity and FadeDuration on every heartbeat.
            var s = CoreSettings.Current;
            Assert.True(ShowOne());
            var item = Assert.Single(FlashOverlay.Hosted).Item;
            Advance(timers, TimeSpan.FromSeconds(1));
            Assert.Equal(0.6, item.Opacity, 3);
            s.FlashOpacity = 90;
            Advance(timers, TimeSpan.FromSeconds(2));
            Assert.Equal(0.9, item.Opacity, 3);
            s.FadeDuration = 200; // 2 s: the fade-out from 0.9 now takes 1.8 s
            Advance(timers, TimeSpan.FromSeconds(6));
            Assert.InRange(item.Opacity, 0.35, 0.5); // 0.9 - 1 s / 2 s, give or take a frame
            Assert.Single(FlashOverlay.Hosted);
            Advance(timers, TimeSpan.FromSeconds(9));
            Assert.Empty(FlashOverlay.Hosted);
        }));

    [Fact]
    public void Passive_flash_goes_to_the_stage_under_subliminal_cards_fades_in_and_expires_alone() => AvaloniaTestDispatcher.Run(() =>
        WithStage((timers, stage) =>
        {
            var classic = FlashOverlay.Active.Count;
            Assert.True(ShowOne());
            Assert.Equal(classic, FlashOverlay.Active.Count);
            var item = Assert.IsType<Border>(Assert.Single(FlashOverlay.Hosted).Item);
            var canvas = Assert.IsType<Grid>(stage()!.Content);
            Assert.Same(item, Assert.Single(canvas.Children));
            Assert.Equal(CompositorLayers.Flash, item.ZIndex);
            Assert.Equal(0, item.Opacity);
            // The black backing is the item's own background, so it fades with the picture.
            Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(item.Background).Color);
            Advance(timers, TimeSpan.FromSeconds(1));
            Assert.Equal(0.6, item.Opacity, 3);

            // A subliminal card shares the stage and stays above the flash.
            Assert.True(SubliminalOverlay.ShowHosted(new Window().Screens.All[0].Bounds, "TEST", 0.8, TimeSpan.FromSeconds(30), (_, _) => { }));
            Assert.Equal(2, canvas.Children.Count);
            Assert.True(canvas.Children.Single(c => c != item).ZIndex > item.ZIndex);

            Advance(timers, TimeSpan.FromSeconds(4.9));
            Assert.Equal(0.6, item.Opacity, 3);
            Advance(timers, TimeSpan.FromSeconds(5.3));
            Assert.InRange(item.Opacity, 0.01, 0.59);
            Advance(timers, TimeSpan.FromSeconds(10));
            Assert.Empty(FlashOverlay.Hosted);
            Assert.DoesNotContain(item, canvas.Children);
            Assert.True(stage()!.IsVisible); // the subliminal lease still holds the stage
        }));

    [Fact]
    public void Stop_mid_fade_removes_the_flash_cancels_its_timers_and_closes_the_stage() => AvaloniaTestDispatcher.Run(() =>
        WithStage((timers, stage) =>
        {
            Assert.True(ShowOne());
            var item = Assert.Single(FlashOverlay.Hosted).Item;
            Advance(timers, TimeSpan.FromSeconds(0.5));
            Assert.InRange(item.Opacity, 0.01, 0.59);
            var pending = timers.ToArray();
            Assert.NotEmpty(pending);
            FlashOverlay.CloseAll(final: false);
            Assert.Empty(FlashOverlay.Hosted);
            Assert.All(pending, t => Assert.True(t.Disposed));
            Assert.Empty(Assert.IsType<Grid>(stage()!.Content).Children);
            Assert.False(stage()!.IsVisible);
        }));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Corners_follow_the_fitted_picture_and_the_grant(bool rounded, bool owned) => AvaloniaTestDispatcher.Run(() =>
        WithStage((_, _) =>
        {
            CoreSettings.Current.FlashRoundedCorners = rounded;
            PrizeOwnership.IsGranted = _ => owned;
            Assert.True(ShowOne());
            var item = Assert.IsType<Border>(Assert.Single(FlashOverlay.Hosted).Item);
            var image = Assert.IsType<Image>(item.Child);
            var fit = Stretch.Uniform.CalculateSize(new Size(item.Width, item.Height), ((Bitmap)image.Source!).Size);
            var r = FlashCorners.Resolve(rounded, owned, false, Math.Min(fit.Width, fit.Height), 1);
            Assert.Equal(r, item.CornerRadius.TopLeft);
            Assert.Equal(rounded && owned, r > 0);
            if (r > 0) Assert.Equal(r, ((RectangleGeometry)image.Clip!).RadiusX);
            else Assert.Null(image.Clip);
        }));

    [Fact]
    public void Clickable_gazed_moving_or_glowing_flashes_stay_classic()
    {
        var s = new AppSettings { UnifiedOverlayHost = true, FlashMotionStyle = FlashMotionStyle.Still };
        bool Host(bool clickable = false, bool glow = false, FlashMotionStyle? inherit = null, bool gaze = false, string path = "a.png") =>
            FlashOverlay.CanHost(s, clickable, glow, inherit, path, gaze);
        Assert.True(Host());
        Assert.False(Host(clickable: true));
        Assert.False(Host(glow: true));
        Assert.False(Host(inherit: FlashMotionStyle.Pendulum));
        Assert.True(s.FlashGazePopEnabled && s.FlashGazeLingerEnabled); // on by default, so only a running tracker counts
        Assert.False(Host(gaze: true));
        s.FlashGazePopEnabled = false;
        Assert.False(Host(gaze: true));
        s.FlashGazeLingerEnabled = false;
        Assert.True(Host(gaze: true));
        s.FlashMotionStyle = FlashMotionStyle.DriftBounce;
        Assert.False(Host());
        s.FlashMotionStyle = FlashMotionStyle.Still; s.UnifiedOverlayHost = false;
        Assert.False(Host());
        s.FlashSolidMode = true; // WPF :1698: solid mode hosts without the compositor
        Assert.True(Host());
    }

    [Fact]
    public void A_clickable_flash_uses_the_classic_window() => AvaloniaTestDispatcher.Run(() =>
        WithStage((_, stage) =>
        {
            CoreSettings.Current.FlashClickable = true;
            var classic = FlashOverlay.Active.Count;
            Assert.True(ShowOne(clickable: true));
            Assert.Empty(FlashOverlay.Hosted);
            Assert.Equal(classic + 1, FlashOverlay.Active.Count);
            Assert.Null(stage());
        }));

    [Fact]
    public void A_refused_stage_falls_back_to_the_classic_window() => AvaloniaTestDispatcher.Run(() =>
        WithStage((_, stage) =>
        {
            var classic = FlashOverlay.Active.Count;
            Assert.True(ShowOne());
            Assert.NotNull(stage()); // the stage was asked for and refused
            Assert.Empty(FlashOverlay.Hosted);
            Assert.Equal(classic + 1, FlashOverlay.Active.Count);
        }, stageAvailable: false));
}
