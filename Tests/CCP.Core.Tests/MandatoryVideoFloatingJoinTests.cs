using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class MandatoryVideoFloatingJoinTests
{
    private sealed class Host : IMandatoryVideoHost
    {
        public bool Floating = true;
        public bool FloatingRunOpen { get; set; } = true;
        public int FloatingLimit { get; set; } = 3;
        public int FloatingUp { get; set; } = 1;
        public bool FeedOwnsTheScreen { get; set; }
        public bool BrowserMediaBlocksScheduledVideo { get; set; }
        public int Shown;
        public List<bool> Calls = new();
        public bool AddOnTrigger;
        public bool IsFloating(bool strict) => Floating && !strict;
        public bool TryFloatingTrigger(bool userInitiated) { Calls.Add(userInitiated); if (AddOnTrigger) FloatingUp++; return true; }
        public void Show(string path, bool strict) { Shown++; }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    private static void With(Action<FakeClock, Host, MandatoryVideoScheduler> run, bool together = false, bool strict = false)
    {
        var s = CoreSettings.Current;
        var old = (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.MandatoryVideoFloatingTogether, s.FlashEnabled);
        (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.MandatoryVideoFloatingTogether, s.FlashEnabled) = (true, 20, strict, together, false);
        var clock = new FakeClock(); var host = new Host();
        using var video = new MandatoryVideoScheduler(host, clock, () => new[] { "clip.mp4" });
        try { run(clock, host, video); }
        finally { (s.MandatoryVideosEnabled, s.VideosPerHour, s.StrictLockEnabled, s.MandatoryVideoFloatingTogether, s.FlashEnabled) = old; }
    }

    private static void Open(FakeClock clock, MandatoryVideoScheduler video)
    {
        video.Start();
        Assert.True(video.Trigger());
        clock.Advance(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Open_run_joins_on_short_delay_and_rearms() => With((clock, host, video) =>
    {
        Open(clock, video);
        clock.Advance(TimeSpan.FromSeconds(32));
        Assert.Empty(host.Calls);
        clock.Advance(TimeSpan.FromSeconds(24));
        Assert.Equal(new[] { false }, host.Calls);
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.Equal(new[] { false, false }, host.Calls);
        video.End();
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(2, host.Calls.Count);
    });

    [Theory]
    [InlineData(3, 3, false, false, true)]
    [InlineData(3, 1, true, false, true)]
    [InlineData(1, 1, false, false, true)]
    [InlineData(3, 1, false, true, true)]
    [InlineData(3, 1, false, false, false)]
    public void Ineligible_run_does_not_join(int limit, int up, bool together, bool strict, bool floating) => With((clock, host, video) =>
    {
        host.FloatingLimit = limit; host.FloatingUp = up; host.Floating = floating;
        Open(clock, video);
        clock.Advance(TimeSpan.FromSeconds(220));
        Assert.Empty(host.Calls);
    }, together, strict);

    [Fact]
    public void Full_run_keeps_short_interval_after_a_window_closes() => With((clock, host, video) =>
    {
        host.FloatingUp = host.FloatingLimit - 1;
        host.AddOnTrigger = true;
        Open(clock, video);
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.Single(host.Calls);
        Assert.Equal(host.FloatingLimit, host.FloatingUp);
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.Single(host.Calls);
        host.FloatingUp--;
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.True(host.Calls.Count >= 2);
        Assert.All(host.Calls, userInitiated => Assert.False(userInitiated));
    });

    [Fact]
    public void Run_opening_full_also_uses_short_interval() => With((clock, host, video) =>
    {
        host.FloatingUp = host.FloatingLimit;
        Open(clock, video);
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.Empty(host.Calls);
        host.FloatingUp--;
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.NotEmpty(host.Calls);
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Scheduled_join_waits_for_cascade_and_feed(bool rain, bool feed) => With((clock, host, video) =>
    {
        var raining = false;
        video.CascadeIsRaining = () => raining;
        Open(clock, video);
        raining = rain;
        host.FeedOwnsTheScreen = feed;
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.Empty(host.Calls);
        raining = false;
        clock.Advance(TimeSpan.FromSeconds(1));
        if (feed) Assert.Empty(host.Calls);
        host.FeedOwnsTheScreen = false;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new[] { false }, host.Calls);
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void End_or_panic_cancels_a_held_join(bool cascade, bool panic) => With((clock, host, video) =>
    {
        var raining = false;
        video.CascadeIsRaining = () => raining;
        Open(clock, video);
        raining = cascade;
        host.FeedOwnsTheScreen = !cascade;
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.Empty(host.Calls);
        host.FloatingRunOpen = false; // The last host window closed before End.
        if (panic) video.ForceCleanup(); else video.End();
        raining = false;
        host.FeedOwnsTheScreen = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(host.Calls);
        Assert.False(video.IsPlaying);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Queued_scheduled_join_observes_feed_and_ends_with_its_run(bool endBeforeReplay) => With((clock, host, video) =>
    {
        Open(clock, video);
        Action? queued = null;
        video.DeferTrigger = action => { queued = action; return true; };
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.NotNull(queued);
        Assert.Empty(host.Calls);
        video.DeferTrigger = null;
        host.FeedOwnsTheScreen = true;
        if (endBeforeReplay) { host.FloatingRunOpen = false; video.End(); }
        queued!();
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(host.Calls);
        host.FeedOwnsTheScreen = false;
        clock.Advance(TimeSpan.FromSeconds(2));
        if (endBeforeReplay) Assert.False(video.IsPlaying);
        else Assert.Equal(new[] { false }, host.Calls);
    });

    [Fact]
    public void Queued_join_keeps_scheduled_eligibility_through_cascade() => With((clock, host, video) =>
    {
        var raining = false;
        video.CascadeIsRaining = () => raining;
        Open(clock, video);
        Action? queued = null;
        video.DeferTrigger = action => { queued = action; return true; };
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.NotNull(queued);
        video.DeferTrigger = null;
        raining = true;
        queued!();
        CoreSettings.Current.MandatoryVideosEnabled = false;
        raining = false;
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Empty(host.Calls);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void User_request_held_during_run_is_not_cancelled_by_end(bool cascade) => With((clock, host, video) =>
    {
        var raining = false;
        video.CascadeIsRaining = () => raining;
        Open(clock, video);
        raining = cascade;
        host.FeedOwnsTheScreen = !cascade;
        Assert.True(video.Trigger(userInitiated: true));
        host.FloatingRunOpen = false;
        video.End();
        raining = false;
        host.FeedOwnsTheScreen = false;
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(video.IsPlaying);
        Assert.Equal(2, host.Shown);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Browser_media_retries_scheduled_video_after_thirty_seconds(bool floating) => With((clock, host, video) =>
    {
        host.Floating = floating;
        if (floating) Open(clock, video); else video.Start();
        host.BrowserMediaBlocksScheduledVideo = true;
        clock.Advance(TimeSpan.FromSeconds(230));
        Assert.Empty(host.Calls);
        if (!floating) Assert.False(video.IsPlaying);
        host.BrowserMediaBlocksScheduledVideo = false;
        clock.Advance(TimeSpan.FromSeconds(31));
        if (floating) Assert.Equal(new[] { false }, host.Calls);
        else Assert.True(video.IsPlaying);
    });

    [Fact]
    public void Browser_media_does_not_block_an_explicit_floating_trigger() => With((clock, host, video) =>
    {
        Open(clock, video);
        host.BrowserMediaBlocksScheduledVideo = true;
        Assert.True(video.Trigger(userInitiated: true));
        Assert.Equal(new[] { true }, host.Calls);
    });

    [Fact]
    public void Do_not_disturb_retries_a_join_after_the_short_delay() => With((clock, host, video) =>
    {
        var s = CoreSettings.Current;
        var (list, enabled, provider) = (s.DndProcessList, s.DndSuppressVideos, ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess);
        var front = "player";
        try
        {
            s.DndProcessList = new List<string> { "player" };
            s.DndSuppressVideos = true;
            ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess = () => front;
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
            Open(clock, video);
            clock.Advance(TimeSpan.FromSeconds(55));
            Assert.Empty(host.Calls);
            front = "";
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
            clock.Advance(TimeSpan.FromSeconds(31));
            Assert.Equal(new[] { false }, host.Calls);
        }
        finally
        {
            (s.DndProcessList, s.DndSuppressVideos) = (list, enabled);
            ConditioningControlPanel.Services.UI.DndGuard.ForegroundProcess = provider;
            ConditioningControlPanel.Services.UI.DndGuard.ResetCacheForTests();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Panic_and_stop_cancel_join(bool stop) => With((clock, host, video) =>
    {
        Open(clock, video);
        if (stop) video.Stop(); else video.ForceCleanup();
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Empty(host.Calls);
    });
}
