using System;
using System.Threading;
using ConditioningControlPanel;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntermittentCueFlashesUntilCompletionOrStop(bool stop)
    {
        int borrows = 0, listens = 0, shows = 0, hides = 0, flashes = 0;
        var second = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var heard = new TaskCompletionSource<PhraseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CoreSubliminal.CustomShowProvider = (text, suppressHaptic) =>
        {
            Assert.Equal("Say YES", text);
            Assert.True(suppressHaptic);
            if (Interlocked.Increment(ref flashes) >= 2) second.TrySetResult(true);
        };
        var session = new SpeakPromptSession(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCue = "Say YES", SpeakCueMode = SpeakCueMode.Intermittent,
            SpeakCueIntervalMs = 80
        }, null, () => true, () => Task.FromResult(true), () => { borrows++; return true; }, () => { },
        async (_, ct) => { listens++; return await heard.Task.WaitAsync(ct); }, _ => shows++, () => hides++, (_, _) => { });
        var run = session.RunAsync();
        Assert.Equal(1, flashes);
        Assert.Equal(1, borrows);
        Assert.Equal(1, listens);
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (stop) session.Stop(); else heard.SetResult(new PhraseResult { Matched = true });
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        var count = Volatile.Read(ref flashes);
        await Task.Delay(250);
        Assert.Equal(count, Volatile.Read(ref flashes));
        Assert.Equal(0, shows);
        Assert.Equal(0, hides);
    }

    [Fact]
    public async Task BusyRecognitionRetriesPastEightUntilMatched()
    {
        int listens = 0, hides = 0;
        var session = new SpeakPromptSession(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent
        }, null, () => true, () => Task.FromResult(true), () => false, () => { },
        (_, _) => Task.FromResult(++listens <= 9 ? PhraseResult.NotAvailable : new PhraseResult { Matched = true }),
        _ => { }, () => hides++);
        await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(10, listens);
        Assert.Equal(1, hides);
    }

    [Fact]
    public async Task RecognitionExceptionRetriesWithoutReleasingCueOrPlayback()
    {
        var source = new Source();
        int listens = 0, hides = 0;
        var session = new SpeakPromptSession(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent, SpeakHoldMode = SpeakHoldMode.Pause
        }, source, () => true, () => Task.FromResult(true), () => false, () => { },
        (_, _) => ++listens == 1 ? Task.FromException<PhraseResult>(new InvalidOperationException()) :
            Task.FromResult(new PhraseResult { Matched = true }),
        _ => { }, () => hides++);
        await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, listens);
        Assert.Equal(1, hides);
        Assert.Equal(1, source.Pauses);
        Assert.Equal(1, source.Plays);
    }

    [Theory]
    [InlineData(true, 550)]
    [InlineData(false, 650)]
    public async Task FeedbackWaitsBeforeNextListen(bool firstCorrect, int minimumMs)
    {
        int listens = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long firstListen = 0, secondListen = 0;
        var feedback = new System.Collections.Generic.List<(string, bool)>();
        var session = new SpeakPromptSession(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent, SpeakRequiredReps = 2,
            SpeakCorrectMessage = "correct", SpeakIncorrectMessage = "incorrect"
        }, null, () => true, () => Task.FromResult(true), () => false, () => { },
        (_, _) =>
        {
            listens++;
            if (listens == 1) firstListen = clock.ElapsedMilliseconds;
            if (listens == 2) secondListen = clock.ElapsedMilliseconds;
            return Task.FromResult(new PhraseResult { Matched = listens != 1 || firstCorrect });
        }, _ => { }, () => { }, (text, correct) => feedback.Add((text, correct)));
        await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(firstCorrect ? ("correct", true) : ("incorrect", false), feedback[0]);
        Assert.Equal(2, feedback.FindAll(f => f == ("correct", true)).Count);
        Assert.True(secondListen - firstListen >= minimumMs, $"Next listen started after {secondListen - firstListen} ms");
    }

}
