using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SpiralWithholdRulesTests
{
    [Theory]
    [InlineData(false, false, null, false, false, false)]
    [InlineData(true, false, null, false, false, true)]
    [InlineData(false, false, null, true, false, true)]
    [InlineData(false, false, null, false, true, true)]
    [InlineData(true, true, null, true, true, false)]
    [InlineData(true, false, "restore", true, true, false)]
    [InlineData(true, false, "cycle", true, true, false)]
    [InlineData(true, false, "RESTORE", false, false, true)]
    [InlineData(true, false, "invalid", false, false, true)]
    public void AnswerWinsOverEveryOutstandingOffer(bool offered, bool completed, string? pending,
        bool offerInHand, bool ceremonyOpen, bool expected)
    {
        var settings = new AppSettings
        {
            DescentMigrationOffered = offered, DescentMigrationCompleted = completed,
            PendingDescentMigrationChoice = pending
        };
        Assert.Equal(expected, SpiralRoom.SpiralWithheldFor(settings, offerInHand, ceremonyOpen));
    }

    [Fact]
    public void MissingSettingsWithholdsNothing() => Assert.False(SpiralRoom.SpiralWithheldFor(null, true, true));
}
