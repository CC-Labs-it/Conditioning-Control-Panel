using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class TierOnNoticeTests
{
    [Theory]
    [InlineData(null, 2, false, false, 2)]
    [InlineData(0, 1, false, true, 1)]
    [InlineData(1, 2, false, true, 2)]
    [InlineData(2, 2, true, false, 2)]
    [InlineData(2, 0, false, false, 2)]
    [InlineData(2, 0, true, false, 0)]
    [InlineData(0, 99, true, true, 2)]
    [InlineData(0, -1, true, false, 0)]
    public void MatchesWpfRiseAndAuthoritativeDrop(int? previous, int current, bool serverAnswered, bool owed, int record)
    {
        var result = TierOnNotice.Decide(previous, current, serverAnswered);
        Assert.Equal(owed, result.Owed);
        Assert.Equal(record, result.Record);
    }
    [Fact]
    public void TitleMatchesTheTier() =>
        Assert.Equal(("tier_on_basic", "tier_on_prime"), (TierOnNotice.TitleKey(1), TierOnNotice.TitleKey(2)));
}
