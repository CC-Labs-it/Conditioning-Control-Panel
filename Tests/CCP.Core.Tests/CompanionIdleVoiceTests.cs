using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public class CompanionIdleVoiceTests
{
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(false, false, false, true)]
    public void VoiceRule(bool idle, bool running, bool optedIn, bool expected) =>
        Assert.Equal(expected, CompanionIdleVoice.ShouldVoice(idle, running, optedIn));
}
