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
        public int Closes, NoVideoNotices;
        public readonly List<bool> EffectsHolds = new();
        public void ShowNoVideos() => NoVideoNotices++;
        public void SetEffectsHold(bool hold) => EffectsHolds.Add(hold);
        public double LastVideoDurationSeconds { get; set; } = 60;
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) => Shown.Add((path, difficulty, strict, onComplete));
        public void ShowMessage(string text, int ms, Action then) => Messages.Add((text, then));
        public void CloseAll() => Closes++;
        public bool Feed;
        public Func<bool>? FeedState;
        public bool FeedOwnsTheScreen => FeedState?.Invoke() ?? Feed;
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

    [Fact]
    public void Empty_library_tells_once_per_launch_and_on_every_test() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Array.Empty<string>());
        try
        {
            b.Start();
            b.Trigger();
            clock.Advance(BubbleCountScheduler.LeadIn);
            Assert.False(b.IsBusy);
            Assert.Equal(1, host.NoVideoNotices);
            b.Trigger();
            clock.Advance(BubbleCountScheduler.LeadIn);
            Assert.Equal(1, host.NoVideoNotices);
            b.Stop();
            b.Start();
            b.Trigger();
            clock.Advance(BubbleCountScheduler.LeadIn);
            Assert.Equal(1, host.NoVideoNotices); // once per launch, not per engine start
            for (var i = 0; i < 2; i++)
            {
                b.Trigger(forceTest: true);
                clock.Advance(BubbleCountScheduler.LeadIn);
            }
            Assert.Equal(3, host.NoVideoNotices);
            Assert.Empty(host.Messages); // strict retry messages are not used for missing clips
            Assert.Empty(host.Shown);
        }
        finally { b.Stop(); b.ForceCleanup(); }
    });

    [Fact]
    public void Effects_hold_is_opt_in_latched_and_spans_strict_retries() => With(true, true, true, () =>
    {
        var s = CoreSettings.Current;
        var saved = s.BubbleCountPauseEffects;
        var flashWasRunning = CoreFlash.IsRunning;
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        try
        {
            CoreFlash.Start();
            s.BubbleCountPauseEffects = false;
            b.Trigger(forceTest: true);
            Assert.True(CoreFlash.IsRunning);
            Assert.Empty(host.EffectsHolds);
            b.ForceCleanup();

            s.BubbleCountPauseEffects = true;
            b.Trigger(forceTest: true);
            Assert.False(CoreFlash.IsRunning); // hold starts before the freeze lead-in
            Assert.Equal(new[] { true }, host.EffectsHolds);
            s.BubbleCountPauseEffects = false; // takes effect next round, never mid-round
            clock.Advance(BubbleCountScheduler.LeadIn);
            host.Shown[^1].Done(false);
            Assert.Equal(new[] { true }, host.EffectsHolds); // WRONG! WATCH AGAIN still holds
            host.Messages[^1].Then();
            host.Shown[^1].Done(true);
            Assert.Equal(new[] { true, false }, host.EffectsHolds);
            Assert.False(CoreFlash.IsRunning); // engine is stopped: Test Now must not start it
            b.ForceCleanup();
            Assert.Equal(2, host.EffectsHolds.Count); // release is idempotent
        }
        finally
        {
            b.ForceCleanup(); s.BubbleCountPauseEffects = saved;
            if (flashWasRunning) CoreFlash.Start(); else CoreFlash.Stop();
        }
    });

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    public void Flashes_resume_only_after_a_normal_end_if_previously_running_and_still_enabled(
        bool wasRunning, bool normalEnd, bool enabledAtEnd, bool resumes) => With(true, false, true, () =>
    {
        var s = CoreSettings.Current;
        var saved = (s.BubbleCountPauseEffects, s.FlashEnabled, s.AudioOnlySession, s.TotalSessions);
        var oldStopped = CoreEngine.StoppedHook;
        var oldBubble = CoreEngine.BubbleCount;
        var flashWasRunning = CoreFlash.IsRunning;
        var clock = new FakeClock(); var host = new Host();
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        try
        {
            CoreEngine.StoppedHook = null;
            CoreEngine.BubbleCount = b;
            s.AudioOnlySession = true; // no ambient services are started in this test
            s.BubbleCountPauseEffects = s.FlashEnabled = true;
            CoreEngine.Start();
            if (wasRunning) CoreFlash.Start(); else CoreFlash.Stop();
            b.Trigger(forceTest: true);
            Assert.False(CoreFlash.IsRunning);
            clock.Advance(BubbleCountScheduler.LeadIn);
            s.FlashEnabled = enabledAtEnd;
            if (normalEnd) host.Shown[^1].Done(true);
            else b.ForceCleanup(); // engine still true here, as it is during CoreEngine.Stop
            Assert.Equal(resumes, CoreFlash.IsRunning);
            Assert.Equal(new[] { true, false }, host.EffectsHolds);
        }
        finally
        {
            CoreEngine.Stop();
            CoreEngine.StoppedHook = oldStopped;
            CoreEngine.BubbleCount = oldBubble;
            (s.BubbleCountPauseEffects, s.FlashEnabled, s.AudioOnlySession, s.TotalSessions) = saved;
            if (flashWasRunning) CoreFlash.Start(); else CoreFlash.Stop();
        }
    });


    [Fact]
    public void Feed_defer_coalesces_and_replays_after_the_feed_leaves() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { Feed = true };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        var oldFreeze = CoreSubliminal.BambiFreezeProvider;
        var freezes = 0;
        CoreSubliminal.BambiFreezeProvider = () => freezes++;
        try
        {
            b.Start();
            b.Trigger(); b.Trigger();
            clock.Advance(TimeSpan.FromSeconds(89));
            Assert.False(b.IsBusy);
            Assert.Equal(0, freezes);
            host.Feed = false;
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.True(b.IsBusy);
            Assert.Equal(1, freezes);
            clock.Advance(BubbleCountScheduler.LeadIn);
            Assert.Single(host.Shown);
        }
        finally { b.Stop(); b.ForceCleanup(); CoreSubliminal.BambiFreezeProvider = oldFreeze; }
    });

    [Theory]
    [InlineData("expiry")]
    [InlineData("stop")]
    [InlineData("disabled")]
    [InlineData("panic")]
    public void Deferred_feed_game_drops_when_its_reason_expires(string reason) => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { Feed = true };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        try
        {
            b.Start(); b.Trigger();
            Assert.False(b.IsBusy);
            if (reason == "expiry") clock.Advance(TimeSpan.FromSeconds(90));
            if (reason == "stop") b.Stop();
            if (reason == "disabled") CoreSettings.Current.BubbleCountEnabled = false;
            if (reason == "panic") b.ForceCleanup();
            host.Feed = false;
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.False(b.IsBusy);
            Assert.Empty(host.Shown);
            b.Trigger(forceTest: true); // none of these paths leaves the defer latch stuck
            clock.Advance(BubbleCountScheduler.LeadIn);
            Assert.Single(host.Shown);
        }
        finally { b.Stop(); b.ForceCleanup(); }
    });


    [Theory]
    [InlineData(0, 0, "bubblecount_no_videos")]
    [InlineData(2, 0, "bubblecount_no_videos_packs_off")]
    [InlineData(2, 1, "bubblecount_no_videos")]
    public void Empty_library_advice_accounts_for_inactive_packs(int installed, int active, string key) =>
        Assert.Equal(key, BubbleCountScheduler.NoVideosMessageKey(installed, active));

    [Fact]
    public void Feed_returning_during_replay_inherits_the_original_deadline() => With(true, false, true, () =>
    {
        var clock = new FakeClock(); var host = new Host { Feed = true };
        var b = new BubbleCountScheduler(host, clock, () => Clips);
        try
        {
            b.Start(); b.Trigger();
            clock.Advance(TimeSpan.FromSeconds(89));
            var reads = 0;
            host.FeedState = () => ++reads != 1; // clear at the poll; back by Trigger's recheck
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.False(b.IsBusy);
            clock.Advance(TimeSpan.FromMilliseconds(500)); // original 90 s ceiling, not 179.5 s
            host.FeedState = null; host.Feed = false;
            clock.Advance(TimeSpan.FromSeconds(2));
            Assert.False(b.IsBusy);
            Assert.Empty(host.Shown);
        }
        finally { b.Stop(); b.ForceCleanup(); }
    });

}
