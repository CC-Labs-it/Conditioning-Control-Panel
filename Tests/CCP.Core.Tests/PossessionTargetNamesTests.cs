using ConditioningControlPanel.Services.Possession;
using Xunit;
namespace CCP.Core.Tests;
public sealed class PossessionTargetNamesTests
{
    [Theory]
    [InlineData(PossessionRole.Button, " Start\n now ", "the Start now button")]
    [InlineData(PossessionRole.Toggle, "Sound", "the Sound toggle")]
    [InlineData(PossessionRole.Title, "", "the title")]
    [InlineData(PossessionRole.Timer, null, "the timer")]
    public void NamesMatchTheWpfBarkGrammar(PossessionRole role, string? content, string expected) =>
        Assert.Equal(expected, PossessionTargetNames.For(role, null, content));
    [Fact]
    public void ExplicitNameWinsAndUnspeakableCandidatesFallThrough()
    {
        Assert.Equal("the bank", PossessionTargetNames.For(PossessionRole.Progress, "the bank", "ignored"));
        Assert.Equal("the useful button", PossessionTargetNames.For(PossessionRole.Button, null, "x", new string('a', 33), " useful "));
    }
}
