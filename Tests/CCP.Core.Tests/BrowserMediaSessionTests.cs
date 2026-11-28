using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Browser;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF Services/Browser/BrowserMediaService.cs: owners, gates, heartbeat, load window, hard stop.</summary>
[Collection(SessionStatics.Name)]
public sealed class BrowserMediaSessionTests
{
    private static void With(bool protect, int grace, int maxSeconds, Action body)
    {
        var s = CoreSettings.Current;
        var (p, g, m) = (s.ProtectBrowserVideoPlayback, s.BrowserVideoGraceSeconds, s.VideoMaxDurationSeconds);
        (s.ProtectBrowserVideoPlayback, s.BrowserVideoGraceSeconds, s.VideoMaxDurationSeconds) = (protect, grace, maxSeconds);
        try { body(); }
        finally { (s.ProtectBrowserVideoPlayback, s.BrowserVideoGraceSeconds, s.VideoMaxDurationSeconds) = (p, g, m); }
    }

    [Fact]
    public void UserPlaybackOpensASessionThatDefersThenCoolsOff() => With(true, 30, 0, () =>
    {
        var clock = new FakeTimeProvider();
        var m = new BrowserMediaSession(clock);
        Assert.False(m.HoldsScreen);
        m.OnMediaPlaying(120);
        Assert.Equal(BrowserMediaSession.MediaOwner.User, m.Owner);
        Assert.False(m.IsTakeover);
        Assert.True(m.HoldsScreen);
        Assert.True(m.ShouldDeferInterruptions);
        m.OnMediaStopped("paused");
        Assert.False(m.HoldsScreen);
        Assert.False(m.ShouldDeferInterruptions);
        Assert.True(m.ShouldDeferNewVideo);              // cool-off
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.False(m.ShouldDeferNewVideo);
    });

    [Fact]
    public void ProtectOffNeverDefersButTheSessionStillHoldsTheScreen() => With(false, 30, 0, () =>
    {
        var m = new BrowserMediaSession(new FakeTimeProvider());
        m.OnMediaPlaying(0);
        Assert.False(m.ShouldDeferInterruptions);
        Assert.True(m.HoldsScreen);   // WPF: the WebVideo slot is claimed regardless of the setting
        m.OnMediaStopped("ended");
    });

    [Fact]
    public void HeartbeatAndLoadWindowEndSilentSessions() => With(true, 0, 0, () =>
    {
        var clock = new FakeTimeProvider();
        var m = new BrowserMediaSession(clock);
        m.ReplaceSession(BrowserMediaSession.MediaOwner.Remote, takeover: true);
        m.OnMediaStopped("navigated");                   // outgoing page during the load window
        Assert.True(m.HoldsScreen);
        clock.Advance(TimeSpan.FromSeconds(55));
        Assert.True(m.HoldsScreen);                      // 60 s load window
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(m.HoldsScreen);
        Assert.False(m.TickArmed);

        m.OnMediaPlaying(0);
        clock.Advance(TimeSpan.FromSeconds(15));
        m.OnMediaProgress(15, 0, 15);
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.True(m.IsPlaying);                        // the heartbeat kept it alive
        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(m.IsPlaying);                       // 20 s silent
    });

    [Fact]
    public void TakeoverHardStopAndLengthCapForceEndTheTakeoverOnly() => With(true, 0, 30, () =>
    {
        var clock = new FakeTimeProvider();
        var m = new BrowserMediaSession(clock);
        var teardowns = 0;
        m.TearDownTakeover = () => teardowns++;
        m.ReplaceSession(BrowserMediaSession.MediaOwner.Remote, takeover: true);
        m.OnMediaPlaying(10);
        for (var i = 0; i < 26; i++) { clock.Advance(TimeSpan.FromSeconds(1)); m.OnMediaProgress(i, 10, 1); }
        Assert.False(m.HoldsScreen);                     // 10 s clip + 15 s grace
        Assert.Equal(1, teardowns);

        m.ReplaceSession(BrowserMediaSession.MediaOwner.Remote, takeover: true);
        m.OnMediaPlaying(0);
        m.OnMediaProgress(30, 0, 30);                    // the player's 30 s limit
        Assert.False(m.HoldsScreen);
        Assert.Equal(2, teardowns);

        m.ReplaceSession(BrowserMediaSession.MediaOwner.Remote, takeover: true);
        m.OnMediaPlaying(10);
        m.OnFullscreenExited();                          // the user keeps watching: no cut, no hard stop
        Assert.Equal(BrowserMediaSession.MediaOwner.User, m.Owner);
        for (var i = 0; i < 40; i++) { clock.Advance(TimeSpan.FromSeconds(1)); m.OnMediaProgress(i, 10, 40); }
        Assert.True(m.IsPlaying);
        Assert.Equal(2, teardowns);
        m.ForceEnd("panic");
        Assert.False(m.HoldsScreen);
        Assert.False(m.TickArmed);
        Assert.Equal(3, teardowns);
    });
}
