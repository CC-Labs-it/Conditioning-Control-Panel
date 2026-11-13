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
        public int Consents, Borrows, Restores, Shows, Hides, Listens, Feedbacks;
        public string? Cue;
        public TaskCompletionSource<PhraseResult> Heard = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SpeakPromptSession Create() => new(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCue = "Say YES", SpeakCueMode = SpeakCueMode.Persistent,
            SpeakHoldMode = SpeakHoldMode.Pause
        }, Source, () => Available, () => { Consents++; return Task.FromResult(Consent); },
        () => { Borrows++; return true; }, () => Restores++,
        async (_, ct) => { Listens++; return await Heard.Task.WaitAsync(ct); },
        cue => { Shows++; Cue = cue; }, () => Hides++, (_, _) => Feedbacks++);
    }

    private static SpeakPromptSession New(SpeakHoldMode hold = SpeakHoldMode.LoopRegion, Source? source = null,
        Func<string, CancellationToken, Task<PhraseResult>>? listen = null, Func<bool>? borrow = null, Action? restore = null,
        Action<string>? show = null, Action? hide = null, Func<Task<bool>>? consent = null, Action<string, bool>? feedback = null,
        SpeakCueMode mode = SpeakCueMode.Persistent, double? regionEnd = null, int reps = 1) => new(new TriggerEffectAction
        {
            SpeakTarget = "yes", SpeakCueMode = mode, SpeakHoldMode = hold, SpeakRequiredReps = reps,
            SpeakRegionStartSec = regionEnd.HasValue ? 1 : null, SpeakRegionEndSec = regionEnd,
            SpeakCorrectMessage = "correct", SpeakIncorrectMessage = "incorrect"
        }, source, () => true, consent ?? (() => Task.FromResult(true)), borrow ?? (() => false), restore ?? (() => { }),
        listen ?? ((_, _) => Task.FromResult(new PhraseResult { Matched = true })), show ?? (_ => { }), hide ?? (() => { }), feedback ?? ((_, _) => { }));

    [Fact]
    public async Task FixtureFeedbackDoesNotCallGlobalXpProvider()
    {
        var previous = ConditioningControlPanel.CoreProgression.AddXPProvider;
        int awards = 0;
        ConditioningControlPanel.CoreProgression.AddXPProvider = (_, _) => awards++;
        try
        {
            var f = new Fixture();
            f.Heard.SetResult(new PhraseResult { Matched = true });
            await f.Create().RunAsync();
            Assert.Equal((1, 0), (f.Feedbacks, awards));
        }
        finally { ConditioningControlPanel.CoreProgression.AddXPProvider = previous; }
    }

    [Theory, InlineData(false, true), InlineData(true, false)]
    public async Task UnavailableOrDeclinedDoesNotCaptureOrShow(bool available, bool consent)
    {
        var f = new Fixture { Available = available, Consent = consent };
        f.Heard.SetResult(new PhraseResult { Matched = true });
        await f.Create().RunAsync();
        Assert.Equal((0, 0, 0, 0), (f.Borrows, f.Shows, f.Listens, f.Source.Pauses));
        Assert.Equal(available ? 1 : 0, f.Consents);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task CompletionOrStopHidesReleasesAndRestores(bool stop)
    {
        var f = new Fixture();
        var session = f.Create();
        var run = session.RunAsync();
        Assert.Equal("Say YES", f.Cue);
        Assert.Equal((1, 1, 1, 0), (f.Shows, f.Borrows, f.Source.Pauses, f.Hides));
        if (stop) session.Stop(); else f.Heard.SetResult(new PhraseResult { Matched = true });
        await run;
        Assert.Equal((1, 1, 1), (f.Hides, f.Restores, f.Source.Plays));
        session.Stop();
        Assert.Equal(1, f.Hides);
    }

    [Fact]
    public async Task StopDuringConsentCannotStartCapture()
    {
        var consent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int borrows = 0, shows = 0, listens = 0;
        var session = New(consent: () => consent.Task, borrow: () => { borrows++; return true; },
            listen: (_, _) => { listens++; return Task.FromResult(new PhraseResult { Matched = true }); }, show: _ => shows++);
        var run = session.RunAsync();
        session.Stop();
        consent.SetResult(true);
        await run;
        Assert.Equal((0, 0, 0), (borrows, shows, listens));
    }
    [Fact]
    public async Task RegionHoldPausesNearEndAndResumesOnStop()
    {
        var source = new Source { Time = 9.8 };
        var session = New(SpeakHoldMode.Pause, source, async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new PhraseResult(); },
            () => true, regionEnd: 10);
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
        var session = New(listen: (_, _) => Task.FromResult(new PhraseResult()), borrow: () => true, restore: () => restores++,
            show: _ => throw new InvalidOperationException(), hide: () => hides++);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync());
        Assert.Equal((1, 1), (hides, restores));
    }

    [Fact]
    public async Task UnsupportedIntermittentCueDoesNotStartCapture()
    {
        int borrows = 0, listens = 0;
        var session = New(mode: SpeakCueMode.Intermittent, borrow: () => { borrows++; return true; },
            listen: (_, _) => { listens++; return Task.FromResult(new PhraseResult { Matched = true }); });
        await session.RunAsync();
        Assert.Equal((0, 0), (borrows, listens));
    }

    [Fact]
    public async Task BusyRecognitionRetriesPastEightUntilMatched()
    {
        int listens = 0, hides = 0;
        var session = New(listen: (_, _) => Task.FromResult(++listens <= 9 ? PhraseResult.NotAvailable : new PhraseResult { Matched = true }),
            hide: () => hides++);
        await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((10, 1), (listens, hides));
    }

    [Fact]
    public async Task RecognitionExceptionRetriesWithoutReleasingCueOrPlayback()
    {
        var source = new Source();
        int listens = 0, hides = 0;
        var session = New(SpeakHoldMode.Pause, source, (_, _) => ++listens == 1 ? Task.FromException<PhraseResult>(new InvalidOperationException()) :
            Task.FromResult(new PhraseResult { Matched = true }), hide: () => hides++);
        await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal((2, 1, 1, 1), (listens, hides, source.Pauses, source.Plays));
    }

    [Theory, InlineData(true, 550), InlineData(false, 650)]
    public async Task FeedbackWaitsBeforeNextListen(bool firstCorrect, int minimumMs)
    {
        int listens = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long firstListen = 0, secondListen = 0;
        var feedback = new System.Collections.Generic.List<(string, bool)>();
        var session = New(listen: (_, _) =>
        {
            if (++listens == 1) firstListen = clock.ElapsedMilliseconds;
            if (listens == 2) secondListen = clock.ElapsedMilliseconds;
            return Task.FromResult(new PhraseResult { Matched = listens != 1 || firstCorrect });
        }, feedback: (text, correct) => feedback.Add((text, correct)), reps: 2);
        await session.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(firstCorrect ? ("correct", true) : ("incorrect", false), feedback[0]);
        Assert.Equal(2, feedback.FindAll(f => f == ("correct", true)).Count);
        Assert.True(secondListen - firstListen >= minimumMs, $"Next listen started after {secondListen - firstListen} ms");
    }
}
