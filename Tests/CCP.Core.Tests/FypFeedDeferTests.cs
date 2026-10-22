using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

// The For You stand-down (WPF #1073/#1239): VideoService.DeferTriggerPastFeed and
// BubbleCountService.DeferGamePastFeed - held while the feed is on screen, one replay, 90 s ceiling,
// preconditions re-read at fire time, latch cleared by Stop.
[Collection(SessionStatics.Name)]
public sealed class FypFeedDeferTests
{
    private sealed class VideoHost : IMandatoryVideoHost
    {
        public readonly List<string> Shown = new();
        public void Show(string path, bool strict) => Shown.Add(path);
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    private sealed class GameHost : IBubbleCountHost
    {
        public readonly List<string> Shown = new();
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) => Shown.Add(path);
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
        public double LastVideoDurationSeconds => 60;
    }

    private static readonly string[] Clips = { "/v/a.mp4", "/v/b.mkv" };

    private static void WithFeed(Func<bool> onScreen, Action<List<string>> body)
    {
        var s = CoreSettings.Current;
        var (mv, vph, bc, fl) = (s.MandatoryVideosEnabled, s.VideosPerHour, s.BubbleCountEnabled, s.FlashEnabled);
        var (prevFeed, prevNotify) = (FypFeedDefer.FeedOnScreen, CoreProgram.NotifyProvider);
        var toasts = new List<string>();
        (s.MandatoryVideosEnabled, s.VideosPerHour, s.BubbleCountEnabled, s.FlashEnabled) = (true, 1, true, false);
        FypFeedDefer.FeedOnScreen = onScreen;
        CoreProgram.NotifyProvider = (_, kind, _) => toasts.Add(kind);
        try { body(toasts); }
        finally
        {
            FypFeedDefer.FeedOnScreen = prevFeed;
            CoreProgram.NotifyProvider = prevNotify;
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.BubbleCountEnabled, s.FlashEnabled) = (mv, vph, bc, fl);
        }
    }

    [Fact]
    public void Video_waits_out_the_feed_replays_once_and_only_a_press_is_announced()
    {
        var feed = true;
        WithFeed(() => feed, toasts =>
        {
            var clock = new FakeClock(); var host = new VideoHost();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.Start();
            Assert.True(v.Trigger());                         // scheduler-style: held, silent
            Assert.Empty(toasts);
            Assert.True(v.Trigger(userInitiated: true));      // the Test button: coalesced, but says who has the screen
            Assert.Equal(new[] { "Info" }, toasts);
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Empty(host.Shown);
            Assert.False(v.IsPlaying);

            feed = false;                                     // the feed closed
            clock.Advance(TimeSpan.FromSeconds(0.5 + 1.3));   // next poll, then the pre-roll
            Assert.Single(host.Shown);                        // one replay, not a backlog
            v.Stop();
        });
    }

    [Fact]
    public void Video_specific_clip_plays_over_the_feed() => WithFeed(() => true, _ =>
    {
        var clock = new FakeClock(); var host = new VideoHost();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        Assert.True(v.Trigger(false, "/v/a.mp4"));            // WPF PlaySpecificVideo has no feed guard
        clock.Advance(TimeSpan.FromSeconds(1.3));
        Assert.Equal(new[] { "/v/a.mp4" }, host.Shown);
        v.ForceCleanup();
    });

    [Fact]
    public void Video_hold_expires_at_90s_and_a_stop_abandons_the_replay()
    {
        var feed = true;
        WithFeed(() => feed, _ =>
        {
            var clock = new FakeClock(); var host = new VideoHost();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            Assert.True(v.Trigger());
            clock.Advance(FypFeedDefer.MaxWait + TimeSpan.FromSeconds(1));
            feed = false;
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Empty(host.Shown);                         // expired while the feed was still up

            feed = true;
            v.Start();
            Assert.True(v.Trigger());
            v.Stop();                                         // engine stopped while held
            feed = false;
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Empty(host.Shown);
        });
    }

    [Fact]
    public void BubbleCount_waits_out_the_feed_and_rereads_the_setting_at_fire_time()
    {
        var feed = true;
        WithFeed(() => feed, _ =>
        {
            var clock = new FakeClock(); var host = new GameHost();
            var g = new BubbleCountScheduler(host, clock, () => Clips);
            g.Trigger(forceTest: true);
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.Empty(host.Shown);
            Assert.False(g.IsBusy);

            feed = false;
            clock.Advance(TimeSpan.FromSeconds(0.5 + 0.8));   // next poll, then the lead-in
            Assert.Single(host.Shown);
            g.ForceCleanup();

            feed = true;
            g.Start();
            g.Trigger();
            CoreSettings.Current.BubbleCountEnabled = false;  // switched off while held (#872)
            feed = false;
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Single(host.Shown);
            g.Stop();
        });
    }
}
