using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
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
                    else for (int i = 0; i < 250; i++) ChaosRunHostTests.TickRun();   // past the 60 s run: EndRun
                    Assert.False(ChaosRunHost.IsDescending);
                    if (!panic) Assert.True(ChaosRunHost.State!.ElapsedSec >= 60);
                    Assert.Empty(ChaosPopText.Pool.All);
                    ChaosRunHost.ForceShutdown();
                }
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosPopText.ShutdownPool(); }
            return Task.CompletedTask;
        });
    }
}
