using ConditioningControlPanel.Services.Account;
using Xunit;

namespace CCP.Core.Tests;

public sealed class NeedsDisplayNameRuleTests
{
    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(true, "", null, true)]
    [InlineData(true, "   ", null, true)]
    [InlineData(true, "Pixie", null, false)]
    [InlineData(true, "Pixie", false, false)]
    [InlineData(true, "Pixie", true, true)]
    [InlineData(true, null, false, false)]
    [InlineData(true, "", false, false)]
    [InlineData(true, "   ", false, false)]
    [InlineData(false, null, null, false)]
    [InlineData(false, "", true, false)]
    public void Server_answer_wins_both_ways(bool signedIn, string? name, bool? flag, bool expected)
        => Assert.Equal(expected, NeedsDisplayNameRule.ShouldAsk(signedIn, name, flag));
}
