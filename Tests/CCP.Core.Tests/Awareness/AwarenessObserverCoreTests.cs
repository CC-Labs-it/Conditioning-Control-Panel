using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace CCP.Core.Tests.Awareness;

[CollectionDefinition("AwarenessObserverCore", DisableParallelization = true)]
public sealed class AwarenessObserverCoreCollection { }

[Collection("AwarenessObserverCore")]
public sealed class AwarenessObserverCoreTests : IDisposable
{
    private readonly Func<SettingsService?>? _oldSettings = CoreSettings.ServiceProvider;
    private readonly Func<bool>? _oldEntitlement = AwarenessObserver.EntitlementProvider;
    private readonly Action<Action>? _oldPost = AwarenessObserver.PostToUi;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Input _input = new();
    private readonly Media _media = new();
    private readonly Foreground _foreground = new();
    private readonly List<ContextFrame> _frames = new();
    private AwarenessObserver? _observer;
    private ActivityLedger? _ledger;
    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public AwarenessObserverCoreTests()
    {
        Directory.CreateDirectory(_dir);
        CoreSettings.ServiceProvider = null;
        AwarenessObserver.EntitlementProvider = null;
        AwarenessObserver.PostToUi = a => a();
    }

    private AwarenessObserver Create()
    {
        _ledger = new ActivityLedger(Path.Combine(_dir, "ledger.json"), () => Now, () => 30);
        var policy = new AwarenessPolicySettings(Array.Empty<string>(), Array.Empty<string>(), true, true);
        _observer = new AwarenessObserver(_ledger, new WorthinessScorer(() => AwarenessIntensity.Unhinged),
            new SilentArbiter(), new StubCompanionMemory(), () => Now,
            _foreground, _input, new Microphone(), _media, new AppState(), () => policy, _time);
        _observer.FrameCut += (_, f) => _frames.Add(f);
        return _observer;
    }

    private void Enable()
    {
        var settings = new SettingsService();
        settings.Current.UseAwarenessV2 = true;
        settings.Current.AwarenessModeEnabled = true;
        settings.Current.AwarenessConsentGiven = true;
        settings.Current.AwarenessConsentShownV2 = true;
        CoreSettings.ServiceProvider = () => settings;
        AwarenessObserver.EntitlementProvider = () => true;
    }

    [Fact]
    public void MissingSettingsEntitlementOrV2ConsentNeverArmsTheObserver()
    {
        var observer = Create();
        Assert.False(AwarenessObserver.IsEnabled);
        observer.Start();
        Assert.False(observer.IsRunning);
        Assert.Equal(0, _media.Starts);
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(0, _foreground.Reads);
        Enable();
        AwarenessObserver.EntitlementProvider = null;
        Assert.False(AwarenessObserver.IsEnabled);
        observer.Start();
        Assert.False(observer.IsRunning);
        Assert.Equal(0, _media.Starts);
        AwarenessObserver.EntitlementProvider = () => true;
        CoreSettings.Service!.Current.AwarenessConsentShownV2 = false;
        Assert.False(AwarenessObserver.IsEnabled);
        observer.Start();
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(observer.IsRunning);
        Assert.Equal(0, _media.Starts);
        Assert.Equal(0, _foreground.Reads);
    }

    [Fact]
    public async Task StopAndDisposeStopMediaAndClearRepeatState()
    {
        Enable();
        var observer = Create();
        observer.Start();
        Assert.Equal(1, _media.Starts);
        _time.Advance(AwarenessObserver.PollInterval);
        Assert.True(_foreground.Reads > 0);
        AwarenessObserver.PostToUi = _ => { };
        await Prime();
        await Sample("A", 2);
        await Sample("A", 200);
        observer.Stop();
        Assert.True(_media.Stops > 0);
        var reads = _foreground.Reads;
        AwarenessObserver.PostToUi = a => a();
        _time.Advance(AwarenessObserver.PollInterval);
        Assert.Equal(reads, _foreground.Reads);
        AwarenessObserver.PostToUi = _ => { };
        observer.Start();
        await Prime();
        _frames.Clear();
        await Sample("A", 2);
        Assert.DoesNotContain(_frames, f => f.Transition == TransitionKind.MediaChanged);
        observer.Dispose();
        Assert.True(_media.Stops >= 2);
        Assert.True(_media.Disposed);
    }

    [Fact]
    public void QueuedTickFromStoppedRunCannotObserveAfterRestart()
    {
        Enable();
        var pending = new List<Action>();
        AwarenessObserver.PostToUi = pending.Add;
        var observer = Create();
        observer.Start();
        _time.Advance(AwarenessObserver.PollInterval);
        var stale = Assert.Single(pending);
        observer.Stop();
        observer.Start();
        stale();
        Assert.Equal(0, _foreground.Reads);
    }

    [Fact]
    public async Task TitleChangeAndThirdPlayProduceMediaFrames()
    {
        Enable();
        Create();
        await Prime();
        _frames.Clear();
        await Sample("B", 200);
        var title = Assert.Single(_frames.Where(f => f.Transition == TransitionKind.MediaChanged));
        Assert.Equal(1, title.NowPlaying!.RepeatCount);
        Assert.Equal("B", title.NowPlaying.Title);
        _frames.Clear();
        await Sample("B", 2);
        await Sample("B", 200);
        Assert.DoesNotContain(_frames, f => f.Transition == TransitionKind.MediaChanged);
        await Sample("B", 2);
        var loop = Assert.Single(_frames.Where(f => f.Transition == TransitionKind.MediaChanged));
        Assert.Equal(3, loop.NowPlaying!.RepeatCount);
    }

    [Fact]
    public async Task PlayingMediaKeepsIdleTimeAccruing()
    {
        Enable();
        Create();
        _input.Idle = 1800;
        _media.Sample = new MediaSample("A", "artist", "Playing", TimeSpan.FromSeconds(200));
        for (int i = 0; i < 10; i++) await Tick(60);
        Assert.True(_ledger!.Snapshot("spotify", Now).MinutesToday >= 8);
    }

    private async Task Prime()
    {
        _media.Sample = new MediaSample("A", "artist", "Playing", TimeSpan.FromSeconds(200));
        await Tick(1);
        await Tick(21);
    }
    private async Task Sample(string title, int position)
    {
        _media.Sample = new MediaSample(title, "artist", "Playing", TimeSpan.FromSeconds(position));
        await Tick(2);
    }
    private async Task Tick(int seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        await _observer!.TickAsync(Now);
    }
    public void Dispose()
    {
        _observer?.Dispose();
        CoreSettings.ServiceProvider = _oldSettings;
        AwarenessObserver.EntitlementProvider = _oldEntitlement;
        AwarenessObserver.PostToUi = _oldPost;
        try { Directory.Delete(_dir, true); } catch { }
    }
    private sealed class Foreground : IForegroundProbe
    {
        public int Reads;
        public ForegroundSample Read() { Reads++; return new ForegroundSample(new IntPtr(1), "Spotify Premium", "spotify", false); }
    }
    private sealed class Input : IInputProbe
    {
        public int Idle;
        public int IdleSeconds => Idle;
        public bool IsTypingBurst => false;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Microphone : IMicrophoneProbe { public bool IsInUse(DateTime at) => false; }
    private sealed class AppState : IAppStateProbe { public AppStateSample Read(DateTime at) => AppStateSample.Empty; }
    private sealed class Media : IMediaWatcher
    {
        public int Starts, Stops;
        public bool Disposed;
        public MediaSample? Sample;
        public MediaSample? Current => Sample;
        public bool IsAvailable => true;
        public void Start() => Starts++;
        public void Stop() => Stops++;
        public void Dispose() => Disposed = true;
    }
    private sealed class SilentArbiter : IReactionArbiter
    {
        public Task<ArbiterDecision> SubmitAsync(ContextFrame frame, CancellationToken cancellationToken = default) => Task.FromResult(ArbiterDecision.Silent("test"));
        public void RecordExternalLine(ReactionSource source, string? appId = null) { }
        public bool CanSpeak(ReactionSource source, string? appId = null) => true;
    }
}
