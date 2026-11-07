using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

public sealed class OnlineMeterPolicyTests
{
    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"true\"", false)]
    [InlineData("1", false)]
    [InlineData("null", false)]
    public void Official_requires_an_explicit_boolean_true(string value, bool official)
        => Assert.Equal(official, ScrolllerPolicyService.Parse("{\"official\":" + value + "}")!.Official);
}
