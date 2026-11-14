using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class QuestDestinationsCoreTests
{
    [Fact]
    public void EveryCategoryHasExactlyOneDisposition()
    {
        foreach (var category in Enum.GetValues<QuestCategory>())
            Assert.True(QuestDestinations.Map.ContainsKey(category) ^ QuestDestinations.NoHome.Contains(category), category.ToString());
    }

    [Fact]
    public void DestinationsOnlyNavigate()
    {
        foreach (var (category, destination) in QuestDestinations.Map)
        {
            var entry = destination.ToPaletteEntry(category);
            Assert.Equal(destination.TabKey, entry.TabKey);
            Assert.Null(entry.GameId);
            Assert.Null(entry.LauncherKey);
            Assert.False(entry.OpensLauncher);
        }
        Assert.Equal("lockdown", QuestDestinations.For(QuestCategory.Lockdown)!.TabKey);
        Assert.Equal("bubbles", QuestDestinations.For(QuestCategory.Bubbles)!.RackKey);
        Assert.Null(QuestDestinations.For(QuestCategory.Streak));
    }
}
