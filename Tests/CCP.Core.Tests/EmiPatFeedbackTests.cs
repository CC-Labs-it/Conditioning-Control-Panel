using System;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiPatFeedbackTests
{
    [Fact]
    public void PokesEarnGleeAnnoyanceRageThenAMinuteOfTruce()
    {
        var ladder = new EmiPokeLadder();
        var now = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(EmiPokeStep.Pat, ladder.Note(now));
        Assert.Equal(EmiPokeStep.Pat, ladder.Note(now.AddSeconds(1)));
        Assert.Equal(EmiPokeStep.Glee, ladder.Note(now.AddSeconds(2)));
        Assert.Equal(EmiPokeStep.Annoyed, ladder.Note(now.AddSeconds(3)));
        Assert.Equal(EmiPokeStep.Rage, ladder.Note(now.AddSeconds(4)));
        ladder.Reset(); // dismiss forgets the run, but preserves the truce
        Assert.True(ladder.InTruce(now.AddSeconds(63.999)));
        Assert.Equal(EmiPokeStep.Pat, ladder.Note(now.AddSeconds(63)));
        Assert.False(ladder.InTruce(now.AddSeconds(64)));
        Assert.Equal(EmiPokeStep.Pat, ladder.Note(now.AddSeconds(64)));
    }

    [Fact]
    public void FourSecondWindowIncludesTheBoundaryAndThenExpires()
    {
        var ladder = new EmiPokeLadder();
        var now = new DateTime(2026, 10, 14);
        ladder.Note(now);
        ladder.Note(now.AddMilliseconds(4000));
        Assert.Equal(2, ladder.Count);
        ladder.Note(now.AddMilliseconds(8001));
        Assert.Equal(1, ladder.Count);
    }

    [Fact]
    public void FlickAndRageHoldEachAuthoredFrameUntilItsBoundary()
    {
        Assert.Equal("^_~", EmiPatFeedback.FaceAt(EmiPatFeedback.Flick, 319));
        Assert.Equal("0_0", EmiPatFeedback.FaceAt(EmiPatFeedback.Flick, 320));
        Assert.Null(EmiPatFeedback.FaceAt(EmiPatFeedback.Flick, 500));
        Assert.Equal(">.<", EmiPatFeedback.FaceAt(EmiPatFeedback.Rage, 199));
        Assert.Equal(">_<", EmiPatFeedback.FaceAt(EmiPatFeedback.Rage, 200));
        Assert.Equal(">:(", EmiPatFeedback.FaceAt(EmiPatFeedback.Rage, 600));
        Assert.Null(EmiPatFeedback.FaceAt(EmiPatFeedback.Rage, 2100));
    }
}
