using System;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Speech;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The speak effect's rules, moved out of WPF's SpeakPromptSession (deeper-core-e).</summary>
public sealed class SpeakPromptPlanTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static SpeakPromptPlan Plan(Action<TriggerEffectAction>? set = null)
    {
        var a = new TriggerEffectAction { EffectType = EffectTypes.Speak, SpeakTarget = "  yes  " };
        set?.Invoke(a);
        return new SpeakPromptPlan(a, Now);
    }

    [Fact]
    public void DefaultsAndClampsMatchTheWpfSession()
    {
        var p = Plan(a => { a.SpeakCueIntervalMs = 10; a.SpeakRequiredReps = 9; });

        Assert.Equal("yes", p.Target);
        Assert.Equal("Say yes", p.Cue);
        Assert.Equal(80, p.CueIntervalMs);
        Assert.Equal(5, p.RequiredReps);
        Assert.StartsWith("that's it, ", p.CorrectMessage);
        Assert.Equal("try again", p.IncorrectMessage);

        Assert.Equal("Say it", Plan(a => a.SpeakTarget = " ").Cue);
        Assert.Equal(1, Plan(a => a.SpeakRequiredReps = 0).RequiredReps);
        Assert.Equal("louder", Plan(a => a.SpeakIncorrectMessage = " louder ").IncorrectMessage);
    }

    [Fact]
    public void DeadlineIsTheBandWidthForDurationThirtySecondsWithNoRegionElseOpen()
    {
        var duration = Plan(a => { a.SpeakCompletion = SpeakCompletion.Duration; a.SpeakRegionStartSec = 10; a.SpeakRegionEndSec = 14; });
        Assert.Equal(Now.AddSeconds(4), duration.HardDeadline);

        var tiny = Plan(a => { a.SpeakCompletion = SpeakCompletion.Duration; a.SpeakRegionStartSec = 10; a.SpeakRegionEndSec = 10.1; });
        Assert.Equal(Now.AddMilliseconds(500), tiny.HardDeadline);

        Assert.Equal(Now.AddSeconds(30), Plan().HardDeadline);

        var held = Plan(a => { a.SpeakRegionStartSec = 10; a.SpeakRegionEndSec = 14; });
        Assert.Null(held.HardDeadline);
    }

    [Fact]
    public void HoldLoopsOrPausesOnlyNearTheRegionEnd()
    {
        var loop = Plan(a => { a.SpeakRegionStartSec = 10; a.SpeakRegionEndSec = 14; });
        Assert.Equal(SpeakHoldStep.None, loop.HoldStepAt(13.6, paused: false));
        Assert.Equal(SpeakHoldStep.SeekToRegionStart, loop.HoldStepAt(13.8, paused: false));

        var pause = Plan(a => { a.SpeakHoldMode = SpeakHoldMode.Pause; a.SpeakRegionStartSec = 10; a.SpeakRegionEndSec = 14; });
        Assert.Equal(SpeakHoldStep.Pause, pause.HoldStepAt(13.9, paused: false));
        Assert.Equal(SpeakHoldStep.None, pause.HoldStepAt(13.9, paused: true));
        Assert.False(pause.PausesAtCue);

        var keep = Plan(a => { a.SpeakHoldMode = SpeakHoldMode.KeepPlaying; a.SpeakRegionStartSec = 10; a.SpeakRegionEndSec = 14; });
        Assert.False(keep.HoldsPlayback);
        Assert.Equal(SpeakHoldStep.None, keep.HoldStepAt(14, paused: false));

        var duration = Plan(a => { a.SpeakCompletion = SpeakCompletion.Duration; a.SpeakRegionStartSec = 10; a.SpeakRegionEndSec = 14; });
        Assert.Equal(SpeakHoldStep.None, duration.HoldStepAt(14, paused: false));
    }

    [Fact]
    public void RuleFiredPauseHoldsAtCueTimeButARuleFiredLoopCannot()
    {
        // #1221: a rule-fired speak has no region end, so "Pause the video" pauses at the cue.
        Assert.True(Plan(a => a.SpeakHoldMode = SpeakHoldMode.Pause).PausesAtCue);
        Assert.False(Plan(a => a.SpeakHoldMode = SpeakHoldMode.LoopRegion).PausesAtCue);
        Assert.Equal(SpeakHoldStep.None, Plan().HoldStepAt(100, paused: false));
    }

    [Fact]
    public void ListenResultsClassify()
    {
        Assert.Equal(SpeakHeard.Retry, SpeakPromptPlan.Classify(PhraseResult.NotAvailable));
        Assert.Equal(SpeakHeard.Correct, SpeakPromptPlan.Classify(new PhraseResult { Matched = true, Transcript = "yes" }));
        Assert.Equal(SpeakHeard.Silence, SpeakPromptPlan.Classify(new PhraseResult { TimedOut = true, Transcript = " " }));
        Assert.Equal(SpeakHeard.Incorrect, SpeakPromptPlan.Classify(new PhraseResult { TimedOut = true, Transcript = "no" }));
        Assert.Equal(SpeakHeard.Incorrect, SpeakPromptPlan.Classify(new PhraseResult { Transcript = "no" }));
    }
}
