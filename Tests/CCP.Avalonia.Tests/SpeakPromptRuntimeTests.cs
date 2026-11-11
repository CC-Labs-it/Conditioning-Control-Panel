using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SpeakPromptRuntimeTests
{
    private sealed class Source : IPlaybackTimeSource
    {
        public event Action<double>? PlaybackTimeChanged { add { } remove { } }
        public double GetCurrentTimeSeconds() => 0;
        public double GetDurationSeconds() => 0;
        public bool IsPlaying => true;
        public void Seek(double seconds) { }
        public void Pause() { }
        public void Play() { }
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
    }

    private sealed class Fixture
    {
        public bool Consented, Answer, Armed, Listening;
        public int Asks, Listens, Stops, Restores, Shows, Hides, StopListening, ListeningChecks;
        public TimeSpan Timeout;
        public readonly List<string> Events = new();
        public readonly TaskCompletionSource<bool> Hidden = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<PhraseResult> Heard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SpeakPromptRuntime Runtime() => new(() => true, () => Consented,
            () => { Asks++; Consented = Answer; return Task.FromResult(Answer); },
            () => Armed, () => { Events.Add("stop"); Stops++; Armed = false; },
            () => { Events.Add("restore"); Restores++; Armed = true; }, () => { ListeningChecks++; return Listening; },
            async (_, options, ct) => { Timeout = options.Timeout; Events.Add("listen"); Listens++; Started.TrySetResult(true); return await Heard.Task.WaitAsync(ct); },
            () => StopListening++, _ => Shows++, () => { Hides++; Hidden.TrySetResult(true); });
        public static TriggerEffectAction Effect(EffectPhase phase = EffectPhase.Start) => new()
        {
            EffectType = EffectTypes.Speak, EffectId = "test", Phase = phase,
            SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent, SpeakHoldMode = SpeakHoldMode.KeepPlaying
        };
    }

    [Fact]
    public async Task DeclineIsAskedOnceAndNeverCaptures()
    {
        var f = new Fixture { Armed = true };
        var runtime = f.Runtime();
        await runtime.CreateSession(Fixture.Effect(), null).RunAsync();
        await runtime.CreateSession(Fixture.Effect(), null).RunAsync();
        Assert.Equal(1, f.Asks);
        Assert.Equal(0, f.Stops);
        Assert.Equal(0, f.Listens);
        Assert.Equal(0, f.Shows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArmedVoiceIsRestoredAfterCompletionOrStop(bool stop)
    {
        var f = new Fixture { Consented = true, Armed = true };
        var session = f.Runtime().CreateSession(Fixture.Effect(), null);
        var run = session.RunAsync();
        await f.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "stop", "listen" }, f.Events);
        Assert.False(f.Armed);
        if (stop) session.Stop(); else f.Heard.SetResult(new PhraseResult { Matched = true });
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, f.Hides);
        Assert.Equal(1, f.Restores);
        Assert.True(f.Armed);
        Assert.Equal(stop ? 1 : 0, f.StopListening);
    }

    [Fact]
    public async Task WaitsForEvictedCaptureToRelease()
    {
        var f = new Fixture { Consented = true, Armed = true, Listening = true };
        var session = f.Runtime().CreateSession(Fixture.Effect(), null);
        var run = session.RunAsync();
        Assert.Equal(1, f.Stops);
        Assert.Equal(0, f.Listens);
        f.Listening = false;
        await f.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, f.Restores);
    }

    [Fact]
    public async Task ListenUsesTenSecondTimeout()
    {
        var f = new Fixture { Consented = true };
        var session = f.Runtime().CreateSession(Fixture.Effect(), null);
        var run = session.RunAsync();
        await f.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(10), f.Timeout);
        session.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BusyCaptureWaitIsBounded()
    {
        var f = new Fixture { Consented = true, Armed = true, Listening = true };
        var session = f.Runtime().CreateSession(Fixture.Effect(), null);
        var run = session.RunAsync();
        await f.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(24, f.ListeningChecks);
        session.Stop();
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StartupFactoryRoutesSpeakAndOtherActions()
    {
        var f = new Fixture { Consented = true };
        var before = EnhancementHostService.DispatcherFactory;
        try
        {
            App.InstallSpeakPromptRuntime(f.Runtime());
            Assert.NotNull(EnhancementHostService.DispatcherFactory);
            var dispatcher = Assert.IsType<SpeakActionDispatcher>(EnhancementHostService.DispatcherFactory!());
            var context = new EnhancementDispatchContext(new Enhancement(), new Source(), 0, null);
            await dispatcher.DispatchAsync(Fixture.Effect(), context);
            await f.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, f.Shows);
            await dispatcher.DispatchAsync(Fixture.Effect(EffectPhase.Stop), context);
            await f.Hidden.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await dispatcher.DispatchAsync(new PauseAction(), context);
            var logger = Assert.IsType<LoggingActionDispatcher>(typeof(SpeakActionDispatcher).GetField("_other", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dispatcher));
            Assert.Contains(logger.RecentActions, line => line.Contains("pause"));
        }
        finally { EnhancementHostService.DispatcherFactory = before; }
    }
}
