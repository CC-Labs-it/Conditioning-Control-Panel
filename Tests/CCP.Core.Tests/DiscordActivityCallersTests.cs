using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class DiscordActivityCallersTests : IDisposable
{
    private readonly Func<DiscordRichPresenceService?>? _oldRpc = CoreDiscord.ServiceProvider;
    private readonly Func<SettingsService?>? _oldSettings = CoreSettings.ServiceProvider;
    private readonly Action<Action>? _oldDispatch = CoreDispatch.PostProvider;
    private readonly Func<bool>? _oldSession = CoreSession.IsSessionRunningProvider;
    private readonly DiscordRichPresenceService _rpc = new();
    private SessionRunner? _session;

    public DiscordActivityCallersTests()
    {
        CoreSettings.ServiceProvider = () => newSettings;
        CoreDiscord.ServiceProvider = () => _rpc;
        CoreDispatch.PostProvider = a => a();
        CoreSettings.Current.DiscordRichPresenceEnabled = false;
    }
    private readonly SettingsService newSettings = new();

    public void Dispose()
    {
        _session?.Stop(); CoreEngine.Stop(); _rpc.Dispose();
        CoreDiscord.ServiceProvider = _oldRpc;
        CoreSettings.ServiceProvider = _oldSettings;
        CoreDispatch.PostProvider = _oldDispatch;
        CoreSession.IsSessionRunningProvider = _oldSession;
    }

    [Fact]
    public void FlashStartAndStopPublishWpfsActivitiesWithoutArmingPresence()
    {
        CoreFlash.Start();
        Assert.Equal("Conditioning", _rpc.CurrentState);
        Assert.Equal("Flash training", _rpc.CurrentDetails);
        Assert.False(_rpc.IsEnabled || _rpc.IsConnected);
        CoreFlash.Stop();
        Assert.Equal("Browsing", _rpc.CurrentState);
        _rpc.SetVideoActivity(); CoreFlash.Stop(); // WPF Stop resets even an idle schedule
        Assert.Equal("Browsing", _rpc.CurrentState);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionsPublishOwnNameThenIdleOnAbandonOrCompletion(bool completed)
    {
        PhrasePoolCustody.Seed();
        _session = new SessionRunner(new SessionLogService());
        _session.Start(new Session { Name = "My chosen session", DurationMinutes = 1, Settings = new SessionSettings { FlashEnabled = true } });
        Assert.Equal("In Session", _rpc.CurrentState); // wins over feature starts
        Assert.Equal("My chosen session", _rpc.CurrentDetails);
        if (completed) _session.Tick(TimeSpan.FromMinutes(1)); else _session.Stop();
        Assert.Equal("Browsing", _rpc.CurrentState);
        Assert.False(_rpc.IsEnabled || _rpc.IsConnected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VideoPublishesAtPlaybackAndRestoresFlashOrIdle(bool flash)
    {
        var clock = new FakeClock(); var host = new Host();
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/fake.mp4" });
        CoreSettings.Current.FlashEnabled = flash;
        CoreSettings.Current.MandatoryVideosEnabled = true;
        video.Start();
        try
        {
            _rpc.SetActivity("Before playback");
            Assert.True(video.Trigger());
            Assert.NotEqual("Watching", _rpc.CurrentState); // preroll isn't watching
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal("Watching", _rpc.CurrentState);
            Assert.Equal("Mandatory viewing", _rpc.CurrentDetails);
            Assert.Equal(1, host.Shows);
            video.End();
            Assert.Equal(flash ? "Conditioning" : "Browsing", _rpc.CurrentState);
            Assert.True(video.Trigger()); clock.Advance(MandatoryVideoScheduler.PreRoll);
            video.ForceCleanup();
            Assert.Equal("Browsing", _rpc.CurrentState); // standalone fall-open panic
            Assert.False(_rpc.IsEnabled || _rpc.IsConnected);
        }
        finally { video.Stop(); }
    }

    [Fact]
    public void AttentionRetryKeepsWatchingLikeWpfCloseAll()
    {
        var clock = new FakeClock(); var host = new Host { HoldMessage = true };
        var video = new MandatoryVideoScheduler(host, clock, () => new[] { "/fake.mp4" });
        CoreSettings.Current.FlashEnabled = false;
        CoreSettings.Current.MandatoryVideosEnabled = true;
        CoreSettings.Current.AttentionChecksEnabled = true;
        video.Start();
        try
        {
            Assert.True(video.Trigger()); clock.Advance(MandatoryVideoScheduler.PreRoll);
            video.NoteSpawn();
            video.Ended(); // Fail verdict: the message is up, the replay follows
            Assert.NotNull(host.Pending);
            Assert.Equal("Watching", _rpc.CurrentState); // WPF CloseAll never idles
        }
        finally { video.Stop(); }
    }

    private sealed class Host : IMandatoryVideoHost
    {
        internal int Shows;
        internal bool HoldMessage;
        internal Action? Pending;
        public void Show(string path, bool strict) => Shows++;
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int milliseconds, Action after)
        {
            if (HoldMessage) Pending = after; else after();
        }
    }
}
