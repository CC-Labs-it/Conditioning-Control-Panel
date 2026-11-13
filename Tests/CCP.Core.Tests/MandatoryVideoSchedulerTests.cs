using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

// MandatoryVideoScheduler against WPF VideoService: ScheduleNext rate, 1.3 s pre-roll, Cleanup
// re-arms / ForceCleanup does not, watch credit, strict keys, and the CoreEngine start/stop.
[Collection(SessionStatics.Name)]
public sealed class MandatoryVideoSchedulerTests
{
    private sealed class Host : IMandatoryVideoHost
    {
        public readonly List<(string Path, bool Strict)> Shown = new();
        public bool Floating;
        public bool Lockdown;
        public bool IsFloating(bool strict) => Floating && !strict && !Lockdown;
        public int FloatingTriggers;
        public bool TryFloatingTrigger(bool userInitiated) { FloatingTriggers++; return userInitiated; }
        public bool BubbleCountActive { get; set; }
        public int Closes, Freezes;
        public bool ThrowOnFreeze;
        public readonly List<bool> FreezeEnds = new();
        public void Freeze() { Freezes++; if (ThrowOnFreeze) throw new InvalidOperationException("Freeze probe"); }
        public void FinishFreeze(bool cancelled) { FreezeEnds.Add(cancelled); if (ThrowOnFreeze) throw new InvalidOperationException("Reset probe"); }
        public double Watched;
        public bool ThrowOnClose;
        public readonly List<bool> FlashesAtShow = new();
        public void Show(string path, bool strict)
        {
            FlashesAtShow.Add(CoreFlash.IsRunning);
            Shown.Add((path, strict));
        }
        public double CloseAll() { Closes++; if (ThrowOnClose) throw new InvalidOperationException(); return Watched; }
        public readonly List<AttentionVerdict> Messages = new();
        public Action? Then;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { Messages.Add(verdict); Then = then; }
    }

    [Theory]
    [InlineData(true, false, false, 1)]
    [InlineData(false, false, false, 0)]
    [InlineData(true, true, false, 0)]
    [InlineData(true, false, true, 0)]
    public void Playing_trigger_only_reaches_floating_host(bool floating, bool strict, bool lockdown, int calls)
        => With(20, strict, () =>
    {
        var host = new Host { Floating = floating, Lockdown = lockdown };
        var video = new MandatoryVideoScheduler(host, new FakeClock(), () => Clips);
        try
        {
            Assert.True(video.Trigger());
            Assert.Equal(calls == 1, video.Trigger(userInitiated: true));
            Assert.Equal(calls, host.FloatingTriggers);
        }
        finally { video.ForceCleanup(); }
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void Video_freeze_has_800ms_head_start_and_reset_on_cleanup(bool attention, bool forced, bool throwing)
    {
        var surface = CoreSubliminal.BambiFreezeProvider;
        var wasAttention = CoreSettings.Current.AttentionChecksEnabled;
        var clock = new FakeClock();
        var host = new Host { ThrowOnFreeze = throwing };
        var video = new MandatoryVideoScheduler(host, clock, () => Clips);
        CoreSubliminal.BambiFreezeProvider = () => { };   // a head with a Freeze surface
        CoreSettings.Current.AttentionChecksEnabled = attention;
        try
        {
            Assert.True(video.Trigger());
            Assert.Equal(attention ? 0 : 1, host.Freezes);
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(attention ? 1 : 0, host.Shown.Count);
            clock.Advance(MandatoryVideoScheduler.FreezeLead);
            Assert.Single(host.Shown);
            if (forced) video.ForceCleanup(); else video.End();
            video.End();
            // The Freeze ends once: its Reset after a natural end, a cancel after a forced one.
            Assert.Equal(attention ? Array.Empty<bool>() : new[] { forced }, host.FreezeEnds);
        }
        finally
        {
            video.ForceCleanup();
            CoreSettings.Current.AttentionChecksEnabled = wasAttention;
            CoreSubliminal.BambiFreezeProvider = surface;
        }
    }

    private static void With(int perHour, bool strict, Action body)
    {
        var s = CoreSettings.Current;
        var (e, p, st, f) = (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.FlashEnabled);
        (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.FlashEnabled) = (true, perHour, strict, false);
        try { body(); }
        finally { (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.FlashEnabled) = (e, p, st, f); }
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    public void Trigger_preserves_flashes_only_for_a_floating_video(bool floating, bool strict, bool stays)
        => With(20, strict, () =>
    {
        var s = CoreSettings.Current;
        s.FlashEnabled = true;
        var clock = new FakeClock(); var host = new Host { Floating = floating };
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        CoreFlash.Start();
        try
        {
            Assert.True(CoreFlash.IsRunning);
            Assert.True(v.Trigger()); // Test and scheduler share this prologue.
            Assert.Equal(stays, CoreFlash.IsRunning);
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(stays, CoreFlash.IsRunning);
        }
        finally { v.ForceCleanup(); CoreFlash.Stop(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_screen_decided_during_pre_roll_stops_flashes_before_show(bool lockStarts)
        => With(20, false, () =>
    {
        CoreSettings.Current.FlashEnabled = true;
        var clock = new FakeClock(); var host = new Host { Floating = true };
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        CoreFlash.Start();
        try
        {
            Assert.True(v.Trigger());
            Assert.True(CoreFlash.IsRunning);
            clock.Advance(MandatoryVideoScheduler.PreRoll / 2);
            if (lockStarts) host.Lockdown = true;
            else host.Floating = false;
            Assert.True(CoreFlash.IsRunning);
            clock.Advance(MandatoryVideoScheduler.PreRoll / 2);
            Assert.Single(host.Shown);
            Assert.False(Assert.Single(host.FlashesAtShow));
            Assert.False(CoreFlash.IsRunning);
        }
        finally { v.ForceCleanup(); CoreFlash.Stop(); }
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void Replay_switch_keeps_failure_credit_and_strict_replay(bool strict, bool lockdown, bool replay) => With(6, strict, () =>
    {
        var s = CoreSettings.Current;
        var old = (s.AttentionChecksEnabled, s.MandatoryVideoReplayAfterFailedCheck, s.MercySystemEnabled);
        var checks = new List<bool>();
        var prior = CoreProgression.TrackAttentionCheckProvider;
        CoreProgression.TrackAttentionCheckProvider = checks.Add;
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        var priorLockdown = LockdownService.Current;
        var activeLockdown = new LockdownService();
        try
        {
            (s.AttentionChecksEnabled, s.MandatoryVideoReplayAfterFailedCheck, s.MercySystemEnabled) = (true, false, false);
            LockdownService.Current = lockdown ? activeLockdown : null;
            if (lockdown) activeLockdown.Activate(TimeSpan.FromMinutes(10));
            Assert.True(v.Trigger(strictOverride: strict)); clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(strict, v.IsStrict);
            v.NoteSpawn(); v.Ended();
            Assert.Equal(new[] { false }, checks);
            Assert.Equal(replay ? 1 : 0, host.Messages.Count);
            if (replay) host.Then!();
            Assert.Equal(replay, v.IsPlaying);
        }
        finally
        {
            v.Stop(); activeLockdown.Dispose(); LockdownService.Current = priorLockdown;
            CoreProgression.TrackAttentionCheckProvider = prior;
            (s.AttentionChecksEnabled, s.MandatoryVideoReplayAfterFailedCheck, s.MercySystemEnabled) = old;
        }
    });

    private static readonly string[] Clips = { "/v/a.mp4", "/v/b.mkv" };

    [Theory]
    [InlineData("end")]
    [InlineData("ended")]
    [InlineData("force")]
    [InlineData("stop")]
    [InlineData("retry")]
    public void VideoEnded_fires_once_after_the_clip_closes(string ending) => With(6, false, () =>
    {
        var attention = CoreSettings.Current.AttentionChecksEnabled;
        CoreSettings.Current.AttentionChecksEnabled = true;
        try
        {
            var host = new Host();
            var video = new MandatoryVideoScheduler(host, new FakeClock(), () => Clips);
            int ended = 0;
            video.VideoEnded += () =>
            {
                Assert.False(video.IsPlaying);
                Assert.Equal(1, host.Closes);
                ended++;
            };
            Assert.True(video.Trigger());
            if (ending == "retry") video.NoteSpawn();
            if (ending == "end") video.End();
            else if (ending is "ended" or "retry") video.Ended(); // natural end or an attention retry
            else if (ending == "force") video.ForceCleanup();
            else video.Stop();
            video.End(); video.ForceCleanup(); // idle cleanup must not notify twice
            Assert.Equal(1, ended);
        }
        finally { CoreSettings.Current.AttentionChecksEnabled = attention; }
    });

    [Fact]
    public void Idle_panic_contains_a_host_close_failure()
    {
        var host = new Host { ThrowOnClose = true };
        var v = new MandatoryVideoScheduler(host, new FakeClock(), () => Clips);
        Assert.Null(Record.Exception(v.ForceCleanup));
        Assert.Equal(1, host.Closes);
    }

    [Fact]
    public void Panic_after_natural_end_still_drops_the_hosts_fading_curtain() => With(6, false, () =>
    {
        var host = new Host();
        var v = new MandatoryVideoScheduler(host, new FakeClock(), () => Clips);
        Assert.True(v.Trigger());
        v.Ended();
        Assert.False(v.IsPlaying);
        var closed = host.Closes;
        v.ForceCleanup();
        Assert.Equal(closed + 1, host.Closes);
    });

    [Fact]
    public void Video_waits_for_the_whole_bubble_round_and_keeps_the_first_request() => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host { BubbleCountActive = true };
        var picks = 0;
        var v = new MandatoryVideoScheduler(host, clock, () => { picks++; return Clips; });
        try
        {
            Assert.True(v.Trigger(true, "/earned.mp4"));
            Assert.True(v.Trigger(false, "/duplicate.mp4"));
            clock.Advance(TimeSpan.FromMinutes(6)); // a long clip/strict retry still owns the screen
            Assert.False(v.IsPlaying);
            Assert.Empty(host.Shown);
            Assert.Equal(0, picks);
            host.BubbleCountActive = false;
            clock.Advance(BubbleCountScheduler.QueuePoll);
            Assert.True(v.IsPlaying);
            Assert.Empty(host.Shown);
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            var shown = Assert.Single(host.Shown);
            Assert.Equal("/earned.mp4", shown.Path);
            Assert.True(shown.Strict);
        }
        finally { v.Stop(); }
    });

    [Fact]
    public void Duplicate_during_bubble_handoff_keeps_the_pending_clip_and_strictness() => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host { BubbleCountActive = true };
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        try
        {
            Assert.True(v.Trigger(true, "/first.mp4"));
            clock.Advance(TimeSpan.FromMilliseconds(500));
            host.BubbleCountActive = false;
            Assert.True(v.Trigger(false, "/duplicate.mp4")); // before the pending poll runs
            Assert.False(v.IsPlaying);
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.True(v.IsPlaying);
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            var shown = Assert.Single(host.Shown);
            Assert.Equal("/first.mp4", shown.Path);
            Assert.True(shown.Strict);
        }
        finally { v.Stop(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deferred_library_failure_is_contained_and_only_scheduled_playback_rearms(bool scheduled) => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host { BubbleCountActive = true };
        var picks = 0;
        var v = new MandatoryVideoScheduler(host, clock, () =>
        {
            if (++picks == 1) throw new System.IO.IOException("library unavailable");
            return Clips;
        });
        try
        {
            if (scheduled) { v.Start(); clock.Advance(TimeSpan.FromSeconds(722)); }
            else Assert.True(v.Trigger());
            host.BubbleCountActive = false;
            Assert.Null(Record.Exception(() => clock.Advance(BubbleCountScheduler.QueuePoll)));
            Assert.Equal(1, picks);
            Assert.False(v.IsPlaying);
            clock.Advance(TimeSpan.FromSeconds(724)); // next 480..720 s interval plus pre-roll
            if (scheduled)
            {
                Assert.Equal(2, picks);
                Assert.Single(host.Shown);
            }
            else
            {
                Assert.Equal(1, picks);
                Assert.Empty(host.Shown);
                Assert.True(v.Trigger()); // latch released: an explicit retry can still play
                clock.Advance(MandatoryVideoScheduler.PreRoll);
                Assert.Single(host.Shown);
            }
        }
        finally { v.Stop(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Waiting_video_is_cancelled_by_panic_or_stop(bool stop) => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host { BubbleCountActive = true };
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        Assert.True(v.Trigger());
        if (stop) v.Stop(); else v.ForceCleanup();
        host.BubbleCountActive = false;
        clock.Advance(TimeSpan.FromHours(1));
        Assert.False(v.IsPlaying);
        Assert.Empty(host.Shown);
    });

    [Fact]
    public void Scheduled_video_waiting_for_bubbles_drops_if_the_card_is_switched_off() => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host { BubbleCountActive = true };
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        try
        {
            v.Start();
            clock.Advance(TimeSpan.FromSeconds(722));
            CoreSettings.Current.MandatoryVideosEnabled = false;
            host.BubbleCountActive = false;
            clock.Advance(TimeSpan.FromSeconds(3));
            Assert.False(v.IsPlaying);
            Assert.Empty(host.Shown);
        }
        finally { v.Stop(); }
    });

    [Fact]
    public void CascadeHoldsAndCoalescesEarnedClipThenReplaysWithOriginalStrictness() => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host(); var rain = true;
        var v = new MandatoryVideoScheduler(host, clock, () => Clips) { CascadeIsRaining = () => rain };
        Assert.True(v.Trigger(true, Clips[0]));
        Assert.True(v.Trigger(false, Clips[1]));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(v.IsPlaying);
        Assert.Empty(host.Shown);
        rain = false;
        clock.Advance(TimeSpan.FromMilliseconds(250));
        Assert.True(v.IsPlaying);
        clock.Advance(MandatoryVideoScheduler.PreRoll);
        Assert.Equal((Clips[0], true), Assert.Single(host.Shown));
        v.Stop();
    });

    [Fact]
    public void ScheduledCascadeReplayRechecksFeatureEnabled() => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host(); var rain = true;
        var v = new MandatoryVideoScheduler(host, clock, () => Clips) { CascadeIsRaining = () => rain };
        v.Start();
        v.Trigger(scheduled: true);
        CoreSettings.Current.MandatoryVideosEnabled = false;
        rain = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(host.Shown);
        v.Stop();
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CascadeDeadlineDoesNotExtendAndCleanupCancelsPendingReplay(bool forceCleanup) => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host(); var rain = true;
        var v = new MandatoryVideoScheduler(host, clock, () => Clips) { CascadeIsRaining = () => rain };
        v.Trigger();
        clock.Advance(TimeSpan.FromSeconds(89));
        v.Trigger();
        clock.Advance(TimeSpan.FromSeconds(1));
        rain = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(host.Shown);
        rain = true;
        v.Trigger();
        if (forceCleanup) v.ForceCleanup(); else v.Stop();
        rain = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(host.Shown);
        v.Trigger(); // a stopped deferred request cannot latch future earned triggers
        clock.Advance(MandatoryVideoScheduler.PreRoll);
        Assert.Single(host.Shown);
        v.Stop();
    });

    [Theory]   // WPF ChaosGifCascadeOverlay.cs:173-182: a poll at the deadline replays when the rain has cleared
    [InlineData(false)]
    [InlineData(true)]
    public void CascadeClearingExactlyAtTheDeadlineStillReplays(bool clearAtDeadline) => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var deadline = clock.GetUtcNow() + TimeSpan.FromSeconds(90);
        var v = new MandatoryVideoScheduler(host, clock, () => Clips)
        { CascadeIsRaining = () => !clearAtDeadline || clock.GetUtcNow() < deadline };
        Assert.True(v.Trigger(path: Clips[0]));
        clock.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(clearAtDeadline, v.IsPlaying);
        clock.Advance(MandatoryVideoScheduler.PreRoll);
        Assert.Equal(clearAtDeadline ? 1 : 0, host.Shown.Count);
        v.Stop();
    });

    [Fact]
    public void EmptyLibraryAfterCascadeRearmsSchedule() => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host(); var rain = true; var probes = 0;
        IReadOnlyList<string> clips = Array.Empty<string>();
        var v = new MandatoryVideoScheduler(host, clock, () => clips) { CascadeIsRaining = () => { probes++; return rain; } };
        v.Start();
        for (var second = 0; second < 722 && probes == 0; second++)
            clock.Advance(TimeSpan.FromSeconds(1)); // wait for the real scheduler tick
        Assert.True(probes > 0);
        rain = false;
        clock.Advance(TimeSpan.FromMilliseconds(250)); // no library yet, must re-arm
        clips = Clips;
        clock.Advance(TimeSpan.FromSeconds(722));
        Assert.Single(host.Shown);
        v.Stop();
    });

    [Fact]
    public void StartupUsesConfiguredFileOrRandomAndEmptyLibraryStaysSilent() => With(6, true, () =>
    {
        var s = CoreSettings.Current;
        var oldPath = s.StartupVideoPath;
        var oldAttention = s.AttentionChecksEnabled; s.AttentionChecksEnabled = true;
        var file = System.IO.Path.GetTempFileName();
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        try
        {
            s.StartupVideoPath = file;
            Assert.True(v.TriggerStartup());
            clock.Advance(TimeSpan.FromSeconds(1.3));
            Assert.Equal((file, true), Assert.Single(host.Shown));
            v.ForceCleanup();
            System.IO.File.Delete(file);
            Assert.True(v.TriggerStartup());
            clock.Advance(TimeSpan.FromSeconds(1.3));
            Assert.Contains(host.Shown[1].Path, Clips);
            v.ForceCleanup();
            var empty = new MandatoryVideoScheduler(host, clock, () => Array.Empty<string>());
            int warnings = 0;
            empty.NoVideos += () => warnings++;
            Assert.False(empty.TriggerStartup());
            Assert.Equal(0, warnings);
            Assert.False(empty.Trigger()); // a later user action still gets its explanation
            Assert.Equal(1, warnings);
        }
        finally { v.ForceCleanup(); s.StartupVideoPath = oldPath; s.AttentionChecksEnabled = oldAttention; System.IO.File.Delete(file); }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    // No (true, false) row: the attention retry picks its clip before the hold (WPF VideoService.cs:5964),
    // so a held verdict replay never reads the library again; only its probe can throw.
    [InlineData(true, true)]
    public void ThrowingCascadeReplayRecoversScheduleAndReleasesVerdictHold(bool verdictReplay, bool throwInProbe) => With(6, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var rain = !verdictReplay; var fail = false; var probes = 0; var resumed = 0;
        void MaybeThrow(bool probe)
        {
            if (!fail || probe != throwInProbe) return;
            fail = false;
            throw new System.UnauthorizedAccessException("unreadable video folder or failed cascade probe");
        }
        var v = new MandatoryVideoScheduler(host, clock, () => { MaybeThrow(false); return new[] { Clips[0] }; })
        {
            CascadeIsRaining = () => { probes++; MaybeThrow(true); return rain; }
        };
        var attention = CoreSettings.Current.AttentionChecksEnabled;
        var pause = CoreBubbles.PauseAction; var resume = CoreBubbles.ResumeAction;
        CoreSettings.Current.AttentionChecksEnabled = true;
        CoreBubbles.PauseAction = () => { };
        CoreBubbles.ResumeAction = () => resumed++;
        try
        {
            v.Start();
            if (verdictReplay)
            {
                v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
                v.NoteSpawn(); v.Ended(); // failed attention check, with its bubbles still held
                rain = true;
                host.Then!();
            }
            else
            {
                // Consume the actual scheduled tick so an older timer cannot mask a lost re-arm.
                for (var second = 0; second < 722 && probes == 0; second++) clock.Advance(TimeSpan.FromSeconds(1));
                Assert.True(probes > 0);
            }
            Assert.Equal(0, resumed);
            fail = true;
            rain = false;
            Assert.Null(Record.Exception(() => clock.Advance(TimeSpan.FromMilliseconds(250))));
            Assert.False(v.IsPlaying);
            Assert.False(v.IsStrict);
            Assert.Equal(verdictReplay ? 1 : 0, resumed);
            clock.Advance(TimeSpan.FromSeconds(722));
            Assert.Equal(verdictReplay ? 2 : 1, host.Shown.Count);
        }
        finally
        {
            v.Stop();
            CoreSettings.Current.AttentionChecksEnabled = attention;
            CoreBubbles.PauseAction = pause; CoreBubbles.ResumeAction = resume;
        }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StartupFreezeTimingAndCancellation(bool attention, bool cancel) => With(6, false, () =>
    {
        var s = CoreSettings.Current;
        var saved = (s.AttentionChecksEnabled, s.StartupVideoPath);
        s.AttentionChecksEnabled = attention; s.StartupVideoPath = null;
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        try
        {
            Assert.True(v.TriggerStartup());
            Assert.Equal(attention ? 0 : 1, host.Freezes);
            clock.Advance(TimeSpan.FromMilliseconds(cancel ? 400 : (attention ? 0 : 800) + 1299));
            Assert.Empty(host.Shown);
            if (cancel) v.ForceCleanup();
            else { clock.Advance(TimeSpan.FromMilliseconds(1)); Assert.Single(host.Shown); v.End(); }
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(cancel ? 0 : 1, host.Shown.Count);
            Assert.Equal(attention ? Array.Empty<bool>() : new[] { cancel }, host.FreezeEnds);
        }
        finally { v.ForceCleanup(); (s.AttentionChecksEnabled, s.StartupVideoPath) = saved; }
    });

    [Fact]
    public void Rate_follows_videos_per_hour_with_the_pre_roll_and_End_rearms() => With(6, false, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        v.Start();
        clock.Advance(TimeSpan.FromSeconds(479));          // earliest: 600 s * 0.8
        Assert.False(v.IsPlaying);
        var waited = 479;
        while (!v.IsPlaying) { clock.Advance(TimeSpan.FromSeconds(1)); waited++; }
        Assert.InRange(waited, 480, 721);                   // latest: 600 s * 1.2
        Assert.Empty(host.Shown);                           // still in the 1.3 s pre-roll
        clock.Advance(TimeSpan.FromSeconds(1.3));
        Assert.Single(host.Shown);
        Assert.Contains(host.Shown[0].Path, Clips);
        Assert.False(host.Shown[0].Strict);

        clock.Advance(TimeSpan.FromHours(1));               // nothing is scheduled while one plays
        Assert.Single(host.Shown);
        v.End();                                            // natural end / Esc: next one is scheduled
        Assert.Equal(1, host.Closes);
        clock.Advance(TimeSpan.FromSeconds(722));
        Assert.Equal(2, host.Shown.Count);
        Assert.NotEqual(host.Shown[0].Path, host.Shown[1].Path);   // a shuffled queue, not a re-draw

        v.Stop();                                           // engine stop / panic: closes, no more
        Assert.Equal(2, host.Closes);
        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(2, host.Shown.Count);
    });

    [Fact]   // WPF VideoStarted (VideoService.cs:3370): once the clip is on screen, not at the trigger
    public void VideoStarted_fires_when_the_clip_is_shown() => With(20, false, () =>
    {
        var clock = new FakeClock(); var host = new Host(); var started = 0; string? announced = null;
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        v.VideoStarted += () => { started++; announced = v.LastVideoPath; };
        Assert.True(v.Trigger());
        Assert.Equal(0, started);                           // still in the pre-roll
        clock.Advance(TimeSpan.FromSeconds(1.3));
        Assert.Single(host.Shown);
        Assert.Equal(1, started);
        Assert.Equal(host.Shown[0].Path, announced);        // what the media log records (WPF LastVideoPath)
        v.ForceCleanup();
    });

    [Fact]   // WPF TriggerVideo :2526 / Cleanup :7939: a Freeze 800 ms before the pre-roll, its Reset at the end
    public void Freeze_leads_the_video_and_its_reset_waits_for_the_end() => With(20, false, () =>
    {
        var s = CoreSettings.Current;
        var (att, surface) = (s.AttentionChecksEnabled, CoreSubliminal.BambiFreezeProvider);
        CoreSubliminal.BambiFreezeProvider = () => { };   // a head with a Freeze surface
        s.AttentionChecksEnabled = false;
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            Assert.True(v.Trigger());
            Assert.Equal(1, host.Freezes);
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Empty(host.Shown);                        // the Freeze's 800 ms come first
            clock.Advance(MandatoryVideoScheduler.FreezeLead);
            Assert.Single(host.Shown);
            v.End();
            Assert.Equal(new[] { false }, host.FreezeEnds);  // natural end / Esc: the held Reset
            Assert.True(v.Trigger());
            v.ForceCleanup();                                // panic: the Reset is cancelled
            Assert.Equal(2, host.Freezes);
            Assert.Equal(new[] { false, true }, host.FreezeEnds);
            s.AttentionChecksEnabled = true;                 // targets to click: no Freeze, plain pre-roll
            Assert.True(v.Trigger());
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(2, host.Shown.Count);
            Assert.Equal(2, host.Freezes);
            v.ForceCleanup();
            Assert.Equal(2, host.FreezeEnds.Count);
        }
        finally
        {
            s.AttentionChecksEnabled = att;
            CoreSubliminal.BambiFreezeProvider = surface;
        }
    });

    [Fact]
    public void ForceCleanup_does_not_rearm_and_cancels_a_pending_pre_roll() => With(20, true, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        Assert.True(v.Trigger());                           // the Test button: no schedule running
        Assert.True(v.IsStrict);                            // StrictLockEnabled at trigger time
        v.ForceCleanup();
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Empty(host.Shown);                           // the pre-roll died with its run
        Assert.False(v.IsPlaying);

        v.Start();                                          // 20/h: 144-216 s
        clock.Advance(TimeSpan.FromSeconds(217.3));
        Assert.Single(host.Shown);
        v.ForceCleanup();                                   // in-window panic key
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Single(host.Shown);                          // WPF ForceCleanup never re-arms
        v.Stop();
    });

    [Fact]
    public void Empty_library_starts_nothing_and_keeps_the_schedule_alive() => With(60, false, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Array.Empty<string>());
        v.Start();
        Assert.False(v.Trigger());
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty(host.Shown);
        Assert.True(v.IsRunning);
        v.Stop();
    });

    [Fact]
    public void Scrolller_clip_requires_a_cached_file_and_plays_that_file() => With(20, false, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        const string url = "https://cdn.scrolller.com/video.mp4";
        string? cached = null;
        v.CachedScrolllerPath = _ => cached;
        var enabled = ScrolllerPolicy.Enabled; var cap = ScrolllerPolicy.MaxClipShortSide;
        try
        {
            ScrolllerPolicy.Apply(true, null);
            Assert.True(v.Trigger(path: url));
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Contains(Assert.Single(host.Shown).Path, Clips);
            v.End();
            host.Shown.Clear();
            cached = "/cache/video.mp4";
            Assert.True(v.Trigger(path: url));
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(cached, Assert.Single(host.Shown).Path);
            v.End();
        }
        finally { ScrolllerPolicy.Apply(enabled, cap); }
    });

    [Fact]
    public void Scrolller_licence_is_rechecked_after_preroll() => With(20, false, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips)
            { CachedScrolllerPath = _ => "/cache/video.mp4" };
        var enabled = ScrolllerPolicy.Enabled; var cap = ScrolllerPolicy.MaxClipShortSide;
        try
        {
            ScrolllerPolicy.Apply(true, null);
            Assert.True(v.Trigger(path: "https://cdn.scrolller.com/video.mp4"));
            ScrolllerPolicy.Apply(false, null);
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Empty(host.Shown);
            Assert.False(v.IsPlaying);
            Assert.True(v.Trigger(path: "https://cdn.scrolller.com/video.mp4"));
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Contains(Assert.Single(host.Shown).Path, Clips);
            v.End();
        }
        finally { ScrolllerPolicy.Apply(enabled, cap); }
    });

    [Fact]
    public void Uncached_Scrolller_with_empty_library_does_not_start() => With(20, false, () =>
    {
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Array.Empty<string>())
            { CachedScrolllerPath = _ => null };
        Assert.False(v.Trigger(path: "https://cdn.scrolller.com/video.mp4"));
        clock.Advance(MandatoryVideoScheduler.PreRoll);
        Assert.False(v.IsPlaying);
        Assert.Empty(host.Shown);
    });

    [Fact]
    public void Attention_rules_match_WPF()
    {
        var r = new Random(1);
        var t = MandatoryVideoScheduler.AttentionSpawnTimes(8, 30, r);
        Assert.Equal(8, t.Count);
        Assert.True(t[0] >= 3);
        for (var i = 1; i < t.Count; i++) Assert.True(t[i] - t[i - 1] >= 3 - 1e-9, "3 s apart");
        Assert.Equal(3, MandatoryVideoScheduler.AttentionTargetCount(3, false, r));
        Assert.Equal(1, MandatoryVideoScheduler.AttentionTargetCount(0, false, r));
        Assert.InRange(MandatoryVideoScheduler.AttentionTargetCount(5, true, r), 1, 5);
        Assert.Equal(AttentionVerdict.None, MandatoryVideoScheduler.Evaluate(true, 0, 0, 0.5));
        Assert.Equal(AttentionVerdict.None, MandatoryVideoScheduler.Evaluate(false, 3, 0, 0.5));
        Assert.Equal(AttentionVerdict.Fail, MandatoryVideoScheduler.Evaluate(true, 3, 2, 0.5));
        Assert.Equal(AttentionVerdict.Pass, MandatoryVideoScheduler.Evaluate(true, 3, 3, 0.5));
        Assert.Equal(AttentionVerdict.Troll, MandatoryVideoScheduler.Evaluate(true, 3, 3, 0.05));
        Assert.Equal(250, MandatoryVideoScheduler.AttentionPassXp(0));
        Assert.Equal(350, MandatoryVideoScheduler.AttentionPassXp(2));
        Assert.Equal("GOOD\nGIRL", MandatoryVideoScheduler.FormatTriggerText("GOOD GIRL"));
        Assert.Equal("A B\nC D E", MandatoryVideoScheduler.FormatTriggerText("A B C D E"));
        Assert.True(MandatoryVideoScheduler.NeedsBlurFill(4 / 3.0, 16 / 9.0));
        Assert.False(MandatoryVideoScheduler.NeedsBlurFill(16 / 9.0, 16 / 9.0));
    }

    [Fact]
    public void A_missed_check_replays_a_fresh_strict_clip_until_mercy_and_a_catch_pays() => With(60, true, () =>
    {
        var s = CoreSettings.Current;
        var (a, m) = (s.AttentionChecksEnabled, s.MercySystemEnabled);
        (s.AttentionChecksEnabled, s.MercySystemEnabled) = (true, true);
        var xp = new List<double>(); var checks = new List<bool>();
        CoreProgression.AddXPProvider = (x, _) => xp.Add(x);
        CoreProgression.TrackAttentionCheckProvider = checks.Add;
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            var bubbles = new List<string>();   // WPF: bubbles held from the first clip until the run ends
            CoreBubbles.PauseAction = () => bubbles.Add("pause");
            CoreBubbles.ResumeAction = () => bubbles.Add("resume");
            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(new[] { "pause" }, bubbles);
            for (var replay = 1; replay <= 2; replay++)
            {
                v.NoteSpawn(); v.NoteSpawn(); v.NoteHit();   // 1 of 2 caught
                v.Ended();
                Assert.False(v.IsPlaying);
                Assert.Equal(AttentionVerdict.Fail, host.Messages[^1]);
                Assert.Equal(replay, v.Penalties);
                host.Then!();                                // the 2 s message ends: a fresh clip, same strictness
                clock.Advance(MandatoryVideoScheduler.PreRoll);
                Assert.Equal(replay + 1, host.Shown.Count);
                Assert.True(host.Shown[^1].Strict);
                Assert.Equal(0, v.AttentionSpawned);
                Assert.DoesNotContain("resume", bubbles);    // not through the verdict or the replay
            }
            v.NoteSpawn(); v.Ended();                        // third miss: mercy, no replay
            Assert.Equal(AttentionVerdict.Mercy, host.Messages[^1]);
            host.Then!();
            Assert.Equal("resume", bubbles[^1]);
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(3, host.Shown.Count);
            Assert.Equal(0, v.Penalties);
            Assert.Equal(new[] { false, false, false }, checks);

            xp.Clear(); checks.Clear();
            for (var i = 0; i < 50 && host.Messages.Count == 3; i++)   // a catch pays, 1 in 10 is trolled
            {
                v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
                v.NoteSpawn(); v.NoteHit(); v.Ended();
            }
            Assert.Contains(250.0, xp);
            Assert.Contains(15.0, xp);
            Assert.All(checks, Assert.True);

            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);   // a stop during the message cancels the replay
            v.NoteSpawn(); v.Ended();
            var shown = host.Shown.Count;
            v.Stop(); host.Then!();
            clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(shown, host.Shown.Count);
        }
        finally
        {
            (s.AttentionChecksEnabled, s.MercySystemEnabled) = (a, m);
            CoreProgression.AddXPProvider = null;
            CoreProgression.TrackAttentionCheckProvider = null;
            CoreBubbles.PauseAction = CoreBubbles.ResumeAction = null;
        }
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Replay_switch_ends_ordinary_failures_but_preserves_strict_and_enabled_replays(bool strict, bool replay, bool lockdown) => With(60, strict, () =>
    {
        var s = CoreSettings.Current;
        var saved = (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MandatoryVideoReplayAfterFailedCheck);
        var priorTracker = CoreProgression.TrackAttentionCheckProvider;
        var priorLockdown = LockdownService.Current;
        using var hold = new LockdownService();
        var checks = new List<bool>();
        CoreProgression.TrackAttentionCheckProvider = checks.Add;
        (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MandatoryVideoReplayAfterFailedCheck) = (true, false, replay);
        var clock = new FakeClock(); var host = new Host();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        try
        {
            LockdownService.Current = hold;
            if (lockdown) hold.Activate(TimeSpan.FromMinutes(10));
            v.Trigger(strictOverride: strict); clock.Advance(MandatoryVideoScheduler.PreRoll);
            v.NoteSpawn(); v.Ended();
            Assert.Equal(new[] { false }, checks); // the failure still reaches achievements/companion
            Assert.False(v.IsPlaying);
            Assert.Equal(1, host.Closes);
            if (strict || replay || lockdown)
            {
                Assert.Equal(new[] { AttentionVerdict.Fail }, host.Messages);
                host.Then!(); clock.Advance(MandatoryVideoScheduler.PreRoll);
                Assert.Equal(2, host.Shown.Count);
                Assert.Equal(strict, host.Shown[1].Strict);
            }
            else
            {
                Assert.Empty(host.Messages);
                Assert.Equal(0, v.Penalties);
                clock.Advance(TimeSpan.FromHours(1));
                Assert.Single(host.Shown);
            }
        }
        finally
        {
            (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MandatoryVideoReplayAfterFailedCheck) = saved;
            v.Stop();
            hold.Deactivate();
            LockdownService.Current = priorLockdown;
            CoreProgression.TrackAttentionCheckProvider = priorTracker;
        }
    });

    [Theory]   // review P2: a verdict replay held behind GIF rain must still run AfterEnd when dropped
    [InlineData(false)]
    [InlineData(true)]
    public void A_verdict_replay_dropped_behind_the_cascade_resumes_bubbles(bool forceCleanup) => With(60, true, () =>
    {
        var s = CoreSettings.Current;
        var a = s.AttentionChecksEnabled;
        s.AttentionChecksEnabled = true;
        var bubbles = new List<string>();
        CoreBubbles.PauseAction = () => bubbles.Add("pause");
        CoreBubbles.ResumeAction = () => bubbles.Add("resume");
        try
        {
            var clock = new FakeClock(); var host = new Host(); var rain = false;
            var v = new MandatoryVideoScheduler(host, clock, () => Clips) { CascadeIsRaining = () => rain };
            v.Start();
            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            v.NoteSpawn(); v.Ended();                        // missed: verdict message up
            rain = true;
            host.Then!();                                    // the replay is held behind the rain
            Assert.DoesNotContain("resume", bubbles);
            if (forceCleanup) v.ForceCleanup();
            else clock.Advance(TimeSpan.FromSeconds(91));   // the 90 s hold expires
            Assert.Equal("resume", bubbles[^1]);
            Assert.Single(host.Shown);
            v.Stop();
        }
        finally
        {
            s.AttentionChecksEnabled = a;
            CoreBubbles.PauseAction = CoreBubbles.ResumeAction = null;
        }
    });

    /// <summary>#1145: the Mercy picker moves the threshold (here 2 misses).</summary>
    [Fact]
    public void Mercy_comes_after_the_picked_number_of_misses() => With(60, true, () =>
    {
        var s = CoreSettings.Current;
        var (a, m, n) = (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails);
        (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails) = (true, true, 2);
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            v.NoteSpawn(); v.Ended();
            Assert.Equal(AttentionVerdict.Fail, host.Messages[^1]);
            host.Then!(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            v.NoteSpawn(); v.Ended();                        // second miss: mercy
            Assert.Equal(AttentionVerdict.Mercy, host.Messages[^1]);
        }
        finally
        {
            (s.AttentionChecksEnabled, s.MercySystemEnabled, s.MercyAfterFails) = (a, m, n);
            CoreBubbles.PauseAction = CoreBubbles.ResumeAction = null;
        }
    });

    [Fact]
    public void A_new_clip_during_the_verdict_message_cancels_the_pending_replay() => With(60, false, () =>
    {
        var s = CoreSettings.Current;
        var (a, m) = (s.AttentionChecksEnabled, s.MercySystemEnabled);
        (s.AttentionChecksEnabled, s.MercySystemEnabled) = (true, true);
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.Trigger(); clock.Advance(MandatoryVideoScheduler.PreRoll);
            v.NoteSpawn(); v.Ended();                        // missed: the 2 s verdict message is up
            Assert.True(v.Trigger());                        // the Test button in that gap
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(2, host.Shown.Count);
            host.Then!();                                    // the message ends: its replay is stale
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal(2, host.Shown.Count);
            Assert.True(v.IsPlaying);
            Assert.Equal(1, v.Penalties);                    // a stale replay would have run AfterEnd (reset to 0)
        }
        finally { (s.AttentionChecksEnabled, s.MercySystemEnabled) = (a, m); }
    });

    [Fact]
    public void No_videos_is_raised_once_per_launch()
    {
        var v = new MandatoryVideoScheduler(new Host(), new FakeClock(), () => Array.Empty<string>());
        var n = 0;
        v.NoVideos += () => n++;
        Assert.False(v.Trigger(silentIfEmpty: true)); Assert.False(v.Trigger(silentIfEmpty: true));
        Assert.Equal(0, n);
        v.Trigger(); v.Trigger();
        Assert.Equal(1, n);
    }

    [Fact]
    public void Length_filter_keeps_long_slices_and_counts_what_it_emptied()
    {
        // WPF RefillVideoQueues' duration filter + #1352 funnel counts; a clip with no cached length is kept.
        var s = CoreSettings.Current;
        var (min, max) = (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds);
        var lengths = new Dictionary<string, double> { ["/v/short.mp4"] = 20, ["/v/mid.mp4"] = 90, ["/v/long.mp4"] = 400 };
        var files = new[] { "/v/short.mp4", "/v/mid.mp4", "/v/long.mp4", "/v/unparsed.mp4" };
        try
        {
            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (52, 170);
            var host = new Host();
            var v = new MandatoryVideoScheduler(host, new FakeClock(), () => files)
                { DurationOf = p => lengths.TryGetValue(p, out var d) ? d : null };
            var picks = new HashSet<string> { v.PickNext()!, v.PickNext()!, v.PickNext()! };
            Assert.Equal(new HashSet<string> { "/v/mid.mp4", "/v/long.mp4", "/v/unparsed.mp4" }, picks);
            Assert.Equal((4, 3), (v.LastFunnelEnabled, v.LastFunnelDuration));

            // The filter keeps none of the enabled clips: no pick, and the dialog can blame the filter.
            lengths["/v/unparsed.mp4"] = 30;
            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (500, 0);
            var w = new MandatoryVideoScheduler(host, new FakeClock(), () => files) { DurationOf = v.DurationOf };
            var raised = 0;
            w.NoVideos += () => raised++;
            Assert.False(w.Trigger());
            Assert.Equal(1, raised);
            Assert.True(NoVideosReason.LengthFilterEmptied(w.LastFunnelEnabled, w.LastFunnelDuration));
            Assert.Equal("8m 20s - ∞", NoVideosReason.FormatRange(s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds));

            (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (0, 0);   // unset keeps everything
            Assert.Equal(4, MandatoryVideoScheduler.KeepByLength(files, 0, 0, v.DurationOf).Count);
        }
        finally { (s.VideoMinDurationSeconds, s.VideoMaxDurationSeconds) = (min, max); }
    }

    [Fact]
    public void Watch_credit_goes_to_progression_from_one_second()
    {
        var credited = new List<double>();
        CoreProgression.TrackVideoWatchedProvider = credited.Add;
        try
        {
            var host = new Host { Watched = 42.5 };
            var v = new MandatoryVideoScheduler(host, new FakeClock(), () => Clips);
            v.Trigger(); v.End();
            host.Watched = 0.5;
            v.Trigger(); v.ForceCleanup();
            Assert.Equal(new[] { 42.5 }, credited);
        }
        finally { CoreProgression.TrackVideoWatchedProvider = null; }
    }

    [Theory]
    [InlineData(true, "F12", false, VideoKeyAction.Swallow)]     // the panic key
    [InlineData(true, "F4", true, VideoKeyAction.Swallow)]       // Alt+F4
    [InlineData(true, "System", false, VideoKeyAction.Swallow)]
    [InlineData(true, "Escape", false, VideoKeyAction.None)]
    [InlineData(false, "Escape", false, VideoKeyAction.Dismiss)]
    [InlineData(false, "F12", false, VideoKeyAction.ForceStop)]
    [InlineData(false, "A", false, VideoKeyAction.None)]
    public void Strict_keys(bool strict, string key, bool alt, VideoKeyAction expected) =>
        Assert.Equal(expected, MandatoryVideoScheduler.KeyAction(strict, key, alt, panicEnabled: true, panicKey: "F12"));

    // #875: no live global panic listener -> strict must not trap; panic key and Esc force-stop.
    [Theory]
    [InlineData("F12", VideoKeyAction.ForceStop)]
    [InlineData("Escape", VideoKeyAction.ForceStop)]
    [InlineData("F4", VideoKeyAction.None)]
    public void Strict_without_a_live_panic_listener_falls_open(string key, VideoKeyAction expected) =>
        Assert.Equal(expected, MandatoryVideoScheduler.KeyAction(true, key, false, panicEnabled: true, panicKey: "F12", panicListenerLive: false));

    [Fact]
    public void Pure_rules_match_WPF()
    {
        Assert.Equal(60, MandatoryVideoScheduler.NextIntervalSeconds(100, 0));   // floored at 60 s
        Assert.Equal(3600 * 1.2, MandatoryVideoScheduler.NextIntervalSeconds(0, 1), 6);   // perHour < 1 is 1
        Assert.False(MandatoryVideoScheduler.ShouldFillSecondaryMonitors(1, true));
        Assert.True(MandatoryVideoScheduler.ShouldFillSecondaryMonitors(2, false));
        Assert.False(MandatoryVideoScheduler.ShouldFillSecondaryMonitors(3, false));
        Assert.Equal(25, MandatoryVideoScheduler.EffectiveVolume(50, 50));
        Assert.True(MandatoryVideoScheduler.IsSupportedVideoExtension("x/Clip.MP4"));
        Assert.False(MandatoryVideoScheduler.IsSupportedVideoExtension("x/clip.gif"));
    }

    [Fact]
    public void Engine_starts_and_stops_it_by_the_saved_flag() => With(6, false, () =>
    {
        var s = CoreSettings.Current;
        var host = new Host();
        CoreEngine.Video = new MandatoryVideoScheduler(host, new FakeClock(), () => Clips);
        try
        {
            CoreEngine.Start();
            Assert.True(CoreEngine.Video.IsRunning);
            CoreEngine.Video.Trigger();
            s.MandatoryVideosEnabled = false;
            CoreEngine.Reconcile();                         // WPF ReconcileRunningServices
            Assert.False(CoreEngine.Video.IsRunning);
            Assert.Equal(1, host.Closes);
            s.MandatoryVideosEnabled = true;
            CoreEngine.ApplyLive("video", true);
            Assert.True(CoreEngine.Video.IsRunning);
            CoreEngine.Stop();
            Assert.False(CoreEngine.Video.IsRunning);
        }
        finally { CoreEngine.Stop(); CoreEngine.Video = null; }
    });

    // WPF #735: one grace pause per clip, only while it plays; the same keystroke's second handler
    // is swallowed for 200 ms; the countdown rounds up.
    [Fact]
    public void GracePauseRules()
    {
        Assert.Equal(GraceDecision.Pause, MandatoryVideoScheduler.EvaluateGrace(true, false, false, false, double.MaxValue));
        Assert.Equal(GraceDecision.ConsumedDedup, MandatoryVideoScheduler.EvaluateGrace(true, false, true, true, 150));
        Assert.Equal(GraceDecision.FallThrough, MandatoryVideoScheduler.EvaluateGrace(true, false, true, false, 5000));   // press 2
        Assert.Equal(GraceDecision.FallThrough, MandatoryVideoScheduler.EvaluateGrace(true, false, false, true, 5000));   // spent
        Assert.Equal(GraceDecision.FallThrough, MandatoryVideoScheduler.EvaluateGrace(false, false, false, false, double.MaxValue));
        Assert.Equal(60, MandatoryVideoScheduler.GraceSecondsRemaining(0));
        Assert.Equal(59, MandatoryVideoScheduler.GraceSecondsRemaining(1.2));
        Assert.Equal(0, MandatoryVideoScheduler.GraceSecondsRemaining(61));

        // Which keys try it (WPF SetupStrictHandlers). Strict: Esc only, with a non-Esc panic key and
        // a live listener - the fall-open force-stop keeps Esc when nothing global can stop the clip.
        Assert.Equal(false, MandatoryVideoScheduler.GraceKey(true, "Escape", true, "F12"));
        Assert.Null(MandatoryVideoScheduler.GraceKey(true, "Escape", true, "F12", panicListenerLive: false));
        Assert.Null(MandatoryVideoScheduler.GraceKey(true, "Escape", true, "Escape"));
        Assert.Null(MandatoryVideoScheduler.GraceKey(true, "F12", true, "F12"));
        Assert.Equal(false, MandatoryVideoScheduler.GraceKey(false, "Escape", true, "F12"));
        Assert.Equal(true, MandatoryVideoScheduler.GraceKey(false, "Escape", true, "Escape"));
        Assert.Equal(true, MandatoryVideoScheduler.GraceKey(false, "F12", true, "F12"));
        Assert.Null(MandatoryVideoScheduler.GraceKey(false, "F12", false, "F12"));
    }

    // WPF vout watchdog (8 s), mid-play vout loss (5 s) - both replay once - and the safety timer
    // (+5 s), 600 s fallback and max-length cap on the clock from the first frame.
    [Fact]
    public void ClipGuards()
    {
        string? G(double show, double played, bool framed, double len, double sinceFrame, int max, bool video = true)
            => MandatoryVideoScheduler.Guard(show, played, framed, video, len, sinceFrame, max);
        Assert.Null(G(7.9, 0, false, 0, 0, 0));
        Assert.Equal("no frame", G(8, 0, false, 0, 0, 0));
        Assert.Null(G(30, 30, false, 0, 0, 0, video: false));            // audio-only plays out
        Assert.Null(G(40, 34.9, true, 30, 0, 0));                        // counts from the first frame
        Assert.Equal("overran", G(40, 35, true, 30, 0, 0));
        Assert.Null(G(700, 599, true, 0, 0, 0));
        Assert.Equal("overran", G(600, 600, true, 0, 0, 0));
        Assert.Equal("max length", G(25, 20, true, 30, 0, 20));
        Assert.Null(G(25, 19.9, true, 30, 0, 20));
        Assert.Equal("output lost", G(10, 10, true, 30, 5, 0));
        Assert.Null(G(10, 10, true, 30, 4.9, 0));
        Assert.True(MandatoryVideoScheduler.GuardHeals("no frame", false));
        Assert.True(MandatoryVideoScheduler.GuardHeals("output lost", false));
        Assert.False(MandatoryVideoScheduler.GuardHeals("output lost", true));
        Assert.False(MandatoryVideoScheduler.GuardHeals("max length", false));
    }

    // WPF CompanionService.OnAttentionCheckFailed: only the Trainer's perk costs 25 XP, floored at 0.
    [Fact]
    public void TrainerPerkPaysForAFailedCheck()
    {
        var s = new ConditioningControlPanel.Models.AppSettings();
        var trainer = ConditioningControlPanel.Models.CompanionDefinition.AllCompanions
            .First(c => c.BonusType == ConditioningControlPanel.Models.CompanionBonusType.StrictModeBonus);
        s.ActiveCompanionId = (int)trainer.Id;
        s.ActiveCompanionProgress.CurrentXP = 30;
        Assert.True(ConditioningControlPanel.Services.Companion.CompanionPerks.ApplyAttentionFailPenalty(s));
        Assert.Equal(5, s.ActiveCompanionProgress.CurrentXP);
        Assert.True(ConditioningControlPanel.Services.Companion.CompanionPerks.ApplyAttentionFailPenalty(s));
        Assert.Equal(0, s.ActiveCompanionProgress.CurrentXP);
        var other = ConditioningControlPanel.Models.CompanionDefinition.AllCompanions
            .First(c => c.BonusType != ConditioningControlPanel.Models.CompanionBonusType.StrictModeBonus);
        s.ActiveCompanionId = (int)other.Id;
        s.ActiveCompanionProgress.CurrentXP = 30;
        Assert.False(ConditioningControlPanel.Services.Companion.CompanionPerks.ApplyAttentionFailPenalty(s));
        Assert.Equal(30, s.ActiveCompanionProgress.CurrentXP);
    }

    /// <summary>WPF VideoService.cs:5729 through CoreHaptics: a caught target pulses the toy.</summary>
    [Fact]
    public void Haptics_follow_the_target_hit_and_the_clip() => With(6, false, () =>
    {
        var haptics = new HapticService(new ConditioningControlPanel.Models.HapticSettings());
        var msgs = new List<string>();
        haptics.HapticTriggered += (_, m) => msgs.Add(m);
        var old = CoreHaptics.Service;
        CoreHaptics.Service = haptics;
        try
        {
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.NoteSpawn(); v.NoteHit();
            Assert.Contains(msgs, m => m.StartsWith("Target Hit"));
        }
        finally { CoreHaptics.Service = old; haptics.Dispose(); }
    });

    /// <summary>The AI's named clip (WPF PlaySpecificVideo(path, strict: false)) plays that clip, not a pick,
    /// and not strict even when Strict Lock is on.</summary>
    [Fact]
    public void TriggerWithAPathPlaysThatClipUnstrict() => With(6, strict: true, () =>
    {
        var host = new Host();
        var clock = new FakeClock();
        var v = new MandatoryVideoScheduler(host, clock, () => Clips);
        Assert.True(v.Trigger(strictOverride: false, path: "/ai/named.mp4"));
        clock.Advance(TimeSpan.FromSeconds(1.3));
        Assert.Equal(new[] { ("/ai/named.mp4", false) }, host.Shown);
        Assert.False(v.Trigger(path: "/ai/other.mp4"));      // one already playing
    });

    [Fact]   // WPF VideoService :3007: a do-not-disturb app in front reschedules (SkipRetrySeconds), never drops
    public void Dnd_app_in_front_reschedules_the_scheduled_clip() => With(60, false, () =>
    {
        var s = CoreSettings.Current;
        var (list, on, provider) = (s.DndProcessList, s.DndSuppressVideos, ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess);
        var front = "mpv";
        try
        {
            s.DndProcessList = new List<string> { "mpv" };
            s.DndSuppressVideos = true;
            ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess = () => front;
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => Clips);
            v.Start();
            clock.Advance(TimeSpan.FromHours(2));               // many ticks, every one skipped
            Assert.False(v.IsPlaying);
            front = "firefox";                                  // the user alt-tabs away
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
            clock.Advance(TimeSpan.FromSeconds(MandatoryVideoScheduler.SkipRetrySeconds + 5));
            Assert.Single(host.Shown);
            v.Stop();
        }
        finally
        {
            (s.DndProcessList, s.DndSuppressVideos) = (list, on);
            ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess = provider;
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
        }
    });
}
