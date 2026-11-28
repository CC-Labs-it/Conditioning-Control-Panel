using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Core.Tests;

public class QuestVideoCopyPortTests
{
    [Fact]
    public void VideoCopyUsesHintsAndOtherCategoriesDoNot()
    {
        string Loc(string key) => key == QuestVideoCopy.ShortKey ? "App only." : "Details";
        Assert.Equal("Watch. App only.", QuestVideoCopy.Describe(QuestCategory.Video, "Watch", Loc));
        Assert.Equal("Details", QuestVideoCopy.Tooltip(QuestCategory.Video, Loc));
        Assert.Equal("Watch", QuestVideoCopy.Describe(QuestCategory.Bubbles, "Watch", Loc));
        Assert.Null(QuestVideoCopy.Tooltip(QuestCategory.Bubbles, Loc));
        Assert.Equal("Watch", QuestVideoCopy.Describe(QuestCategory.Video, "Watch", key => key));
        Assert.Null(QuestVideoCopy.Tooltip(QuestCategory.Video, key => key));
    }
}
