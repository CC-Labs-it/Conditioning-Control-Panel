using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1421: a Deeper file played as a mandatory video dropped subliminals and on-screen
/// instructions the Deeper player showed. The player reports its position only a few times a
/// second, and the engine asked whether the TICK still sat inside a short effect's span, so an
/// effect the playhead crossed between two reports never fired. The Deeper player polls every
/// 100 ms and so rarely lost one.
/// </summary>
public class DeeperMandatoryVideoTimingTests
{
    // ---------------------------------------------------------------- the engine gate

    private static TimelineItem Sub(double start, double duration, string text) => new()
    {
        Kind = TimelineItemKind.Effect,
        Start = start,
        Duration = duration,
        EffectType = EffectTypes.Subliminal,
        EffectText = text,
    };

    private static List<string> Play(IEnumerable<TimelineItem> items, params double[] ticks)
    {
        var enh = new Enhancement();
        enh.TimelineItems.AddRange(items);
        var source = new FakeSource();
        var dispatcher = new FakeDispatcher();
        using var engine = new EnhancementEngine(enh, source, dispatcher);
        engine.Start();
        foreach (var t in ticks) source.Tick(t);
        return dispatcher.Texts;
    }

    [Fact]
    public void AShortSubliminal_CrossedBetweenTwoSparseTicks_StillShows()
    {
        // 0.2 s long at 1.0 s. The player reports 0.9 s, then 1.4 s: the tick lands after the span.
        var shown = Play(new[] { Sub(1.0, 0.2, "obey") }, 0.9, 1.4);
        Assert.Equal(new[] { "obey" }, shown);
    }

    [Fact]
    public void SubliminalsLessThanASecondApart_AllShow_InOrder()
    {
        var items = new[] { Sub(1.0, 0.2, "one"), Sub(1.3, 0.2, "two"), Sub(1.6, 0.2, "three"), Sub(2.4, 0.3, "four") };
        // A sparse clock, about what LibVLC gives a mandatory video.
        var shown = Play(items, 0.5, 0.9, 1.25, 1.55, 1.95, 2.3, 2.75, 3.1);
        Assert.Equal(new[] { "one", "two", "three", "four" }, shown);
    }

    [Fact]
    public void EachSubliminal_ShowsOnce_HoweverDenseTheClock()
    {
        var ticks = Enumerable.Range(0, 40).Select(i => i * 0.05).ToArray();
        var shown = Play(new[] { Sub(1.0, 0.5, "once") }, ticks);
        Assert.Equal(new[] { "once" }, shown);
    }

    [Fact]
    public void AJumpAhead_StillSkipsWhatItJumpedOver()
    {
        // A scrub far past the tick cadence must not fire the skipped span in one burst.
        var shown = Play(new[] { Sub(1.0, 0.2, "skipped"), Sub(9.0, 0.2, "after") }, 0.5, 8.0, 9.1);
        Assert.Equal(new[] { "after" }, shown);
    }

    // ---------------------------------------------------------------- the playhead

    [Fact]
    public void BetweenTwoReports_ThePlayheadRunsOnTheClock()
    {
        var p = new PlayheadExtrapolator();
        Assert.Equal(1000, p.OnReport(1000, nowMs: 50_000));
        Assert.Equal(1100, p.OnTimer(50_100, playing: true));
        Assert.Equal(1200, p.OnTimer(50_200, playing: true));
        // The next report agrees with where the playhead already is: nothing new to say.
        Assert.Equal(-1, p.OnReport(1200, 50_200));
        Assert.Equal(1300, p.OnTimer(50_300, playing: true));
    }

    [Fact]
    public void NoReportYet_OrPaused_NothingIsMadeUp()
    {
        var p = new PlayheadExtrapolator();
        Assert.Equal(-1, p.OnTimer(1_000, playing: true));
        p.OnReport(2000, 1_000);
        Assert.Equal(-1, p.OnTimer(1_100, playing: false));
        Assert.Equal(-1, p.OnTimer(9_000, playing: false));
        // The resume starts from where it paused, not ahead by the length of the pause.
        Assert.Equal(2100, p.OnTimer(9_100, playing: true));
    }

    [Fact]
    public void AStalledPlayer_StopsThePlayhead()
    {
        var p = new PlayheadExtrapolator();
        p.OnReport(1000, 0);
        long last = -1;
        for (int now = 100; now <= 5000; now += 100)
        {
            var e = p.OnTimer(now, playing: true);
            if (e >= 0) last = e;
        }
        Assert.Equal(1000 + PlayheadExtrapolator.MaxLeadMs, last);
    }

    [Fact]
    public void AReportSlightlyBehind_IsJitter_AndNeverRunsThePlayheadBackwards()
    {
        var p = new PlayheadExtrapolator();
        p.OnReport(1000, 0);
        Assert.Equal(1300, p.OnTimer(300, playing: true));
        // The player says 1250 at the moment we had 1300: hold, then carry on from its number.
        Assert.Equal(-1, p.OnReport(1250, 300));
        Assert.Equal(-1, p.OnTimer(340, playing: true));
        Assert.Equal(1350, p.OnTimer(400, playing: true));
    }

    [Fact]
    public void ARealSeekBack_OrALoop_IsPassedOn()
    {
        var p = new PlayheadExtrapolator();
        p.OnReport(60_000, 0);
        p.OnTimer(100, playing: true);
        Assert.Equal(0, p.OnReport(0, 200));
        Assert.Equal(100, p.OnTimer(300, playing: true));
    }

    [Fact]
    public void ThePlayhead_NeverRunsPastTheEndOfTheMedia()
    {
        var p = new PlayheadExtrapolator();
        p.OnReport(9_900, 0);
        Assert.Equal(10_000, p.OnTimer(400, playing: true, durationMs: 10_000));
        Assert.Equal(-1, p.OnTimer(500, playing: true, durationMs: 10_000));
    }

    [Fact]
    public void TheMandatoryVideoClock_TicksAsOftenAsTheDeeperPlayers()
        => Assert.True(PlayheadExtrapolator.TickMs <= 100);

    // ---------------------------------------------------------------- the subliminal latch

    [Fact]
    public void TwoPointFiredSubliminals_WaitingBehindOneHapticHeadStart_BothShow()
    {
        // Ambient subliminals off, a toy connected: both shows wait out the haptic head start.
        // The first to arrive clears the shared latch; the second still carries its generation.
        Assert.True(ConditioningControlPanel.Services.OneShotGate.AdmitsShow(
            ambientRunning: false, oneShotLatch: true, dispatchGeneration: 3));
        Assert.True(ConditioningControlPanel.Services.OneShotGate.AdmitsShow(
            ambientRunning: false, oneShotLatch: false, dispatchGeneration: 3));
    }

    [Fact]
    public void AnAmbientShow_ArrivingAfterTheServiceStopped_IsStillDropped()
        => Assert.False(ConditioningControlPanel.Services.OneShotGate.AdmitsShow(
            ambientRunning: false, oneShotLatch: false, dispatchGeneration: null));

    // ---------------------------------------------------------------- fakes

    private sealed class FakeSource : IPlaybackTimeSource
    {
        private double _t;
        public event Action<double>? PlaybackTimeChanged;
        public void Tick(double t) { _t = t; PlaybackTimeChanged?.Invoke(t); }
        public double GetCurrentTimeSeconds() => _t;
        public double GetDurationSeconds() => 600;
        public bool IsPlaying => true;
        public void Seek(double seconds) { }
        public void Pause() { }
        public void Play() { }
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
    }

    private sealed class FakeDispatcher : IActionDispatcher
    {
        public List<string> Texts { get; } = new();
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        {
            if (action is TriggerEffectAction { EffectType: EffectTypes.Subliminal } fx && fx.Text != null)
                Texts.Add(fx.Text);
            return Task.CompletedTask;
        }
    }
}
