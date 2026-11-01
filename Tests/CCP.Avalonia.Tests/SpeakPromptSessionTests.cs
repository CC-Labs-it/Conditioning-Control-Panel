using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpeakPromptSessionTests
{
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var prompted = SpeakPromptSession.PromptedConsentThisRun;
        SpeakPromptSession.PromptedConsentThisRun = false;
        try { await body(); }
        finally { SpeakPromptSession.PromptedConsentThisRun = prompted; }
    });

    private sealed class Playback : IPlaybackTimeSource
    {
        public event Action<double>? PlaybackTimeChanged { add { } remove { } }
        public int Pauses, Plays;
        public double GetCurrentTimeSeconds() => 0;
        public double GetDurationSeconds() => 60;
        public bool IsPlaying => true;
        public void Seek(double seconds) { }
        public void Pause() => Pauses++;
        public void Play() => Plays++;
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
    }

    private static SpeakPromptSession Session(Playback source, string target = "YES", int reps = 1) => new(
        new TriggerEffectAction { SpeakTarget = target, SpeakRequiredReps = reps, SpeakCueMode = SpeakCueMode.Intermittent,
            SpeakCompletion = SpeakCompletion.UntilSatisfied, SpeakHoldMode = SpeakHoldMode.Pause,
            SpeakCorrectMessage = "good", SpeakIncorrectMessage = "again" }, source)
    {
        Available = () => true, Consent = () => true, EvictVoice = () => false,
        Flash = _ => { }, Giggle = _ => { }, IsListening = () => false,
        RestoreVoice = () => { }, StopListening = () => { }, Delay = (_, _) => Task.CompletedTask
    };

    [Fact]
    public void CustomFlash_TrimsLimitsAndRemovesMarkup()
    {
        Assert.Equal("say YES", SpeakPromptSession.SanitizeFlash("  say <b>YES</b>  "));
        Assert.Equal(new string('x', 200), SpeakPromptSession.SanitizeFlash(new string('x', 220)));
    }

    [Fact]
    public Task EmptyAndUnavailable_DoNotAskOrClaimMic() => Run(async () =>
    {
        var source = new Playback();
        var calls = 0;
        var blank = Session(source, "  ");
        blank.AskConsent = () => { calls++; return Task.FromResult(true); };
        blank.EvictVoice = () => { calls++; return true; };
        blank.Recognize = (_, _, _) => Task.FromResult(new PhraseResult { Matched = true });
        await blank.StartAsync();
        var missing = Session(source);
        missing.Available = () => false;
        missing.Consent = () => { calls++; return false; };
        await missing.StartAsync();
        Assert.Equal(0, calls);
        Assert.Equal(0, source.Pauses);
    });

    [Fact]
    public Task Consent_AskedOnceAndMustBePersisted() => Run(async () =>
    {
        var source = new Playback();
        int asked = 0, claimed = 0;
        var first = Session(source);
        first.Consent = () => false;
        first.AskConsent = () => { asked++; return Task.FromResult(true); };
        first.EvictVoice = () => { claimed++; return true; };
        first.Recognize = (_, _, _) => Task.FromResult(new PhraseResult { Matched = true });
        await first.StartAsync(); // dialog success without saved consent is insufficient
        var second = Session(source);
        second.Consent = () => false;
        second.AskConsent = () => { asked++; return Task.FromResult(true); };
        await second.StartAsync();
        Assert.Equal(1, asked);
        Assert.Equal(0, claimed);
        Assert.Equal(0, source.Pauses);
    });

    [Fact]
    public Task GrantedConsent_StartsAndCompletes() => Run(async () =>
    {
        var source = new Playback();
        bool consent = false;
        int listens = 0;
        var s = Session(source);
        s.Consent = () => consent;
        s.AskConsent = () => { consent = true; return Task.FromResult(true); };
        s.Recognize = (_, _, _) => { listens++; return Task.FromResult(new PhraseResult { Matched = true }); };
        await s.StartAsync();
        await s.StartAsync();
        Assert.Equal(1, listens);
        Assert.Equal(1, source.Pauses);
        Assert.Equal(1, source.Plays);
    });

    [Fact]
    public Task StopBeforeStart_LeavesAnotherCaptureAlone() => Run(() =>
    {
        var s = Session(new Playback());
        var stops = 0;
        s.IsListening = () => true;
        s.StopListening = () => stops++;
        s.Stop();
        Assert.Equal(0, stops);
        return Task.CompletedTask;
    });

    [Fact]
    public Task StopDuringConsent_NeverStartsEvenIfConsentIsGranted() => Run(async () =>
    {
        var source = new Playback();
        var answer = new TaskCompletionSource<bool>();
        var consent = false;
        var listens = 0;
        var stops = 0;
        var s = Session(source);
        s.IsListening = () => true;
        s.StopListening = () => stops++;
        s.Consent = () => consent;
        s.AskConsent = () => answer.Task;
        s.Recognize = (_, _, _) => { listens++; return Task.FromResult(new PhraseResult { Matched = true }); };
        var run = s.StartAsync();
        s.Stop();
        consent = true;
        answer.SetResult(true);
        await run;
        Assert.Equal(0, source.Pauses);
        Assert.Equal(0, listens);
        Assert.Equal(0, stops);
    });

    [Fact]
    public Task Listen_HandsOffMic_RetriesAndCountsReps_ThenReleasesHold() => Run(async () =>
    {
        var source = new Playback();
        var messages = new List<string>();
        var delays = new List<int>();
        var results = new Queue<PhraseResult>(new[] { PhraseResult.NotAvailable,
            new PhraseResult { TimedOut = true, Transcript = "" }, new PhraseResult { Transcript = "no" },
            new PhraseResult { Matched = true }, new PhraseResult { Matched = true } });
        int waits = 0, restores = 0, giggles = 0;
        var s = Session(source, reps: 2);
        s.EvictVoice = () => true;
        s.IsListening = () => waits++ < 2;
        s.RestoreVoice = () => restores++;
        s.Flash = messages.Add;
        s.Giggle = text => { Assert.Equal("good", text); giggles++; };
        s.Delay = (ms, _) => { delays.Add(ms); return Task.CompletedTask; };
        s.Recognize = (target, opts, _) =>
        {
            Assert.Equal("YES", target);
            Assert.Equal(TimeSpan.FromSeconds(10), opts.Timeout);
            Assert.Equal(1, source.Pauses);
            return Task.FromResult(results.Dequeue());
        };
        await s.StartAsync();
        Assert.Equal(new[] { 25, 25, 200, 700, 600 }, delays);
        Assert.Equal(new[] { "Say YES", "again", "good", "good" }, messages);
        Assert.Equal(2, giggles);
        Assert.Equal(1, source.Plays);
        Assert.Equal(1, restores);
        s.Stop();
        Assert.Equal(1, source.Plays);
        Assert.Equal(1, restores);
    });

    [Fact]
    public Task Stop_CancelsListen_ReleasesOnce_AndIgnoresLateResult() => Run(async () =>
    {
        var source = new Playback();
        var result = new TaskCompletionSource<PhraseResult>();
        int restores = 0, stops = 0, feedback = 0;
        CancellationToken token = default;
        var s = Session(source);
        s.EvictVoice = () => true;
        s.IsListening = () => false;
        s.RestoreVoice = () => restores++;
        s.Recognize = (_, _, ct) => { token = ct; return result.Task; };
        s.Flash = text => { if (text == "good") feedback++; };
        var run = s.StartAsync();
        Assert.Equal(1, source.Pauses);
        s.IsListening = () => true;
        s.StopListening = () => stops++;
        s.Stop();
        s.Stop();
        Assert.True(token.IsCancellationRequested);
        result.SetResult(new PhraseResult { Matched = true });
        await run;
        Assert.Equal(0, feedback);
        Assert.Equal(1, stops);
        Assert.Equal(1, source.Plays);
        Assert.Equal(1, restores);
    });

    [Fact]
    public Task CancellationCallbackFailure_StillReleasesHold() => Run(async () =>
    {
        var source = new Playback();
        var result = new TaskCompletionSource<PhraseResult>();
        var s = Session(source);
        s.Recognize = (_, _, ct) =>
        {
            ct.Register(() => throw new InvalidOperationException("callback failed"));
            return result.Task;
        };
        var run = s.StartAsync();
        try { s.Stop(); }
        finally { result.TrySetResult(PhraseResult.NotAvailable); await run; }
        Assert.Equal(1, source.Plays);
    });

    [Fact]
    public Task Start_FromWorkerRunsOnUiThread() => Run(async () =>
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var s = Session(new Playback());
        s.Recognize = (_, _, _) =>
        {
            done.TrySetResult(global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            return Task.FromResult(new PhraseResult { Matched = true });
        };
        await Task.Run(s.Start);
        Assert.Same(done.Task, await Task.WhenAny(done.Task, Task.Delay(2000)));
        Assert.True(await done.Task);
        s.Stop();
    });

    [Fact]
    public Task LostSpeechOrDeadline_ReleasesHold() => Run(async () =>
    {
        var source = new Playback();
        int probes = 0;
        var delays = new List<int>();
        var s = Session(source);
        s.Available = () => ++probes == 1;
        s.Delay = (ms, _) => { delays.Add(ms); return Task.CompletedTask; };
        await s.StartAsync();
        Assert.Equal(8, delays.Count);
        Assert.All(delays, d => Assert.Equal(400, d));
        Assert.Equal(1, source.Plays);

        var deadlineSource = new Playback();
        var deadline = Session(deadlineSource);
        deadline.UtcNow = () => DateTime.UtcNow.AddMinutes(1);
        var deadlineListens = 0;
        deadline.Recognize = (_, _, _) => { deadlineListens++; return Task.FromResult(new PhraseResult { Matched = true }); };
        await deadline.StartAsync();
        Assert.Equal(0, deadlineListens);
        Assert.Equal(1, deadlineSource.Plays);
    });
}
