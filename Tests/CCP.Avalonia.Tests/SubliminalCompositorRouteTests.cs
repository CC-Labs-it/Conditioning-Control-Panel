using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalCompositorRouteTests
{
    internal sealed class Env
    {
        internal readonly SettingsService Service = new();
        internal readonly List<Window> Stages = new(), Classic = new();
        internal readonly List<Action<TimeSpan>> Frames = new();
        internal bool StageRefused, ClassicRefused;
        internal int Videos;
        internal readonly Window Owner = new();
    }

    // Every native seam Show touches, swapped for headless stand-ins and restored afterwards.
    internal static void With(Action<Env> body) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var bounds = new PixelRect(0, 0, 300, 200);
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(IntPtr.Zero, "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, true);
        var (configure, raise, provider, query) = (SharedOverlayHost.Configure, SharedOverlayHost.Raise, CoreSettings.ServiceProvider, ScreenList.QueryOverride);
        var (available, videos, classic, activate, stack, frame) = (SubliminalOverlay.IsAvailable, SubliminalOverlay.VideoWindowCount,
            SubliminalOverlay.ConfigureClassic, SubliminalOverlay.ActivateCard, SubliminalOverlay.StackOverVideo, SubliminalOverlayWindow.RequestFrame);
        var env = new Env();
        try
        {
            CoreSettings.ServiceProvider = () => env.Service;
            var s = env.Service.Current;
            (s.SubliminalFont, s.DualMonitorEnabled, s.GlobalTargetMonitor, s.GentleVisuals) = ("Fredoka (bundled)", false, -1, false);
            SharedOverlayHost.Configure = (w, _) => { env.Stages.Add(w); w.Width = 300; w.Height = 200; return !env.StageRefused; };
            SharedOverlayHost.Raise = _ => true;
            ScreenList.QueryOverride = _ => new[] { screen };
            SubliminalOverlay.IsAvailable = () => true;
            SubliminalOverlay.VideoWindowCount = () => env.Videos;
            SubliminalOverlay.ConfigureClassic = (w, _) => { env.Classic.Add(w); w.Width = 300; w.Height = 200; return !env.ClassicRefused; };
            SubliminalOverlay.ActivateCard = _ => { };
            SubliminalOverlay.StackOverVideo = _ => { };
            SubliminalOverlayWindow.RequestFrame = (_, callback) => env.Frames.Add(callback);
            env.Owner.Show();
            Assert.False(CoreSubliminal.IsRunning);
            body(env);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            env.Owner.Close();
            (SharedOverlayHost.Configure, SharedOverlayHost.Raise, CoreSettings.ServiceProvider, ScreenList.QueryOverride) = (configure, raise, provider, query);
            (SubliminalOverlay.IsAvailable, SubliminalOverlay.VideoWindowCount, SubliminalOverlay.ConfigureClassic,
                SubliminalOverlay.ActivateCard, SubliminalOverlay.StackOverVideo, SubliminalOverlayWindow.RequestFrame) = (available, videos, classic, activate, stack, frame);
        }
    });

    public static IEnumerable<object[]> Routes() =>
        from unified in new[] { false, true }
        from solid in new[] { false, true }
        from focus in new[] { false, true }
        from video in new[] { false, true }
        select new object[] { unified, solid, focus, video, (unified || solid) && !focus && !video };

    // WPF SubliminalService.cs:651-659 + SubliminalOverVideo.cs:29: the unified host takes ordinary cards
    // unless focus is requested or a video is live; solid mode keeps its own host (SubliminalService.cs:669).
    [Theory]
    [MemberData(nameof(Routes))]
    public void Show_picks_the_stage_or_a_classic_card(bool unified, bool solid, bool focus, bool video, bool hosted) => With(env =>
    {
        var s = env.Service.Current;
        (s.UnifiedOverlayHost, s.SubliminalSolidMode, s.SubliminalStealsFocus) = (unified, solid, focus);
        env.Videos = video ? 1 : 0;
        SubliminalOverlay.Show(env.Owner, "CARD");
        if (hosted)
        {
            Assert.Empty(env.Classic);
            var stage = Assert.Single(env.Stages);
            Assert.True(stage.IsVisible);
            var card = Assert.IsType<Grid>(Assert.Single(Assert.IsType<Grid>(stage.Content).Children));
            Assert.Equal(CompositorLayers.Subliminal, card.ZIndex);
            Assert.Equal(0, card.Opacity); // waits for the stage's first frame
            Assert.Single(env.Frames);
        }
        else
        {
            Assert.Empty(env.Stages);
            var card = Assert.Single(env.Classic);
            Assert.True(card.IsVisible);
            Assert.Contains(Assert.IsType<Grid>(card.Content).Children.OfType<TextBlock>(), t => t.Text == "CARD");
        }
    });

    // WPF SubliminalService.cs:659-690 and :865-871: a refused stage still shows a classic card, and a card
    // that leaves the stage (video, focus, native refusal) releases the now idle one-shot stage.
    [Theory]
    [InlineData("stage-refused")]
    [InlineData("video")]
    [InlineData("focus")]
    [InlineData("classic-refused")]
    public void Fallback_shows_a_classic_card_and_releases_the_idle_stage(string reason) => With(env =>
    {
        env.Service.Current.UnifiedOverlayHost = true;
        env.StageRefused = reason == "stage-refused";
        if (!env.StageRefused)
        {
            SubliminalOverlay.Show(env.Owner, "FIRST");
            Assert.True(Assert.Single(env.Stages).IsVisible);
            env.Videos = reason is "video" or "classic-refused" ? 1 : 0;
            env.Service.Current.SubliminalStealsFocus = reason == "focus";
            env.ClassicRefused = reason == "classic-refused";
        }
        SubliminalOverlay.Show(env.Owner, "SECOND");
        var stage = Assert.Single(env.Stages);
        Assert.False(stage.IsVisible);
        Assert.Empty(Assert.IsType<Grid>(stage.Content).Children);
        var card = Assert.Single(env.Classic);
        Assert.Equal(reason != "classic-refused", card.IsVisible);
    });

    // Replacement retires the old card; its late first frame and timers cannot bring it back.
    [Fact]
    public void Replacement_invalidates_the_old_cards_callbacks() => With(env =>
    {
        env.Service.Current.UnifiedOverlayHost = true;
        var timers = new List<Action>();
        Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "FIRST", 0.8, TimeSpan.FromMilliseconds(100),
            (action, _) => timers.Add(action)));
        var stage = Assert.Single(env.Stages);
        var canvas = Assert.IsType<Grid>(stage.Content);
        var old = Assert.Single(canvas.Children);
        var oldFrame = Assert.Single(env.Frames);
        SubliminalOverlay.Show(env.Owner, "SECOND");
        Assert.Same(stage, Assert.Single(env.Stages));
        var current = Assert.Single(canvas.Children);
        Assert.NotSame(old, current);
        var count = timers.Count;
        oldFrame(TimeSpan.Zero);
        Assert.Equal(count, timers.Count);
        foreach (var timer in timers.ToArray()) timer();
        Assert.Equal(0, old.Opacity);
        Assert.Same(current, Assert.Single(canvas.Children));
        Assert.True(stage.IsVisible);
    });

    // CompositorLayers (WPF draws them on one stage): Flash 30 < Subliminal 40 < Spiral 60 < PinkTint 70, or
    // the tint at PinkTintUnder 35. Each family is its own window here, so the stage restacks them in that order.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hosted_card_keeps_the_compositor_order_with_tint_spiral_and_flashes(bool tintUnder) => With(env =>
    {
        var s = env.Service.Current;
        (s.UnifiedOverlayHost, s.PinkFilterEnabled, s.PinkFilterUnderEffects) = (true, true, tintUnder);
        var (effects, raiseWindow, skip, present, stageRaise) = (PinkFilterOverlay.EffectWindows, PinkFilterOverlay.RaiseWindow,
            PinkFilterOverlay.SkipPlatformChecksForTests, FlashOverlay.Present, SharedOverlayHost.Raise);
        var spiral = new SpiralOverlayWindow();
        var raised = new List<Window>();
        try
        {
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            PinkFilterOverlay.Refresh(env.Owner);
            var tint = Assert.Single(PinkFilterOverlay.Shown);
            PinkFilterOverlay.EffectWindows = () => new Window[] { tint, spiral };
            PinkFilterOverlay.RaiseWindow = w => { raised.Add(w); return true; };
            SharedOverlayHost.Raise = w => { raised.Add(w); return true; };
            FlashOverlay.Present = (_, _, _) => true;
            var expected = (Window stage) => tintUnder ? new Window[] { tint, stage, spiral } : new Window[] { stage, spiral, tint };

            SubliminalOverlay.Show(env.Owner, "CARD");
            var stage = Assert.Single(env.Stages);
            Assert.Equal(expected(stage), raised);

            raised.Clear(); // a flash maps on top; the card and everything over it go back above
            Assert.True(FlashOverlay.Show(new global::Avalonia.Media.Imaging.Bitmap(global::Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://CCP.Avalonia/Resources/features/flash.png"))),
                new PixelRect(10, 10, 50, 50), "flash.png", env.Owner.Screens.All[0], 1, TimeSpan.Zero,
                TimeSpan.FromSeconds(6), false, false, 0));
            Assert.Equal(expected(stage), raised);

            env.Videos = 1; // a video starts between the card and a flash: the stage, spiral and tint stay under it (WPF #497)
            raised.Clear();
            Assert.True(FlashOverlay.Show(new global::Avalonia.Media.Imaging.Bitmap(global::Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://CCP.Avalonia/Resources/features/flash.png"))),
                new PixelRect(10, 10, 50, 50), "flash-video.png", env.Owner.Screens.All[0], 1, TimeSpan.Zero,
                TimeSpan.FromSeconds(6), false, false, 0));
            Assert.Empty(raised);
            env.Videos = 0;

            SubliminalOverlay.CloseAll(); // no hosted card: a flash restacks nothing
            raised.Clear();
            Assert.True(FlashOverlay.Show(new global::Avalonia.Media.Imaging.Bitmap(global::Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://CCP.Avalonia/Resources/features/flash.png"))),
                new PixelRect(10, 10, 50, 50), "flash2.png", env.Owner.Screens.All[0], 1, TimeSpan.Zero,
                TimeSpan.FromSeconds(6), false, false, 0));
            Assert.Empty(raised);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            PinkFilterOverlay.CloseAll();
            spiral.Close();
            (PinkFilterOverlay.EffectWindows, PinkFilterOverlay.RaiseWindow, PinkFilterOverlay.SkipPlatformChecksForTests,
                FlashOverlay.Present, SharedOverlayHost.Raise) = (effects, raiseWindow, skip, present, stageRaise);
        }
    });
}
