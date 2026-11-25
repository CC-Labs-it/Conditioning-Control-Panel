using System;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1705: video quests say which videos count.</summary>
public class QuestVideoCopyTests
{
    private static string Loc(string key) => key switch
    {
        QuestVideoCopy.ShortKey => "App videos only, not the browser.",
        QuestVideoCopy.FullKey => "full",
        _ => key,
    };

    [Fact]
    public void A_video_quest_carries_the_hint()
    {
        Assert.Equal("Watch 12 minutes of video. App videos only, not the browser.",
            QuestVideoCopy.Describe(QuestCategory.Video, "Watch 12 minutes of video", Loc));
        Assert.Equal("full", QuestVideoCopy.Tooltip(QuestCategory.Video, Loc));
    }

    [Fact]
    public void Other_quests_are_unchanged()
    {
        foreach (QuestCategory c in Enum.GetValues(typeof(QuestCategory)))
        {
            if (c == QuestCategory.Video) continue;
            Assert.Equal("Pop 50 bubbles", QuestVideoCopy.Describe(c, "Pop 50 bubbles", Loc));
            Assert.Null(QuestVideoCopy.Tooltip(c, Loc));
        }
    }

    [Fact]
    public void A_missing_key_adds_nothing()
    {
        Assert.Equal("Watch", QuestVideoCopy.Describe(QuestCategory.Video, "Watch", k => k));
        Assert.Null(QuestVideoCopy.Tooltip(QuestCategory.Video, k => k));
    }

    [Fact]
    public void The_english_copy_names_every_player_that_feeds_the_counter()
    {
        string? en = Path.Combine(SourceRoots.LanguagesDirectory, "en.json");
        Assert.NotNull(en);
        var full = (string?)JObject.Parse(File.ReadAllText(en!))[QuestVideoCopy.FullKey];
        Assert.NotNull(full);
        // AchievementService.TrackVideoWatched callers: VideoService (mandatory, Takeover),
        // FloatingInstances, BubbleCountWindow. The browser never reaches it.
        foreach (var word in new[] { "mandatory", "floating", "Bubble Count", "browser" })
            Assert.Contains(word, full!, StringComparison.OrdinalIgnoreCase);
    }
}
