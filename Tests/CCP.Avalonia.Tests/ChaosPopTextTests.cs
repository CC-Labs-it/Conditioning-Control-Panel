using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel;
using Xunit;
using M = ConditioningControlPanel.Avalonia.Views.Chaos.ChaosPopTextMotion;

namespace CCP.Avalonia.Tests;

/// <summary>WPF ChaosPopText (ConditioningControlPanel/Chaos/ChaosPopText.cs): timing, layout,
/// palette, pool cap/reuse, the announcer gate, and teardown by the run host.</summary>
public sealed class ChaosPopTextTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    // WPF BubbleService :4712/:4910-4918: a treat click delivers its variant word
    // at the bubble centre. ChaosPopText :54-55: the announcer switch gates delivery.
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Treat_pop_delivers_the_variant_word_through_the_default_host_hook(bool enabled, bool payouts)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var prepare = ChaosPopText.Prepare;
            var canShow = ChaosPopText.PlatformCanShow;
            bool announcer = CoreSettings.Current.ChaosAnnouncerEnabled;
            bool tasted = ChaosMeta.State.FirstTimesAwarded.Contains(ChaosFirstTimes.Taste);
            ChaosMeta.State.FirstTimesAwarded.Add(ChaosFirstTimes.Taste); // this test isolates existing payouts
            ChaosPopText.Prepare = (w, r) => { w.Position = r.Position; return true; };
            ChaosPopText.PlatformCanShow = () => true;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                CoreSettings.Current.ChaosAnnouncerEnabled = enabled;
                ChaosRunHost.State!.Focus = payouts ? 0 : 100;
                ChaosRunHost.State.ShowPopScores = payouts;
                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                {
                    VariantId = "flash", Tint = 0xFF00C8FF, SizePx = 100,
                    Payload = new FlashPayload()
                });
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                Dispatcher.UIThread.RunJobs();
                Assert.True(b.Popping);
                Assert.True(ChaosRunHost.State!.Score > 0);
                if (enabled)
                {
                    Assert.Equal(payouts ? 3 : 1, ChaosPopText.Pool.All.Count);
                    var w = Assert.Single(ChaosPopText.Pool.All.Where(x => x.LabelText == "FLASH"));
                    Assert.True(w.IsVisible);
                    Assert.Equal("FLASH", w.LabelText);
                    Assert.Equal(Color.FromRgb(71, 215, 255), ((ISolidColorBrush)w.LabelFill).Color);
                    var k = w.Screens.Primary?.Scaling is > 0 and var scale ? scale : 1.0;
                    Assert.Equal(new PixelPoint((int)Math.Round((b.CenterX - 140) * k),
                        (int)Math.Round((b.CenterY - 44) * k)), w.Position);
                }
                else Assert.Empty(ChaosPopText.Pool.All);
                if (enabled && payouts)
                {
                    var focus = Assert.Single(ChaosPopText.Pool.All.Where(x => x.LabelText.EndsWith(" FOCUS")));
                    Assert.Equal($"+{(int)ChaosFieldMath.TreatFocusGain(b.Spec!.PayMult)} FOCUS", focus.LabelText);
                    var score = Assert.Single(ChaosPopText.Pool.All.Where(x => x != focus && x.LabelText != "FLASH"));
                    Assert.Equal("+" + ((long)ChaosRunHost.State!.Score).ToString("N0"), score.LabelText);
                    var k = score.Screens.Primary?.Scaling is > 0 and var scale ? scale : 1.0;
                    Assert.Equal((int)Math.Round((b.CenterY + 30 - 44) * k), score.Position.Y);
                    Assert.Equal((int)Math.Round((b.CenterY + 34 - 44) * k), focus.Position.Y);
                }
                ChaosRunHost.ForceShutdown();
                Assert.Empty(ChaosPopText.Pool.All);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosPopText.ShutdownPool();
                ChaosPopText.Prepare = prepare;
                ChaosPopText.PlatformCanShow = canShow;
                CoreSettings.Current.ChaosAnnouncerEnabled = announcer;
                if (!tasted) ChaosMeta.State.FirstTimesAwarded.Remove(ChaosFirstTimes.Taste);
            }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosPopText.cs:80-93: teardown follows pending displays and leaves no windows.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Teardown_invalidates_a_pop_queued_before_dispatch(bool panic)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var prepare = ChaosPopText.Prepare;
            var canShow = ChaosPopText.PlatformCanShow;
            var hook = ChaosRunHost.PopText;
            bool announcer = CoreSettings.Current.ChaosAnnouncerEnabled;
            bool tasted = ChaosMeta.State.FirstTimesAwarded.Contains(ChaosFirstTimes.Taste);
            ChaosMeta.State.FirstTimesAwarded.Add(ChaosFirstTimes.Taste);
            int prepared = 0;
            ChaosPopText.Prepare = (w, r) => { prepared++; w.Position = r.Position; return true; };
            ChaosPopText.PlatformCanShow = () => true;
            ChaosRunHost.PopText = ChaosPopText.Show;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                CoreSettings.Current.ChaosAnnouncerEnabled = true;
                ChaosRunHost.State!.ShowPopScores = false;
                ChaosRunHost.State.Focus = 100;
                ChaosPopText.Show(400, 300, "existing", Colors.Red);
                Dispatcher.UIThread.RunJobs();
                var existing = Assert.Single(ChaosPopText.Pool.All);
                Assert.True(existing.IsVisible);
                Assert.Equal(1, prepared);

                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                {
                    VariantId = "flash", SizePx = 100, Payload = new FlashPayload()
                });
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                Assert.True(b.Popping);
                // No dispatcher drain between the real pop and teardown.
                if (panic) ChaosRunHost.ForceShutdown(); else ChaosPopText.ShutdownPool();
                Assert.False(existing.IsVisible);
                Assert.Empty(ChaosPopText.Pool.All);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(ChaosPopText.Pool.All);
                Assert.Equal(1, prepared); // the queued word never even prepares a window

                // A later run can still display text after the invalidated generation.
                ChaosPopText.Show(400, 300, "fresh", Colors.Red);
                Dispatcher.UIThread.RunJobs();
                var fresh = Assert.Single(ChaosPopText.Pool.All);
                Assert.True(fresh.IsVisible);
                Assert.Equal("FRESH", fresh.LabelText);
                Assert.Equal(2, prepared);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosPopText.ShutdownPool();
                Dispatcher.UIThread.RunJobs();
                ChaosRunHost.PopText = hook;
                ChaosPopText.Prepare = prepare;
                ChaosPopText.PlatformCanShow = canShow;
                CoreSettings.Current.ChaosAnnouncerEnabled = announcer;
                if (!tasted) ChaosMeta.State.FirstTimesAwarded.Remove(ChaosFirstTimes.Taste);
            }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService.cs:1844/:2002 and :208-225. Repeating the action pays once.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task First_bubble_bonus_banks_once_and_toasts_at_the_hand(bool snap, bool enabled)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var meta = ChaosMeta.State;
            string id = snap ? ChaosFirstTimes.Snap : ChaosFirstTimes.Taste;
            bool awarded = meta.FirstTimesAwarded.Remove(id);
            int sparks = meta.Sparks;
            bool announcer = CoreSettings.Current.ChaosAnnouncerEnabled;
            var prepare = ChaosPopText.Prepare;
            var canShow = ChaosPopText.PlatformCanShow;
            var hook = ChaosRunHost.PopText;
            ChaosRunHost.PopText = ChaosPopText.Show;
            ChaosPopText.Prepare = (w, r) => { w.Position = r.Position; return true; };
            ChaosPopText.PlatformCanShow = () => true;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                CoreSettings.Current.ChaosAnnouncerEnabled = enabled;
                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                {
                    VariantId = snap ? "pink" : "flash", Tint = 0xFF00C8FF, SizePx = 200,
                    IsLive = snap, FuseMs = 5000, Payload = new FlashPayload()
                });
                var t = DateTime.UnixEpoch;
                BubbleOverlay.Field.Clock = () => t = t.AddMilliseconds(32);
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                if (snap)
                    for (int i = 0; i < 40 && !b.Popping; i++) BubbleOverlay.Field.Step();
                Assert.True(b.Popping);
                Dispatcher.UIThread.RunJobs();
                int amount = ChaosFirstTimes.Amounts[id];
                string text = $"+{amount} {ChaosGlyphs.Drops} {ChaosFirstTimes.Labels[id]}";
                Assert.Contains(text, ChaosRunHost.State!.RecentEvents);
                Assert.True(ChaosFirstTimes.IsAwarded(id));
                Assert.Equal(sparks + amount, meta.Sparks);
                int paid = meta.Sparks;
                if (snap) ChaosRunHost.OnDefused(b); else ChaosRunHost.OnBubblePopped(b);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(paid, meta.Sparks);
                var words = ChaosPopText.Pool.All.Where(w => w.LabelText == text.ToUpperInvariant()).ToArray();
                if (enabled)
                {
                    var w = Assert.Single(words);
                    Assert.True(w.IsVisible);
                    var k = w.Screens.Primary?.Scaling is > 0 and var scale ? scale : 1.0;
                    Assert.Equal(new PixelPoint((int)Math.Round((b.CenterX - 140) * k),
                        (int)Math.Round((b.CenterY - 30 - 44) * k)), w.Position);
                    var (r, g, blue) = M.Lift(0xC8, 0xA8, 0xFF);
                    Assert.Equal(Color.FromRgb(r, g, blue), ((ISolidColorBrush)w.LabelFill).Color);
                }
                else Assert.Empty(words);
                ChaosRunHost.ForceShutdown();
                Assert.Empty(ChaosPopText.Pool.All);
                Assert.False(ChaosFirstTimes.TryAward(id));
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(ChaosPopText.Pool.All);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                BubbleOverlay.Field.Clock = () => DateTime.UtcNow;
                ChaosRunHost.PopText = hook;
                ChaosPopText.Prepare = prepare;
                ChaosPopText.PlatformCanShow = canShow;
                CoreSettings.Current.ChaosAnnouncerEnabled = announcer;
                meta.Sparks = sparks;
                if (awarded) meta.FirstTimesAwarded.Add(id); else meta.FirstTimesAwarded.Remove(id);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Fades_in_60ms_holds_to_290_fades_out_by_490_while_rising_6_to_minus_22()
    {
        Assert.Equal(0, M.Opacity(0));
        Assert.Equal(0.29, M.Opacity(30), 6);
        Assert.Equal(0.58, M.Opacity(60), 6);
        Assert.Equal(0.58, M.Opacity(289), 6);
        Assert.Equal(0.29, M.Opacity(390), 6);
        Assert.Equal(0, M.Opacity(490));
        Assert.False(M.Done(489));
        Assert.True(M.Done(490));
        Assert.Equal(6, M.Rise(0), 6);
        Assert.Equal(-8, M.Rise(245), 6);
        Assert.Equal(-22, M.Rise(490), 6);
        Assert.Equal(-22, M.Rise(900), 6);
        Assert.Equal((860.0, 456.0), M.Origin(1000, 500));   // 280x88 centred on the anchor
    }

    [Fact]
    public void Tint_is_lifted_28_percent_toward_white_and_the_gate_needs_text_and_the_announcer()
    {
        Assert.Equal(((byte)71, (byte)215, (byte)255), M.Lift(0, 200, 255));
        Assert.True(M.ShouldShow("SNAP", true));
        Assert.False(M.ShouldShow("SNAP", false));
        Assert.False(M.ShouldShow("  ", true));
        Assert.False(M.ShouldShow(null, true));
    }

    [Fact]
    public void Pool_reuses_retired_items_and_drops_past_the_cap()
    {
        var pool = new ChaosPopPool<object>(M.POOL_MAX);
        Assert.Equal(14, M.POOL_MAX);
        var first = pool.Acquire(() => new object())!;
        for (int i = 1; i < 14; i++) Assert.NotNull(pool.Acquire(() => new object()));
        Assert.Null(pool.Acquire(() => new object()));   // 15th live word: dropped
        pool.Return(first);
        pool.Return(first);   // a double retire never hands one window out twice
        Assert.Same(first, pool.Acquire(() => new object()));
        Assert.Null(pool.Acquire(() => new object()));
        pool.Forget(first);
        Assert.NotNull(pool.Acquire(() => new object()));
        Assert.Equal(14, pool.All.Count);
    }

    [Fact]
    public async Task Show_plays_a_pooled_click_through_word_and_retires_it_back_to_the_pool()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var prepare = ChaosPopText.Prepare;
            var canShow = ChaosPopText.PlatformCanShow;
            bool announcer = CoreSettings.Current.ChaosAnnouncerEnabled;
            var clock = BeatLoop.Time;
            var time = new SteppedTime();
            BeatLoop.Time = time;   // P08: the beat sees only stepped time
            int prepared = 0;
            ChaosPopText.Prepare = (w, r) => { prepared++; w.Position = r.Position; return true; };
            ChaosPopText.PlatformCanShow = () => true;
            try
            {
                ChaosPopText.ShutdownPool();
                CoreSettings.Current.ChaosAnnouncerEnabled = false;
                ChaosPopText.Show(500, 300, "snap", Colors.Red);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(ChaosPopText.Pool.All);   // on-screen Chaos text off: nothing

                CoreSettings.Current.ChaosAnnouncerEnabled = true;
                ChaosPopText.Prepare = (_, _) => false;   // the platform refuses: dropped, slot freed
                ChaosPopText.Show(500, 300, "snap", Colors.Red);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(ChaosPopText.Pool.All);
                ChaosPopText.Prepare = (w, r) => { prepared++; w.Position = r.Position; return true; };

                ChaosPopText.Show(500, 300, "snap", Color.FromRgb(0, 200, 255));
                Dispatcher.UIThread.RunJobs();
                var w = Assert.Single(ChaosPopText.Pool.All);
                Assert.True(w.IsVisible);
                Assert.False(w.IsHitTestVisible);
                Assert.False(w.Focusable);
                Assert.Equal(CoreSettings.Current.ChaosPinOnTop, w.Topmost);
                Assert.Equal("SNAP", w.LabelText);
                Assert.Equal(Color.FromRgb(71, 215, 255), ((ISolidColorBrush)w.LabelFill).Color);
                Assert.Equal(0, w.Alpha);
                Assert.Equal(1, prepared);

                time.Advance(TimeSpan.FromMilliseconds(200));
                BeatLoop.StepAll();
                Assert.Equal(0.58, w.Alpha, 6);
                Assert.True(w.RiseY < 0);
                time.Advance(TimeSpan.FromMilliseconds(290));
                BeatLoop.StepAll();
                Assert.False(w.IsVisible);
                Assert.Equal(1, ChaosPopText.Pool.FreeCount);

                ChaosPopText.Show(800, 300, "gold", Colors.Gold);   // reused, moved, not re-prepared
                Dispatcher.UIThread.RunJobs();
                Assert.Same(w, Assert.Single(ChaosPopText.Pool.All));
                Assert.Equal(6, w.RiseY, 6);   // restarts at the bottom, not where the last word ended
                Assert.True(w.IsVisible);
                Assert.Equal(1, prepared);
                var k = w.Screens.Primary?.Scaling is > 0 and var s ? s : 1.0;
                Assert.Equal((int)Math.Round((800 - 140) * k), w.Position.X);
            }
            finally
            {
                ChaosPopText.ShutdownPool();
                ChaosPopText.Prepare = prepare;
                BeatLoop.Time = clock;
                ChaosPopText.PlatformCanShow = canShow;
                CoreSettings.Current.ChaosAnnouncerEnabled = announcer;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task The_run_host_closes_the_pool_on_end_and_on_panic()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                foreach (var panic in new[] { false, true })
                {
                    ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                    ChaosRunHost.Overlay!.FinishCountdown();
                    var w = new ChaosPopText();
                    Assert.Same(w, ChaosPopText.Pool.Acquire(() => w));
                    if (panic) ChaosRunHost.ForceShutdown();
                    else for (int i = 0; i < 250; i++) ChaosRunHost.Tick();   // past the 60 s run: EndRun
                    Assert.Empty(ChaosPopText.Pool.All);
                    ChaosRunHost.ForceShutdown();
                }
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosPopText.ShutdownPool(); }
            return Task.CompletedTask;
        });
    }
}
