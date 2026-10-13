using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The playback/dispatch contracts both heads share: PlaybackRect replaces
/// System.Windows.Rect, and the dry-run / recording dispatchers keep their log rules.</summary>
public class DeeperDispatchContractTests
{
    [Fact]
    public void PlaybackRect_EmptyIsEmpty_ZeroSizeIsNot()
    {
        Assert.True(PlaybackRect.Empty.IsEmpty);
        // WPF Rect semantics: a laid-out 0x0 rect is not Empty; only negative extents are.
        Assert.False(new PlaybackRect(10, 20, 0, 0).IsEmpty);
        Assert.False(new PlaybackRect(0, 0, 1920, 1080).IsEmpty);
    }

    [Fact]
    public async Task Logging_RecordsDescribedLine_AndRaisesEvent()
    {
        var d = new LoggingActionDispatcher();
        string? raised = null;
        d.ActionLogged += l => raised = l;

        await d.DispatchAsync(new SeekAction { Target = SeekTargets.Time, Time = 12.5 }, Ctx(3.25));

        Assert.Equal("t=3.25s  seek → 12.50s", Assert.Single(d.RecentActions));
        Assert.Equal(d.RecentActions[0], raised);
    }

    [Fact]
    public async Task Logging_DropsRampUpdates_AndCancelledDispatches()
    {
        var d = new LoggingActionDispatcher();
        await d.DispatchAsync(new TriggerEffectAction { Phase = EffectPhase.Update }, Ctx(1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await d.DispatchAsync(new PauseAction(), Ctx(1), cts.Token);

        Assert.Empty(d.RecentActions);
    }

    [Fact]
    public async Task Logging_CapsRecentAtFifty_OldestDropped()
    {
        var d = new LoggingActionDispatcher();
        for (var i = 0; i < 55; i++)
            await d.DispatchAsync(new PauseAction(), Ctx(i));

        Assert.Equal(50, d.RecentActions.Count);
        Assert.StartsWith("t=5.00s", d.RecentActions[0]);
        d.Clear();
        Assert.Empty(d.RecentActions);
    }

    [Fact]
    public async Task Recording_ForwardsUpdatesUnrecorded_AndRecordsEvenWhenInnerThrows()
    {
        var inner = new ThrowingInner();
        var d = new RecordingActionDispatcher(inner);

        await d.DispatchAsync(new TriggerEffectAction { Phase = EffectPhase.Update }, Ctx(1));
        Assert.Single(inner.Seen);
        Assert.Empty(d.RecentActions);

        inner.Throw = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => d.DispatchAsync(new PauseAction(), Ctx(2)));
        Assert.Equal("t=2.00s  pause", Assert.Single(d.RecentActions));
    }

    [Fact]
    public async Task Logging_DescribesHapticAndEffectLines_WithPhaseSuffix()
    {
        var d = new LoggingActionDispatcher();
        await d.DispatchAsync(new TriggerHapticAction { PatternName = "pulse", Intensity = 0.5, DurationMs = 800, Phase = EffectPhase.Start }, Ctx(1));
        await d.DispatchAsync(new TriggerHapticAction { Intensity = 1, DurationMs = 200, Phase = EffectPhase.Restart }, Ctx(1));
        await d.DispatchAsync(new TriggerEffectAction { EffectType = EffectTypes.Flash, DurationMs = 300, Phase = EffectPhase.Stop }, Ctx(1));
        await d.DispatchAsync(new TriggerEffectAction { EffectType = EffectTypes.Subliminal, Text = "hi", DurationMs = 400 }, Ctx(1));

        Assert.Equal(new[]
        {
            "t=1.00s  haptic pulse @ 0.50 for 800ms [start]",
            "t=1.00s  haptic custom @ 1.00 for 200ms [restart 200ms]",
            "t=1.00s  effect flash random for 300ms [stop]",
            "t=1.00s  effect subliminal \"hi\" for 400ms",
        }, d.RecentActions);
    }

    [Fact]
    public async Task Recording_SkipsCancelledDispatch_InnerNotCalled()
    {
        var inner = new ThrowingInner();
        var d = new RecordingActionDispatcher(inner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await d.DispatchAsync(new PauseAction(), Ctx(1), cts.Token);

        Assert.Empty(inner.Seen);
        Assert.Empty(d.RecentActions);
    }

    private static EnhancementDispatchContext Ctx(double t) =>
        new(new Enhancement(), new AudioOnlySource(), t, null);

    private sealed class ThrowingInner : IActionDispatcher
    {
        public bool Throw;
        public List<EnhancementAction> Seen { get; } = new();
        public Task DispatchAsync(EnhancementAction action, EnhancementDispatchContext ctx, CancellationToken ct = default)
        {
            Seen.Add(action);
            return Throw ? throw new InvalidOperationException("device gone") : Task.CompletedTask;
        }
    }

    private sealed class AudioOnlySource : IPlaybackTimeSource
    {
        public event Action<double>? PlaybackTimeChanged { add { } remove { } }
        public double GetCurrentTimeSeconds() => 0;
        public double GetDurationSeconds() => 0;
        public bool IsPlaying => false;
        public void Seek(double seconds) { }
        public void Pause() { }
        public void Play() { }
        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;
    }
}
