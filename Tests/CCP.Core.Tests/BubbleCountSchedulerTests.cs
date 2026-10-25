using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

// BubbleCountScheduler against WPF BubbleCountService: schedule rate, 800 ms lead-in, completion XP
// (duration-scaled, 3-minute cooldown), strict retry with mercy at 3, and panic via CoreEngine.Stop.
[Collection(SessionStatics.Name)]
public sealed class BubbleCountSchedulerTests
{
    private sealed class Host : IBubbleCountHost
    {
        public readonly List<(string Path, int Difficulty, bool Strict, Action<bool> Done)> Shown = new();
        public readonly List<(string Text, Action Then)> Messages = new();
        public int Closes;
        public double LastVideoDurationSeconds { get; set; } = 60;
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) => Shown.Add((path, difficulty, strict, onComplete));
        public void ShowMessage(string text, int ms, Action then) => Messages.Add((text, then));
        public void CloseAll() => Closes++;
        public bool FeedOwnsTheScreen { get; set; }
        public int Notices;
        public void TellNoVideos() => Notices++;
        public bool Other;
        public bool OtherInteractionActive => Other;
    }

    private static void With(bool enabled, bool strict, bool mercy, Action body)
    {
        var s = CoreSettings.Current;
        var (e, f, st, m, d) = (s.BubbleCountEnabled, s.BubbleCountFrequency, s.BubbleCountStrictLock, s.MercySystemEnabled, s.BubbleCountDifficulty);
        (s.BubbleCountEnabled, s.BubbleCountFrequency, s.BubbleCountStrictLock, s.MercySystemEnabled, s.BubbleCountDifficulty) = (enabled, 6, strict, mercy, 2);
        var xp = new List<(double, string)>();
        var prevXp = CoreProgression.AddXPProvider;
        CoreProgression.AddXPProvider = (a, src) => xp.Add((a, src));
        try { body(); }
        finally
        {
            CoreProgression.AddXPProvider = prevXp;
            (s.BubbleCountEnabled, s.BubbleCountFrequency, s.BubbleCountStrictLock, s.MercySystemEnabled, s.BubbleCountDifficulty) = (e, f, st, m, d);
        }
    }

    private static readonly string[] Clips = { "/v/a.mp4", "/v/b.mkv" };

    private sealed class VideoHost : IMandatoryVideoHost
    {
        public int Shown;
        public void Show(string path, bool strict) => Shown++;
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Feed_replay_rechecks_engine_and_enabled_flag(bool stop) => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { FeedOwnsTheScreen = true };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Start(); b.Trigger();
        if (stop) b.Stop(); else CoreSettings.Current.BubbleCountEnabled = false;
        host.FeedOwnsTheScreen = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(host.Shown);
        Assert.False(b.IsBusy);
        b.Stop();
    });

    [Fact]
    public void Mandatory_video_waits_for_game_and_panic_drops_pending_video() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host(); var videoHost = new VideoHost();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        var video = new MandatoryVideoScheduler(videoHost, clock, () => Clips);
        var prev = CoreEngine.BubbleCount;
        CoreEngine.BubbleCount = b;
        try
        {
            b.Trigger(true); clock.Advance(BubbleCountScheduler.LeadIn);
            Assert.True(video.Trigger());
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.False(video.IsPlaying);
            Assert.Equal(0, videoHost.Shown);
            host.Shown[0].Done(true);
            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.True(video.IsPlaying);
            Assert.Equal(1, videoHost.Shown);
            video.ForceCleanup();
            b.Trigger(true); video.Trigger(); video.ForceCleanup(); b.ForceCleanup();
            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal(1, videoHost.Shown);
        }
        finally { video.ForceCleanup(); b.ForceCleanup(); CoreEngine.BubbleCount = prev; }
    });

    [Fact]
    public void Feed_coalesces_triggers_then_replays_without_freezing_early() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { FeedOwnsTheScreen = true };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Trigger(true); b.Trigger(true);
        Assert.False(b.IsBusy);
        clock.Advance(TimeSpan.FromSeconds(89));
        Assert.Empty(host.Shown);
        host.FeedOwnsTheScreen = false;
        clock.Advance(TimeSpan.FromSeconds(0.5));
        Assert.True(b.IsBusy);
        clock.Advance(BubbleCountScheduler.LeadIn);
        Assert.Single(host.Shown);
        b.ForceCleanup();
    });

    [Fact]
    public void Feed_wait_expires_and_panic_cancels_replay() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { FeedOwnsTheScreen = true };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Trigger(true);
        clock.Advance(TimeSpan.FromSeconds(91));
        host.FeedOwnsTheScreen = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(host.Shown);
        host.FeedOwnsTheScreen = true;
        b.Trigger(true); b.ForceCleanup();
        host.FeedOwnsTheScreen = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(host.Shown);
    });

    [Fact]
    public void Empty_library_warns_once_per_launch_but_every_test() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Array.Empty<string>());
        b.Start();
        for (int i = 0; i < 2; i++) { b.Trigger(); clock.Advance(TimeSpan.FromSeconds(1)); }
        Assert.Equal(1, host.Notices);
        b.Trigger(true); clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, host.Notices);
        Assert.False(b.IsBusy);
        b.Stop();
    });

    [Fact]
    public void Stuck_game_uses_duration_plus_150_seconds_and_retry_renews() => With(true, true, false, () =>
    {
        var clock = new FakeClock(); var host = new Host { LastVideoDurationSeconds = 400 };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Trigger(true); clock.Advance(BubbleCountScheduler.LeadIn);
        clock.Advance(TimeSpan.FromSeconds(549));
        Assert.True(b.IsBusy);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(b.IsBusy);
        Assert.Equal(1, host.Closes);
        b.Trigger(true); clock.Advance(BubbleCountScheduler.LeadIn);
        host.Shown[^1].Done(false);
        host.Messages[^1].Then();
        clock.Advance(TimeSpan.FromSeconds(329));
        Assert.True(b.IsBusy);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(b.IsBusy);
    });

    [Fact]
    public void Rules_match_wpf()
    {
        Assert.Equal(360, BubbleCountScheduler.NextIntervalSeconds(100, 0.5));    // clamped to 10/h
        Assert.Equal(2880, BubbleCountScheduler.NextIntervalSeconds(1, 0));        // 3600 * 0.8
        Assert.Equal(100, BubbleCountScheduler.ScaleXpByDuration(100, 90));
        Assert.Equal(50, BubbleCountScheduler.ScaleXpByDuration(100, 30));
        Assert.Equal(10, BubbleCountScheduler.ScaleXpByDuration(100, 1));          // 10% floor
        Assert.Equal(5, BubbleCountScheduler.TargetBubbles(BubbleCountScheduler.Difficulty.Medium, 30, 0.5));
        Assert.Equal(16, BubbleCountScheduler.TargetBubbles(BubbleCountScheduler.Difficulty.Hard, 60, 0.5));
        Assert.Equal(3, BubbleCountScheduler.TargetBubbles(BubbleCountScheduler.Difficulty.Easy, 5, 0));  // min 3
    }

    [Fact]
    public void Schedule_lead_in_and_xp_cooldown() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { LastVideoDurationSeconds = 30 };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        var xp = new List<double>();
        CoreProgression.AddXPProvider = (a, _) => xp.Add(a);
        var held = 0;
        CoreBubbles.PauseAction = () => held++;
        CoreBubbles.ResumeAction = () => held--;
        var freezes = 0;
        CoreSubliminal.BambiFreezeProvider = () => freezes++;
        b.Start();
        clock.Advance(TimeSpan.FromSeconds(479));              // 6/h: 480-720 s
        Assert.False(b.IsBusy);
        Assert.Equal(0, freezes);
        clock.Advance(TimeSpan.FromSeconds(242));
        Assert.True(b.IsBusy);
        Assert.Equal(1, freezes);                              // WPF :353: Bambi Freeze with the game
        clock.Advance(TimeSpan.FromSeconds(0.8));              // lead-in
        CoreSubliminal.BambiFreezeProvider = null;
        var game = Assert.Single(host.Shown);
        Assert.Equal(2, game.Difficulty);
        Assert.Equal(1, held);                                 // WPF PauseAndClear: ambient bubbles held
        game.Done(true);
        Assert.False(b.IsBusy);
        Assert.Equal(0, held);                                 // and resumed
        Assert.Equal(new[] { 50.0 }, xp);                      // 100 scaled by a 30 s clip

        b.Trigger(forceTest: true);
        clock.Advance(TimeSpan.FromSeconds(1));
        host.Shown[1].Done(true);
        Assert.Single(xp);                                     // inside the 3-minute cooldown
        CoreBubbles.PauseAction = CoreBubbles.ResumeAction = null;
    });

    [Fact]
    public void Strict_failure_retries_then_mercy_after_three() => With(true, true, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Trigger(forceTest: true);
        clock.Advance(TimeSpan.FromSeconds(1));
        for (int i = 0; i < 2; i++)
        {
            host.Shown[^1].Done(false);
            Assert.Equal("WRONG!\nWATCH AGAIN", host.Messages[^1].Text);
            host.Messages[^1].Then();
            Assert.True(host.Shown[^1].Strict);                // the rewatch is always strict
        }
        Assert.Equal(3, host.Shown.Count);
        host.Shown[^1].Done(false);
        Assert.Equal("BAMBI GETS MERCY", host.Messages[^1].Text);
        Assert.True(b.IsBusy);
        host.Messages[^1].Then();
        Assert.False(b.IsBusy);
        Assert.Equal(3, host.Shown.Count);
    });

    /// <summary>#1145: the Mercy picker moves the threshold (here 2 wrong tries).</summary>
    [Fact]
    public void Mercy_comes_after_the_picked_number_of_fails() => With(true, true, true, () =>
    {
        var s = CoreSettings.Current;
        var saved = s.MercyAfterFails;
        s.MercyAfterFails = 2;
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var b = new BubbleCountScheduler(host, clock, () => Clips);
            b.Trigger(forceTest: true);
            clock.Advance(TimeSpan.FromSeconds(1));
            host.Shown[^1].Done(false);
            Assert.Equal("WRONG!\nWATCH AGAIN", host.Messages[^1].Text);
            host.Messages[^1].Then();
            host.Shown[^1].Done(false);
            Assert.Equal("BAMBI GETS MERCY", host.Messages[^1].Text);
            Assert.Equal(2, host.Shown.Count);
        }
        finally { s.MercyAfterFails = saved; }
    });

    [Fact]
    public void Non_strict_failure_ends_and_disabled_drops_scheduled_games() => With(false, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Start();                                             // disabled: never runs
        Assert.False(b.IsRunning);
        b.Trigger(forceTest: true);                            // the Test button ignores the flag
        clock.Advance(TimeSpan.FromSeconds(1));
        host.Shown[0].Done(false);
        Assert.False(b.IsBusy);
        Assert.Empty(host.Messages);
    });

    [Fact]
    public void Panic_engine_stop_closes_the_game_and_a_late_answer_is_ignored() => With(true, true, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        var prev = CoreEngine.BubbleCount;
        CoreEngine.BubbleCount = b;
        try
        {
            b.Trigger(forceTest: true);
            clock.Advance(TimeSpan.FromSeconds(1));
            CoreEngine.Stop();
            Assert.Equal(1, host.Closes);
            Assert.False(b.IsBusy);
            host.Shown[0].Done(false);                         // the closing window's own callback
            Assert.Empty(host.Messages);                       // no strict retry after panic

            b.Trigger(forceTest: true);                        // panic during the lead-in
            CoreEngine.Stop();
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Single(host.Shown);
        }
        finally { CoreEngine.BubbleCount = prev; }
    });

    /// <summary>WPF TriggerGame :321: a game due over a video / lock card queues and plays once it
    /// ends; panic drops the queued game.</summary>
    [Fact]
    public void Game_waits_for_another_interaction_and_panic_drops_it() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { Other = true };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Start();
        clock.Advance(TimeSpan.FromSeconds(721));              // scheduled game falls due
        Assert.True(b.IsQueued);
        Assert.False(b.IsBusy);
        clock.Advance(TimeSpan.FromSeconds(30));               // the video is still playing
        Assert.Empty(host.Shown);
        host.Other = false;                                    // the video ends
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(b.IsBusy);
        clock.Advance(BubbleCountScheduler.LeadIn);
        Assert.Single(host.Shown);
        host.Shown[0].Done(true);

        host.Other = true;
        b.Trigger(forceTest: true);
        Assert.True(b.IsQueued);
        b.ForceCleanup();                                      // panic
        host.Other = false;
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(b.IsQueued);
        Assert.Single(host.Shown);
        b.Stop();
    });

    /// <summary>WPF TriggerGame :260/:277: a queued game replayed after Stop, or after the card
    /// was switched off, is dropped.</summary>
    [Fact]
    public void Queued_game_drops_after_stop_or_card_off() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { Other = true };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        b.Start();
        clock.Advance(TimeSpan.FromSeconds(721));
        Assert.True(b.IsQueued);
        b.Stop();
        host.Other = false;
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(b.IsBusy);
        Assert.Empty(host.Shown);

        host.Other = true;
        b.Start();
        clock.Advance(TimeSpan.FromSeconds(721));
        Assert.True(b.IsQueued);
        CoreSettings.Current.BubbleCountEnabled = false;       // the card toggle
        host.Other = false;
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.False(b.IsBusy);
        Assert.Empty(host.Shown);
        b.Stop();
    });
}
