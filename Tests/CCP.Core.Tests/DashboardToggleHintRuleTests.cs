using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DashboardToggleHintRuleTests
{
    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(50, false)]
    public void CaptionRetiresAtTheWpfThreshold(int uses, bool shown)
        => Assert.Equal(shown, DashboardToggleHintRule.ShouldShow(uses));

    [Fact]
    public void ThreeGesturesRetireTheLessonAndLaterGesturesDoNotChangeTheCounter()
    {
        var settings = new AppSettings();
        for (int i = 1; i <= 3; i++)
        {
            Assert.True(DashboardToggleHintRule.NoteUse(settings));
            Assert.Equal(i, settings.DashboardToggleHintUses);
        }
        Assert.False(DashboardToggleHintRule.ShouldShow(settings.DashboardToggleHintUses));
        for (int i = 0; i < 10; i++) Assert.False(DashboardToggleHintRule.NoteUse(settings));
        Assert.Equal(3, settings.DashboardToggleHintUses);
    }
}
