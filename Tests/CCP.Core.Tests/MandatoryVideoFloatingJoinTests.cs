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
        public List<bool> Calls = new();
        public bool IsFloating(bool strict) => Floating && !strict;
        public bool TryFloatingTrigger(bool userInitiated) { Calls.Add(userInitiated); return true; }
        public void Show(string path, bool strict) { }
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
