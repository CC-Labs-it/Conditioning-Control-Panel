using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane L2 of the Linux Super port: the Deck's gate, its prefetch (WPF dfc1565889) and the shared
/// stage (WPF FlashService.cs:2757-2840, :5317-5335, FlashLayer.StepPool :1019).</summary>
public sealed partial class FlickerDeckOverlayTests
{
    private static void Press(FlashOverlayWindow w) => w.MouseDown(new Point(w.Width / 2, w.Height / 2), MouseButton.Left);

    [Fact]
    public Task A_free_account_never_gets_a_card_without_the_debug_override() => OnUi((_, _) =>
    {
        var (provider, premium, trying) = (CoreSettings.ServiceProvider, CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider);
        var all = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            var service = new SettingsService();
            service.Current.FlashEnabled = true;
            service.Current.SuperModes = SuperModeRule.With(service.Current.SuperModes, SuperEffect.FlickerDeck, SuperMode.Both);
            CoreSettings.ServiceProvider = () => service;
            SuperAccess.TryingProvider = () => null;
            FlashOverlay.DeckOn = () => SuperAccess.IsOn(SuperEffect.FlickerDeck);   // the shipped switch
            CoreEntitlement.HasPremiumProvider = () => false;
            var w = new FlashOverlayWindow(Solid(255, 0, 0));
            Assert.Equal(0, FlashOverlay.AttachDeck(w, new PixelRect(0, 0, W, H), 1, clickable: true));
            Assert.Null(w.Deck);
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.NotEqual(0, FlashOverlay.AttachDeck(w, new PixelRect(0, 0, W, H), 1, clickable: true));
            w.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", all);
            (CoreSettings.ServiceProvider, CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider) = (provider, premium, trying);
        }
    });

    [Fact]
    public Task A_resting_card_has_its_next_picture_ready_so_the_click_never_waits_and_the_one_after_follows() => OnUi((_, clock) =>
    {
        var asked = new List<bool>();
        FlashOverlay.DeckPicture = (_, local) => { asked.Add(local); return Task.FromResult<(Bitmap, string)?>((Solid(0, 0, 255), "next.png")); };
        var (w, _) = Card();
        w.Deck!.BreakAt = 4;
        Assert.Equal(new[] { true }, asked);   // at spawn: local pictures only, no Scrolller share spent
        Assert.NotNull(w.DeckReady);
        Assert.Equal("next.png", w.DeckReadyKey);
        Press(w);
        Assert.NotNull(w.DeckPending);         // the waiting picture, no fetch on the click
        Assert.False(w.HasDeckReady);
        Assert.Single(asked);
        for (var i = 0; i < 40; i++) Tick(clock, 0.02);
        Assert.Equal("next.png", w.SourcePath);
        Assert.Equal(new[] { true, false }, asked);   // after the flip, the one after it, remote allowed
        Assert.True(w.HasDeckReady);
    });

    [Fact]
    public Task A_miss_holds_edge_on_at_most_a_moment_and_a_late_picture_is_kept_for_the_next_click() => OnUi((_, clock) =>
    {
        var pending = new Queue<TaskCompletionSource<(Bitmap, string)?>>();
        FlashOverlay.DeckPicture = (_, _) => { var t = new TaskCompletionSource<(Bitmap, string)?>(); pending.Enqueue(t); return t.Task; };
        var (w, _) = Card();
        w.Deck!.BreakAt = 4;
        pending.Dequeue().SetResult(null);   // nothing local to get ready
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.HasDeckReady);
        Press(w);
        var late = pending.Dequeue();
        double edgeOn = 0;
        for (var i = 0; i < 60; i++)
        {
            Tick(clock, 0.02);
            if (Scale(w).ScaleX < 0.2) edgeOn += 0.02;
        }
        Assert.False(w.Deck.Busy);
        Assert.InRange(edgeOn, 0.02, 0.5);   // MaxHoldSec 0.4, plus the turn's own edge
        var picture = Solid(0, 255, 0);
        late.SetResult((picture, "late.png"));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(picture, w.DeckReady);
        Assert.Equal("late.png", w.DeckReadyPath);
    });

    [Fact]
    public Task Two_cards_never_get_the_same_picture_ready() => OnUi((_, _) =>
    {
        var dir = Directory.CreateTempSubdirectory("deck-ready-").FullName;
        try
        {
            var images = Directory.CreateDirectory(Path.Combine(dir, "images")).FullName;
            foreach (var name in new[] { "a.png", "b.png" })
                using (var f = File.Create(Path.Combine(images, name))) Solid(10, 20, 30).Save(f);
            CorePaths.EffectiveAssetsProvider = () => dir;
            FlashOverlay.DeckPicture = (w, local) => Task.FromResult(FlashOverlay.LoadDeckPicture(w, local).Result);
            var (one, _) = Card();
            var (two, _) = Card();
            Assert.NotNull(one.DeckReadyKey);
            Assert.NotNull(two.DeckReadyKey);
            Assert.NotEqual(one.DeckReadyKey, two.DeckReadyKey);
            one.Close();
            two.Close();
        }
        finally { Directory.Delete(dir, true); }
    });

    [Fact]
    public Task A_break_blasts_the_stage_and_its_ring_knocks_the_card_beside_it_unless_reach_is_zero() => OnUi((s, _) =>
    {
        var (after, reach) = (FlashOverlay.After, s.DeckBreakReach);
        var blasts = new List<Blast>();
        void Seen(Blast b) => blasts.Add(b);
        SuperStageHost.Stage.Blasted += Seen;
        try
        {
            FlashOverlay.After = (then, _) => then();
            var (breaking, _) = Card();
            breaking.Deck!.BreakAt = 1;
            var (beside, _) = Card();
            beside.Deck!.BreakAt = 4;
            Press(breaking);
            Assert.NotNull(breaking.DeckShatter);
            var b = Assert.Single(blasts);
            Assert.Equal(BlastKind.CardBreak, b.Kind);
            Assert.Equal((600.0, 450.0), (b.X, b.Y));
            Assert.Equal(DeckOptions.StockReachSides * W, b.ReachPx, 6);
            Assert.Equal(0.8 * W, b.HolePx, 6);
            Assert.True(beside.Deck.Busy, "the ring knocks the card in reach");

            blasts.Clear();
            s.DeckBreakReach = 0;
            var (quiet, _) = Card();
            quiet.Deck!.BreakAt = 1;
            var (still, _) = Card();
            Press(quiet);
            Assert.Equal(0, Assert.Single(blasts).ReachPx);   // the hole stays, the ring is gone
            Assert.False(still.Deck!.Busy);
        }
        finally
        {
            SuperStageHost.Stage.Blasted -= Seen;
            (FlashOverlay.After, s.DeckBreakReach) = (after, reach);
        }
    });

    [Fact]
    public Task The_bouncing_word_knocks_a_card_it_crosses_at_most_once_in_one_and_a_half_seconds() => OnUi((_, clock) =>
    {
        var stage = SuperStageHost.Stage;
        var pops = stage.MovingPops;
        try
        {
            stage.MovingPops = true;
            var (w, _) = Card();
            w.Deck!.BreakAt = 9;
            stage.Sweep(560, 420, 640, 480);
            Assert.True(w.Deck.Busy);
            for (var i = 0; i < 50; i++) Tick(clock, 0.02);   // the flip is done, 1 s on
            stage.Sweep(560, 420, 640, 480);
            Assert.False(w.Deck.Busy);
            Tick(clock, 0.7);
            stage.Sweep(560, 420, 640, 480);
            Assert.True(w.Deck.Busy);
            stage.MovingPops = false;   // the player's "Moving effects pop" switch off: nothing sweeps
            for (var i = 0; i < 100; i++) Tick(clock, 0.02);
            stage.Sweep(560, 420, 640, 480);
            Assert.False(w.Deck.Busy);
        }
        finally { stage.MovingPops = pops; }
    });

    [Fact]
    public Task The_whirlpool_drags_a_card_in_draws_it_smaller_and_its_eye_breaks_it_for_nothing() => OnUi((_, clock) =>
    {
        var stage = SuperStageHost.Stage;
        var (pops, poolOn) = (stage.MovingPops, FlashOverlay.PoolOn);
        var clicks = 0;
        void Count() => clicks++;
        FlashOverlay.Clicked += Count;
        try
        {
            stage.MovingPops = true;
            stage.PoolOn = true;
            FlashOverlay.PoolOn = () => true;
            stage.Pool.Step(20, 750, 450, 0, 1, 1080);   // grown to full, 150 px right of the card's centre
            var (w, _) = Card();
            var x0 = FlashOverlay.Active.Single(e => e.Window == w).Rect.X;
            double smallest = 1;
            for (var i = 0; i < 600 && w.DeckShatter == null && !w.IsClosed; i++)
            {
                clock.Now += TimeSpan.TicksPerSecond / 30;
                // After a second the cursor (the eye follows it) rests on the card: a still flash only orbits the eye.
                var at = FlashOverlay.Active.FirstOrDefault(e => e.Window == w).Rect;
                var (cx, cy) = i < 30 ? (750.0, 450.0) : (at.X + at.Width / 2.0, at.Y + at.Height / 2.0);
                lock (SteamOverlay.Gate) stage.Pool.Step(1 / 30.0, cx, cy, 0, 1, 1080);
                FlashOverlay.PoolTick();
                smallest = Math.Min(smallest, w.PoolScale);
                if (i == 3) Assert.True(FlashOverlay.Active.Single(e => e.Window == w).Rect.X > x0, "dragged toward the eye");
            }
            Assert.True(smallest < 0.5, $"drawn smaller near the eye ({smallest})");
            Assert.True(w.DeckShatter != null, $"closed {w.IsClosed} eaten {w.PoolEaten} active {FlashOverlay.Active.Any(e => e.Window == w)} rect {FlashOverlay.Active.FirstOrDefault(e => e.Window == w).Rect} pool {stage.Pool.X},{stage.Pool.Y} live {stage.PoolLive}");
            Assert.Equal(0, clicks);          // and pays nothing
            Assert.DoesNotContain(FlashOverlay.Active, e => e.Window == w);
            FlashOverlay.CloseAll(final: false);
            Assert.False(FlashOverlay.PoolTickRunning);
        }
        finally
        {
            FlashOverlay.Clicked -= Count;
            stage.Reset();
            stage.PoolOn = false;
            (stage.MovingPops, FlashOverlay.PoolOn) = (pops, poolOn);
        }
    });

    [Fact]
    public Task Panic_drops_the_waiting_picture_with_the_card() => OnUi((_, _) =>
    {
        var picture = Solid(0, 0, 255);
        FlashOverlay.DeckPicture = (_, _) => Task.FromResult<(Bitmap, string)?>((picture, "next.png"));
        var (w, _) = Card();
        Assert.Same(picture, w.DeckReady);
        ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(p => p.Id == "flash").Stop(null);
        Assert.True(w.IsClosed);
        Assert.Null(w.DeckReady);
        Assert.Empty(FlashOverlay.Active);
    });

    [Fact]
    public Task The_render_sample_is_a_lifted_card_with_cracks() => OnUi((_, _) =>
    {
        var w = FlashOverlayWindow.DeckSample();
        Assert.NotNull(w.Deck);
        Assert.True(FlickerDeck.Sample(w.Deck!, MotionLevel.Full).Lift > 0.5);
        Assert.True(FlickerDeck.CrackAmount(w.Deck!.Flips, w.Deck.BreakAt) > 0);
        w.Close();
    });
}
