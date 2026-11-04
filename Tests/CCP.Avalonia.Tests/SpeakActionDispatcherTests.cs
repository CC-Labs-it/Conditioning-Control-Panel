using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SpeakActionDispatcherTests
{
    private sealed class Source : IPlaybackTimeSource
    {
        public event Action<double>? PlaybackTimeChanged;
        private double _time;
        public void Advance(double time) { _time = time; PlaybackTimeChanged?.Invoke(time); }
        public double GetCurrentTimeSeconds() => _time;
        public double GetDurationSeconds() => 0;
        public bool IsPlaying => true;
        public void Seek(double seconds) { }
        public void Pause() { }
        public void Play() { }
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
    }

    private sealed class Fixture
    {
        public readonly Source Source = new();
        public readonly TaskCompletionSource<bool>[] Shows = { NewSignal(), NewSignal() };
        public readonly TaskCompletionSource<bool>[] Hides = { NewSignal(), NewSignal() };
        public readonly TaskCompletionSource<bool>[] Restores = { NewSignal(), NewSignal() };
        public int Shown, Hidden, Borrowed, Restored;
        public EnhancementDispatchContext Context => new(new Enhancement(), Source, 0, null);
        public SpeakActionDispatcher Dispatcher() => new((effect, source) => new SpeakPromptSession(effect, source,
            () => true, () => Task.FromResult(true), () => { Interlocked.Increment(ref Borrowed); return true; },
            () => Restores[Interlocked.Increment(ref Restored) - 1].TrySetResult(true),
            async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new PhraseResult(); },
            _ => Shows[Interlocked.Increment(ref Shown) - 1].TrySetResult(true),
            () => Hides[Interlocked.Increment(ref Hidden) - 1].TrySetResult(true)));
        private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static TriggerEffectAction Effect(EffectPhase phase) => new()
        {
            EffectType = EffectTypes.Speak, EffectId = "band", Phase = phase,
            SpeakTarget = "yes", SpeakCueMode = SpeakCueMode.Persistent, SpeakHoldMode = SpeakHoldMode.KeepPlaying
        };
    }

    [Fact]
    public async Task ReplacementFinishesOldSessionBeforeShowingNewAndStopCleansUp()
    {
        var f = new Fixture();
        var dispatcher = f.Dispatcher();
        await dispatcher.DispatchAsync(Fixture.Effect(EffectPhase.Start), f.Context);
        await f.Shows[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DispatchAsync(Fixture.Effect(EffectPhase.Start), f.Context);
        await f.Shows[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(f.Hides[0].Task.IsCompleted);
        Assert.Equal(1, f.Restored);
        await dispatcher.DispatchAsync(Fixture.Effect(EffectPhase.Stop), f.Context);
        await f.Hides[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Restores[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, f.Borrowed);
        Assert.Equal(2, f.Restored);
    }

    [Theory]
    [InlineData(EffectPhase.Start)]
    [InlineData(EffectPhase.OneShot)]
    public async Task RunCancellationStopsBandAndOneShot(EffectPhase phase)
    {
        var f = new Fixture();
        var dispatcher = f.Dispatcher();
        using var stop = new CancellationTokenSource();
        await dispatcher.DispatchAsync(Fixture.Effect(phase), f.Context, stop.Token);
        await f.Shows[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await f.Hides[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Restores[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, f.Restored);
    }
    [Fact]
    public async Task StartAfterStopWaitsForPriorCaptureToRelease()
    {
        var f = new Fixture();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int created = 0;
        var dispatcher = new SpeakActionDispatcher((effect, source) =>
        {
            int index = Interlocked.Increment(ref created) - 1;
            return new SpeakPromptSession(effect, source, () => true, () => Task.FromResult(true),
                () => true, () => { }, async (_, ct) =>
                {
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException) { if (index == 0) await release.Task; throw; }
                    return new PhraseResult();
                }, _ => f.Shows[index].TrySetResult(true), () => f.Hides[index].TrySetResult(true));
        });
        await dispatcher.DispatchAsync(Fixture.Effect(EffectPhase.Start), f.Context);
        await f.Shows[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DispatchAsync(Fixture.Effect(EffectPhase.Stop), f.Context);
        await dispatcher.DispatchAsync(Fixture.Effect(EffectPhase.Start), f.Context);
        await Task.WhenAny(f.Shows[1].Task, Task.Delay(200));
        Assert.False(f.Shows[1].Task.IsCompleted);
        release.SetResult(true);
        await f.Shows[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(f.Hides[0].Task.IsCompleted);
        await dispatcher.DispatchAsync(Fixture.Effect(EffectPhase.Stop), f.Context);
        await f.Hides[1].Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EngineBandEntryAndExitDriveCueLifecycle()
    {
        var f = new Fixture();
        var enhancement = new Enhancement();
        enhancement.TimelineItems.Add(new TimelineItem
        {
            Kind = TimelineItemKind.Effect, Start = 1, Duration = 1, EffectType = EffectTypes.Speak,
            EffectSpeakTarget = "yes", EffectSpeakCueMode = SpeakCueMode.Persistent,
            EffectSpeakHoldMode = SpeakHoldMode.KeepPlaying
        });
        using var engine = new EnhancementEngine(enhancement, f.Source, f.Dispatcher());
        engine.Start();
        f.Source.Advance(1.2);
        await f.Shows[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Source.Advance(2.1);
        await f.Hides[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Restores[0].Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, f.Borrowed);
        Assert.Equal(1, f.Restored);
    }

}
