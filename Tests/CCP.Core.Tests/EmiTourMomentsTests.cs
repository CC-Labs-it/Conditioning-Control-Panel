using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class EmiTourMomentsTests
{
    // WPF EmiTourNarrator.cs:159-168: a step's own pool when it exists, else the shared fallback.
    [Theory]
    [InlineData("welcome", "tour.welcome")]
    [InlineData("avatar_intro", "tourStep")]
    [InlineData("Welcome", "tourStep")]
    [InlineData(" ", null)]
    [InlineData(null, null)]
    public void StepPicksItsOwnMomentOrTheFallback(string? stepId, string? expected) =>
        Assert.Equal(expected, EmiTourMoments.Step(stepId, new[] { "tourStarted", "tour.welcome", "tourStep" }));

    [Fact]
    public void EndingNamesFinishedOrSkipped()
    {
        Assert.Equal("tourFinished", EmiTourMoments.Ending(true));
        Assert.Equal("tourSkipped", EmiTourMoments.Ending(false));
        Assert.Equal("tourStarted", EmiTourMoments.Started);
    }
}
