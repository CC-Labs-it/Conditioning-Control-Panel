using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

// WPF VideoService.DeferTriggerPastFeed (#1073/#1239):
// held while the feed is on screen, one replay, 90 s ceiling,
// preconditions re-read at fire time, latch cleared by Stop.
[Collection(SessionStatics.Name)]
public sealed class FypFeedDeferTests
{
    private sealed class VideoHost : IMandatoryVideoHost
    {
        public readonly List<string> Shown = new();
        public readonly List<bool> Strictness = new();
        public Action? Then;
        public Func<bool> Feed = () => true;
        public bool FeedOwnsTheScreen => Feed();
        public void Show(string path, bool strict) { Shown.Add(path); Strictness.Add(strict); }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => Then = then;
    }

    private static readonly string[] Clips = { "/v/a.mp4", "/v/b.mkv" };

    private static void WithSettings(Action<List<string>> body)
    {
        var s = CoreSettings.Current;
        var (mv, vph, bc, fl) = (s.MandatoryVideosEnabled, s.VideosPerHour, s.BubbleCountEnabled, s.FlashEnabled);
        var prevNotify = CoreProgram.NotifyProvider;
        var toasts = new List<string>();
        (s.MandatoryVideosEnabled, s.VideosPerHour, s.BubbleCountEnabled, s.FlashEnabled) = (true, 1, true, false);
        CoreProgram.NotifyProvider = (_, kind, _) => toasts.Add(kind);
        try { body(toasts); }
        finally
        {
            CoreProgram.NotifyProvider = prevNotify;
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.BubbleCountEnabled, s.FlashEnabled) = (mv, vph, bc, fl);
        }
    }

    [Fact]
    public void Video_waits_out_the_feed_replays_once_and_only_a_press_is_announced()
    {
        var feed = true;
        WithSettings(toasts =>
        {
            var clock = new FakeClock(); var host = new VideoHost { Feed = () => feed };
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
    public void Video_specific_clip_plays_over_the_feed() => WithSettings(_ =>
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
        WithSettings(_ =>
        {
            var clock = new FakeClock(); var host = new VideoHost { Feed = () => feed };
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.Start();
            Assert.True(v.Trigger());
            clock.Advance(TimeSpan.FromSeconds(89));
            Assert.True(v.Trigger()); // coalescing must not renew the original deadline
            clock.Advance(TimeSpan.FromSeconds(2));
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

    // WPF VideoService.cs:5964-5965: a failed check selects a clip and plays it directly.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_check_retries_over_feed_with_retained_strictness_and_bubble_cleanup(bool emptyLibrary)
        => WithSettings(_ =>
    {
        var s = CoreSettings.Current;
        var saved = (s.AttentionChecksEnabled, s.MercySystemEnabled, s.StrictLockEnabled,
            CoreBubbles.PauseAction, CoreBubbles.ResumeAction);
        var paused = false;
        var resumes = 0;
        var clock = new FakeClock(); var host = new VideoHost(); // feed stays on screen throughout
        var v = new MandatoryVideoScheduler(host, clock,
            () => emptyLibrary ? Array.Empty<string>() : new[] { Clips[1] });
        try
        {
            s.AttentionChecksEnabled = true;
            s.MercySystemEnabled = false;
            s.StrictLockEnabled = false; // the explicit strict run must retain its override on retry
            CoreBubbles.PauseAction = () => paused = true;
            CoreBubbles.ResumeAction = () => { paused = false; resumes++; };
            Assert.True(v.Trigger(strictOverride: true, path: Clips[0]));
            clock.Advance(TimeSpan.FromSeconds(1.3));
            Assert.True(paused);
            v.NoteSpawn(); v.Ended(); // missed target, waiting for the verdict message
            Assert.Equal(1, v.Penalties);
            Assert.Equal(0, resumes);
            Assert.NotNull(host.Then);
            host.Then!();
            clock.Advance(TimeSpan.FromSeconds(1.3));
            Assert.Equal(new[] { Clips[0], emptyLibrary ? Clips[0] : Clips[1] }, host.Shown);
            Assert.All(host.Strictness, Assert.True);
            Assert.True(v.IsStrict);
            clock.Advance(TimeSpan.FromSeconds(91)); // no feed expiry may abandon the retry
            Assert.True(v.IsPlaying);
            Assert.True(paused);
            Assert.Equal(0, resumes);
            s.AttentionChecksEnabled = false;
            v.Ended();
            Assert.False(v.IsPlaying);
            Assert.False(paused);
            Assert.Equal(1, resumes);
        }
        finally
        {
            v.Stop();
            (s.AttentionChecksEnabled, s.MercySystemEnabled, s.StrictLockEnabled,
                CoreBubbles.PauseAction, CoreBubbles.ResumeAction) = saved;
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Earned_video_bypasses_feed_without_notice_but_not_in_progress_guard(bool initiated)
        => WithSettings(toasts =>
    {
        var clock = new FakeClock(); var host = new VideoHost();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        Assert.True(v.Trigger(userInitiated: initiated, userEarned: true));
        Assert.False(v.Trigger(userEarned: true));
        clock.Advance(MandatoryVideoScheduler.PreRoll);
        Assert.Single(host.Shown);
        Assert.Empty(toasts);
        v.ForceCleanup();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disable_or_panic_abandons_held_video(bool panic)
    {
        var feed = true;
        WithSettings(_ =>
        {
            var clock = new FakeClock(); var host = new VideoHost { Feed = () => feed };
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.Start();
            Assert.True(v.Trigger());
            if (panic) v.ForceCleanup();
            else CoreSettings.Current.MandatoryVideosEnabled = false;
            feed = false;
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Empty(host.Shown);
            v.Stop();
        });
    }
}
