using System;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class VatFaucetChargePolicyTests
{
    [Theory]
    [InlineData(0, false, 700)]
    [InlineData(400, false, 800)]
    [InlineData(2800, false, 1400)]
    [InlineData(10000, false, 1400)]
    [InlineData(400, true, 480)]
    [InlineData(10000, true, 840)]
    public void BudgetScalesWithHeldXpAndTheCompactCut(int held, bool compact, double expected)
        => Assert.Equal(expected, VatFaucetChargePolicy.BudgetMilliseconds(held, compact));

    [Fact]
    public void CompactStartsAfterThreeCompletedPoursAndResetsAtUtcMidnight()
    {
        var policy = new VatFaucetChargePolicy();
        var day = new DateTime(2026, 10, 14, 23, 59, 59, DateTimeKind.Utc);
        for (int i = 0; i < 3; i++) { Assert.False(policy.IsCompact(day)); policy.NotePour(day); }
        Assert.True(policy.IsCompact(day));
        Assert.Equal(3, policy.PoursToday(day));
        Assert.False(policy.IsCompact(day.AddSeconds(1)));
        Assert.Equal(0, policy.PoursToday(day.AddSeconds(1)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(.17, 2)]
    [InlineData(.5, 4)]
    [InlineData(.99, 6)]
    [InlineData(1, 6)]
    public void LadderAdvancesWithTheRingAndNeverAddsASeventhRung(double progress, int expected)
        => Assert.Equal(expected, VatFaucetChargePolicy.RungsAt(progress));
}
