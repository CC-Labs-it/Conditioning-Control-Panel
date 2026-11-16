using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Super Flicker Deck on the Avalonia flash windows. WPF: FlashService.cs:2726-2965,
/// FlashLayer.cs:516-646 and :929-1130; the rules are Core FlickerDeck/FlickerShatter.</summary>
public sealed class FlickerDeckOverlayTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private const int W = 400, H = 300;

    private static Task OnUi(Action<AppSettings, Clock> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.FlashShatterEnabled, s.FlashDraggable, s.MotionLevel, s.GentleVisuals, s.CorruptionMode, s.FlashExitStyle);
        var old = (FlashOverlay.Clock, FlashOverlay.DeckOn, FlashOverlay.DeckPicture, FlashOverlay.Rng, PrizeOwnership.IsGranted,
            UiMotionPreference.OsProbe, UiMotionPreference.CacheStamp);
        var clock = new Clock();
        FlashOverlay.Clock = clock;
        FlashOverlay.DeckOn = () => true;
        FlashOverlay.DeckPicture = _ => Task.FromResult<(Bitmap, string)?>((Solid(0, 0, 255), "next.png"));
        FlashOverlay.Rng = new Random(73);
        PrizeOwnership.IsGranted = _ => false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        (s.FlashShatterEnabled, s.FlashDraggable, s.MotionLevel, s.GentleVisuals, s.CorruptionMode, s.FlashExitStyle) =
            (false, false, MotionLevel.Full, false, false, FlashExitStyle.None);
        try { body(s, clock); }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            (s.FlashShatterEnabled, s.FlashDraggable, s.MotionLevel, s.GentleVisuals, s.CorruptionMode, s.FlashExitStyle) = saved;
            (FlashOverlay.Clock, FlashOverlay.DeckOn, FlashOverlay.DeckPicture, FlashOverlay.Rng, PrizeOwnership.IsGranted,
                UiMotionPreference.OsProbe, UiMotionPreference.CacheStamp) = old;
        }
        return Task.CompletedTask;
    });

    private static WriteableBitmap Solid(byte r, byte g, byte b)
    {
        var bmp = new WriteableBitmap(new PixelSize(W, H), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        var row = new byte[W * 4];
        for (var i = 0; i < row.Length; i += 4) (row[i], row[i + 1], row[i + 2], row[i + 3]) = (b, g, r, 255);
        for (var y = 0; y < H; y++) System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
        return bmp;
    }

    /// <summary>A clickable flash spawned the way FlashOverlay.Spawn does it, then made a card.</summary>
    private static (FlashOverlayWindow Window, int Pad) Card()
    {
        var w = new FlashOverlayWindow(Solid(255, 0, 0), "first.png");
        w.MakeClickable(() => FlashOverlay.Pop(w), () => FlashOverlay.DeckPress(w));
        var rect = new PixelRect(400, 300, W, H);
        var pad = FlashOverlay.AttachDeck(w, rect, 1, clickable: true);
        Assert.True(pad > 0);
        (w.Width, w.Height, w.Position) = (W + 2 * pad, H + 2 * pad, new PixelPoint(400 - pad, 300 - pad));
        FlashOverlay.Active.Add((w, rect));
        w.Closed += (_, _) => FlashOverlay.Active.RemoveAll(e => e.Window == w);   // as FlashOverlay.Spawn does
        w.Show();
        w.UpdateLayout();
        return (w, pad);
    }

    private static TransformGroup Pose(FlashOverlayWindow w) => (TransformGroup)w.PictureTransform!;
    private static ScaleTransform Scale(FlashOverlayWindow w) => (ScaleTransform)Pose(w).Children[0];
    private static RotateTransform Wobble(FlashOverlayWindow w) => (RotateTransform)Pose(w).Children[1];
    private static TranslateTransform Shift(FlashOverlayWindow w) => (TranslateTransform)Pose(w).Children[2];

    private static void Tick(Clock clock, double seconds)
    {
        clock.Now += (long)(seconds * TimeSpan.TicksPerSecond);
        FlashOverlay.DeckTick();
    }

    [Fact]
    public Task A_click_lifts_flips_edge_on_swaps_the_face_and_the_card_wobbles_after() => OnUi((_, clock) =>
    {
        var (w, _) = Card();
        var deck = w.Deck!;
        deck.BreakAt = 4;
        var first = w.Picture;
        w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
        Assert.True(deck.Busy);
        Assert.NotNull(w.DeckPending);
        double minWidth = 1, maxRise = 0;
        var puff = false;
        for (var i = 0; i < 40; i++)
        {
            Tick(clock, 0.02);
            puff |= w.DeckSparkLayer != null;
            minWidth = Math.Min(minWidth, Scale(w).ScaleX);
            maxRise = Math.Min(maxRise, Shift(w).Y);
        }
        Assert.True(minWidth < 0.2, $"never went edge-on ({minWidth})");
        Assert.True(maxRise < -10, $"never lifted ({maxRise})");
        Assert.False(deck.Busy);
        Assert.Equal(1, deck.Flips);
        Assert.NotSame(first, w.Picture);
        Assert.Equal("next.png", w.SourcePath);
        Assert.True(puff, "the swap puffs");
        var angles = Enumerable.Range(0, 10).Select(_ => { Tick(clock, 0.03); return Wobble(w).Angle; }).ToArray();
        Assert.True(angles.Max() - angles.Min() > 0.5, "a flipped card should sway");
        Assert.True(FlashOverlay.DeckTickRunning);
    });

    [Fact]
    public Task The_flip_before_last_cracks_the_face_and_the_breaking_click_shatters_into_forty_triangles() => OnUi((_, clock) =>
    {
        var (w, _) = Card();
        var deck = w.Deck!;
        deck.BreakAt = 3;
        deck.Flips = 2;
        Tick(clock, 0.01);
        Assert.True(w.CrackLine!.IsVisible);
        Assert.InRange(w.CrackLine.Opacity, 0.3, 0.34);   // 0.55 x 0.6: a hairline, not a drawing
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
        Assert.Null(w.Deck);
        Assert.NotNull(w.DeckShatter);
        Assert.Contains(w, FlashOverlay.Exiting);
        Assert.DoesNotContain(FlashOverlay.Active, e => e.Window == w);
        Assert.False(w.IsHitTestVisible);
        var shards = w.DeckShards!.Children.OfType<Border>().ToArray();
        Assert.Equal(FlickerShatter.ShardCount, shards.Length);
        Assert.Equal(FlickerShatter.WhiteSparks + FlickerShatter.PinkSparks, w.DeckShards.Children.Count - shards.Length);
        clock.Now += TimeSpan.TicksPerSecond / 5;
        w.ExitFrame();
        Assert.Contains(shards, b => ((TranslateTransform)((TransformGroup)b.RenderTransform!).Children[1]).Y != 0);
        Assert.False(closed);
        clock.Now += TimeSpan.TicksPerSecond * 2;
        w.ExitFrame();
        Assert.True(closed);
        Assert.Empty(FlashOverlay.Exiting);
    });

    [Fact]
    public Task Press_and_swap_extend_lifetime_without_shortening_it() => OnUi((_, clock) =>
    {
        var (w, _) = Card();
        w.SpawnedAt = clock.Now;
        w.Lifetime = TimeSpan.FromMilliseconds(500);
        w.Run(1, TimeSpan.Zero, w.Lifetime);
        w.Deck!.BreakAt = 4;
        var before = w.ExpiresAt;
        w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
        Assert.True(w.ExpiresAt >= clock.Now + (long)((FlickerDeck.LiftSpanSec + FlickerDeck.MaxHoldSec) * TimeSpan.TicksPerSecond));
        Assert.True(w.ExpiresAt > before);
        // A fresh face gets the original lifetime, even when it lands close to the deadline.
        w.Lifetime = TimeSpan.FromSeconds(10);
        for (var i = 0; i < 40; i++) Tick(clock, 0.02);
        Assert.True(w.ExpiresAt >= clock.Now + TimeSpan.TicksPerSecond * 9);
    });

    [Fact]
    public Task Break_raises_one_stage_blast_and_panic_closes_the_shatter() => OnUi((s, _) =>
    {
        var stage = ConditioningControlPanel.Avalonia.Platform.Super.SuperStageHost.Stage;
        var blasts = new System.Collections.Generic.List<ConditioningControlPanel.SuperCore.Blast>();
        void OnBlast(ConditioningControlPanel.SuperCore.Blast b) => blasts.Add(b);
        stage.Blasted += OnBlast;
        var oldReach = s.DeckBreakReach;
        try
        {
            s.DeckBreakReach = 50;
            var (w, _) = Card();
            w.Deck!.BreakAt = 1;
            w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
            FlashOverlay.DeckPress(w);
            var b = Assert.Single(blasts);
            Assert.Equal(ConditioningControlPanel.SuperCore.BlastKind.CardBreak, b.Kind);
            Assert.Equal(600, b.X);
            Assert.Equal(450, b.Y);
            Assert.Equal(320, b.ReachPx, 6);
            Assert.Equal(320, b.HolePx, 6);
            Assert.NotNull(w.DeckShatter);
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(p => p.Id == "flash").Stop(null);
            Assert.Empty(FlashOverlay.Exiting);
            Assert.False(w.IsVisible);
        }
        finally { s.DeckBreakReach = oldReach; stage.Blasted -= OnBlast; }
    });

    [Fact]
    public Task Panic_from_a_blast_listener_closes_the_breaking_card() => OnUi((_, _) =>
    {
        var stage = ConditioningControlPanel.Avalonia.Platform.Super.SuperStageHost.Stage;
        void Panic(ConditioningControlPanel.SuperCore.Blast _) =>
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(p => p.Id == "flash").Stop(null);
        stage.Blasted += Panic;
        try
        {
            var (w, _) = Card();
            w.Deck!.BreakAt = 1;
            w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
            Assert.False(w.IsVisible);
            Assert.Null(w.DeckShatter);
            Assert.Empty(FlashOverlay.Exiting);
            Assert.Empty(FlashOverlay.Active);
        }
        finally { stage.Blasted -= Panic; }
    });

    [Fact]
    public Task Live_pool_pulls_pictures_only_when_enabled() => OnUi((s, clock) =>
    {
        var stage = ConditioningControlPanel.Avalonia.Platform.Super.SuperStageHost.Stage;
        var saved = (s.WhirlpoolDragsPictures, stage.DragsPictures);
        try
        {
            stage.Pool.Reset(); stage.PoolOn = true; stage.DragsPictures = true;
            stage.Pool.Step(14, 550, 450, 0, 1, 1080);
            var (w, _) = Card();
            s.WhirlpoolDragsPictures = false;
            var start = w.Position;
            Tick(clock, 0.1);
            Assert.Equal(start, w.Position);
            s.WhirlpoolDragsPictures = true;
            for (int i = 0; i < 5; i++) Tick(clock, 0.05);
            Assert.NotEqual(start, w.Position);
            Assert.True(w.PoolScale < 1);
        }
        finally { stage.PoolOn = false; stage.Pool.Reset(); (s.WhirlpoolDragsPictures, stage.DragsPictures) = saved; }
    });

    [Fact]
    public Task Pool_eye_breaks_a_card_without_click_rewards_and_panic_closes_it() => OnUi((s, clock) =>
    {
        var stage = ConditioningControlPanel.Avalonia.Platform.Super.SuperStageHost.Stage;
        var saved = (s.WhirlpoolDragsPictures, stage.DragsPictures, FlashOverlay.Multiply, s.CorruptionMode);
        int blasts = 0, children = 0;
        void OnBlast(ConditioningControlPanel.SuperCore.Blast b) { if (b.Kind == ConditioningControlPanel.SuperCore.BlastKind.CardBreak) blasts++; }
        stage.Blasted += OnBlast;
        try
        {
            s.WhirlpoolDragsPictures = true; stage.DragsPictures = true; s.CorruptionMode = true;
            FlashOverlay.Multiply = (_, _, _, _) => children++;
            stage.Pool.Reset(); stage.PoolOn = true; stage.Pool.Step(14, 600, 450, 0, 1, 1080);
            var (w, _) = Card();
            Tick(clock, 0.05);
            Assert.Equal(1, blasts);
            Assert.Equal(0, children);
            Assert.NotNull(w.DeckShatter);
            Assert.DoesNotContain(FlashOverlay.Active, e => e.Window == w);
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(p => p.Id == "flash").Stop(null);
            Assert.False(w.IsVisible);
        }
        finally
        {
            stage.Blasted -= OnBlast; stage.PoolOn = false; stage.Pool.Reset();
            (s.WhirlpoolDragsPictures, stage.DragsPictures, FlashOverlay.Multiply, s.CorruptionMode) = saved;
        }
    });

    [Fact]
    public Task Motion_off_keeps_the_card_still_and_breaks_it_with_the_plain_cut() => OnUi((s, clock) =>
    {
        s.MotionLevel = MotionLevel.Off;
        var (w, _) = Card();
        var deck = w.Deck!;
        deck.BreakAt = 2;
        w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
        for (var i = 0; i < 20; i++)
        {
            Tick(clock, 0.03);
            Assert.Equal(1, Scale(w).ScaleX);
            Assert.Equal(1, Scale(w).ScaleY);
            Assert.Equal(0, Wobble(w).Angle);
            Assert.Equal(0, Shift(w).Y);
        }
        Assert.Equal("next.png", w.SourcePath);   // the face still changes, in one cut
        Assert.Null(w.DeckSparkLayer);
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
        Assert.True(closed);
        Assert.Null(w.DeckShards);
    });

    [Fact]
    public Task Gentle_visuals_flip_at_reduced_motion_never_full() => OnUi((s, clock) =>
    {
        s.GentleVisuals = true;
        Assert.Equal(MotionLevel.Reduced, FlashOverlayWindow.DeckLevel);
        var (w, _) = Card();
        w.Deck!.BreakAt = 4;
        w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
        var maxScale = 0.0;
        for (var i = 0; i < 40; i++) { Tick(clock, 0.02); maxScale = Math.Max(maxScale, Scale(w).ScaleY); }
        Assert.InRange(maxScale, 1.01, 1.06);   // half of the 10% lift
    });

    [Fact]
    public Task Panic_closes_cards_mid_flip_and_mid_break_within_one_tick() => OnUi((_, clock) =>
    {
        var (flipping, _) = Card();
        flipping.Deck!.BreakAt = 4;
        var (breaking, _) = Card();
        breaking.Deck!.BreakAt = 1;
        flipping.MouseDown(new Point(flipping.Width / 2, flipping.Height / 2), MouseButton.Left);
        Tick(clock, 0.1);
        breaking.MouseDown(new Point(breaking.Width / 2, breaking.Height / 2), MouseButton.Left);
        Assert.NotNull(breaking.DeckShatter);
        Assert.True(flipping.Deck.Busy);
        var closed = 0;
        flipping.Closed += (_, _) => closed++;
        breaking.Closed += (_, _) => closed++;
        ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(p => p.Id == "flash").Stop(null);
        Assert.Equal(2, closed);
        Assert.False(FlashOverlay.DeckTickRunning);
        Assert.Empty(FlashOverlay.Exiting);
        Assert.Empty(FlashOverlay.Active);
    });

    [Fact]
    public Task Switching_deck_off_or_an_owned_shatter_leaves_flashes_plain() => OnUi((s, _) =>
    {
        var w = new FlashOverlayWindow(Solid(255, 0, 0));
        FlashOverlay.DeckOn = () => false;
        Assert.Equal(0, FlashOverlay.AttachDeck(w, new PixelRect(0, 0, W, H), 1, clickable: true));
        FlashOverlay.DeckOn = () => true;
        Assert.Equal(0, FlashOverlay.AttachDeck(w, new PixelRect(0, 0, W, H), 1, clickable: false));
        s.FlashShatterEnabled = true;
        PrizeOwnership.IsGranted = _ => true;
        Assert.Equal(0, FlashOverlay.AttachDeck(w, new PixelRect(0, 0, W, H), 1, clickable: true));
        Assert.Null(w.Deck);
        w.Close();
    });

    [Fact]
    public Task A_render_shows_the_lifted_card_above_its_rest_and_hairline_cracks_on_the_face() => OnUi((_, clock) =>
    {
        var (w, pad) = Card();
        var deck = w.Deck!;
        deck.BreakAt = 4;
        int Red(WriteableBitmap f, int x, int y)
        {
            using var fb = f.Lock();
            var px = new byte[4];
            System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, px, 0, 4);
            return f.Format == PixelFormat.Rgba8888 ? px[0] : px[2];
        }
        int NotRed(WriteableBitmap f)
        {
            using var fb = f.Lock();
            var n = 0;
            var px = new byte[4];
            for (var y = pad + 60; y < pad + H - 60; y++)
                for (var x = pad + 60; x < pad + W - 60; x++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, px, 0, 4);
                    if (px[1] > 30) n++;
                }
            return n;
        }
        var probeY = pad - 8;   // just above the resting picture, inside the grown margin
        var rest = w.CaptureRenderedFrame()!;
        Assert.True(Red(rest, pad + W / 2, probeY) < 40, "nothing above a resting card");
        Assert.Equal(0, NotRed(rest));
        w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);
        Tick(clock, 0.12);   // well into the lift, before the flip narrows the face
        var lifted = w.CaptureRenderedFrame()!;
        Assert.True(Red(lifted, pad + W / 2, probeY) > 200, "the lifted card rises over its resting edge");
        for (var i = 0; i < 40; i++) Tick(clock, 0.02);
        deck.Flips = 3;   // the flip before the break
        deck.SinceSwap = 99;
        Tick(clock, 0.001);
        Assert.True(NotRed(w.CaptureRenderedFrame()!) > 0, "the cracks draw on the face");
    });
}
