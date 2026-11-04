using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SpeakPromptSessionTests
{
    private sealed class Source : IPlaybackTimeSource
    {
        public event Action<double>? PlaybackTimeChanged;
        public double Time;
        public readonly TaskCompletionSource<bool> Paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public double GetCurrentTimeSeconds() => Time;
        public double GetDurationSeconds() => 0;
        public bool IsPlaying => true;
        public void Seek(double seconds) { }
        public int Pauses, Plays;
        public void Pause() { Pauses++; Paused.TrySetResult(true); }
        public void Play() => Plays++;
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
    }

    private sealed class Fixture
    {
        public readonly Source Source = new();
        public bool Available = true, Consent = true;
        public int Consents, Borrows, Restores, Shows, Hides, Listens;
        public string? Cue;
        public TaskCompletionSource<PhraseResult> Heard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SpeakPromptSession Create() => new(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCue = "Say YES", SpeakCueMode = SpeakCueMode.Persistent,
            SpeakHoldMode = SpeakHoldMode.Pause
        }, Source, () => Available, () => { Consents++; return Task.FromResult(Consent); },
        () => { Borrows++; return true; }, () => Restores++,
        async (_, ct) => { Listens++; return await Heard.Task.WaitAsync(ct); },
        cue => { Shows++; Cue = cue; }, () => Hides++);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task UnavailableOrDeclinedDoesNotCaptureOrShow(bool available, bool consent)
    {
        var f = new Fixture { Available = available, Consent = consent };
        f.Heard.SetResult(new PhraseResult { Matched = true });
        await f.Create().RunAsync();
        Assert.Equal(0, f.Borrows);
        Assert.Equal(0, f.Shows);
        Assert.Equal(0, f.Listens);
        Assert.Equal(0, f.Source.Pauses);
        Assert.Equal(available ? 1 : 0, f.Consents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletionOrStopHidesReleasesAndRestores(bool stop)
    {
        var f = new Fixture();
        var session = f.Create();
        var run = session.RunAsync();
        Assert.Equal("Say YES", f.Cue);
        Assert.Equal(1, f.Shows);
        Assert.Equal(1, f.Borrows);
        Assert.Equal(1, f.Source.Pauses);
        Assert.Equal(0, f.Hides);
        if (stop) session.Stop(); else f.Heard.SetResult(new PhraseResult { Matched = true });
        await run;
        Assert.Equal(1, f.Hides);
        Assert.Equal(1, f.Restores);
        Assert.Equal(1, f.Source.Plays);
        session.Stop();
        Assert.Equal(1, f.Hides);
    }

    [Fact]
    public async Task StopDuringConsentCannotStartCapture()
    {
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int borrows = 0, shows = 0, listens = 0;
        var session = new SpeakPromptSession(new TriggerEffectAction { SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent }, null,
            () => true, () => consent.Task, () => { borrows++; return true; }, () => { },
            (_, _) => { listens++; return Task.FromResult(new PhraseResult { Matched = true }); }, _ => shows++, () => { });
        var run = session.RunAsync();
        session.Stop();
        consent.SetResult(true);
        await run;
        Assert.Equal(0, borrows);
        Assert.Equal(0, shows);
        Assert.Equal(0, listens);
    }
    [Fact]
    public async Task RegionHoldPausesNearEndAndResumesOnStop()
    {
        var source = new Source { Time = 9.8 };
        var session = new SpeakPromptSession(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent,
            SpeakHoldMode = SpeakHoldMode.Pause, SpeakRegionStartSec = 1, SpeakRegionEndSec = 10
        }, source, () => true, () => Task.FromResult(true), () => true, () => { },
        async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new PhraseResult(); },
        _ => { }, () => { });
        var run = session.RunAsync();
        await source.Paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.Stop();
        await run;
        Assert.Equal(1, source.Plays);
    }

    [Fact]
    public async Task ShowFailureStillHidesAndRestoresMicrophone()
    {
        int hides = 0, restores = 0;
        var session = new SpeakPromptSession(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent
        }, null, () => true, () => Task.FromResult(true), () => true, () => restores++,
        (_, _) => Task.FromResult(new PhraseResult()), _ => throw new InvalidOperationException(), () => hides++);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync());
        Assert.Equal(1, hides);
        Assert.Equal(1, restores);
    }

    [Fact]
    public async Task UnsupportedIntermittentCueDoesNotStartCapture()
    {
        int borrows = 0, listens = 0;
        var session = new SpeakPromptSession(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Intermittent
        }, null, () => true, () => Task.FromResult(true), () => { borrows++; return true; }, () => { },
        (_, _) => { listens++; return Task.FromResult(new PhraseResult { Matched = true }); }, _ => { }, () => { });
        await session.RunAsync();
        Assert.Equal(0, borrows);
        Assert.Equal(0, listens);
    }

}
