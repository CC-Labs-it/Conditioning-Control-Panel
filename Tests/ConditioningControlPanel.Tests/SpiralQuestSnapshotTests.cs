using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class SpiralQuestSnapshotTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");

    private static (ActiveQuest? Quest, QuestDefinition? Definition) Slot(
        string id, int progress = 0, int target = 10, bool completed = false) =>
        (new ActiveQuest(id) { CurrentProgress = progress, IsCompleted = completed },
         new QuestDefinition { Id = id, Name = "Quest " + id, TargetValue = target });

    [Fact]
    public void CarriesEveryAssignedSlotAndWeeklyWithRealValues()
    {
        var daily = new[] { Slot("first", 4), Slot("second", 10, completed: true), Slot("third", 0) };
        var weekly = Slot("week", 13, 70);
        var snapshot = SpiralQuestSnapshot.Build(daily, weekly.Quest, weekly.Definition, Now);
        var quests = (JArray)snapshot["quests"]!;
        Assert.Equal("2026-11-12", (string?)snapshot["date"]);
        Assert.Equal(4, quests.Count);
        Assert.Equal(new[] { "first", "second", "third", "week" }, quests.Select(q => (string?)q["id"]));
        Assert.Equal(4, (int)quests[0]["progress"]!);
        Assert.True((bool)quests[1]["completed"]!);
        Assert.Equal("daily", (string?)quests[2]["kind"]);
        Assert.Equal("weekly", (string?)quests[3]["kind"]);
        Assert.Equal(13, (int)quests[3]["progress"]!);
        Assert.Equal(70, (int)quests[3]["target"]!);
        Assert.All(quests, q => Assert.Equal(new[] { "id", "name", "kind", "progress", "target", "completed" },
            ((JObject)q).Properties().Select(p => p.Name)));
    }

    [Fact]
    public void CompletionFlagIsNotInventedFromProgressAndCaptureDoesNotMutate()
    {
        var slot = Slot("over", 25, 10, completed: false);
        var snapshot = SpiralQuestSnapshot.Build(new[] { slot }, null, null, Now);
        var row = snapshot["quests"]![0]!;
        Assert.Equal(25, (int)row["progress"]!);
        Assert.False((bool)row["completed"]!);
        Assert.Equal(25, slot.Quest!.CurrentProgress);
        Assert.False(slot.Quest.IsCompleted);
    }

    [Fact]
    public void MissingOrInvalidAssignmentsAreOmittedInsteadOfInvented()
    {
        var mismatch = Slot("wrong");
        mismatch.Definition!.Id = "another";
        var daily = new List<(ActiveQuest?, QuestDefinition?)>
        {
            (null, null), (new ActiveQuest("unknown"), null), mismatch,
            Slot("negative", -1), Slot("zero-target", 0, 0), Slot("good", 0), Slot("good", 0)
        };
        var snapshot = SpiralQuestSnapshot.Build(daily, null, null, Now);
        Assert.Equal("good", (string?)Assert.Single((JArray)snapshot["quests"]!)["id"]);
        Assert.Null(SpiralQuestSnapshot.Capture(null));
    }

    [Fact]
    public void NamesComeFromTheHostsLocalizationAndPayloadIsBounded()
    {
        var slots = Enumerable.Range(0, 30).Select(n => Slot("quest" + n));
        var snapshot = SpiralQuestSnapshot.Build(slots, null, null, Now, _ => "Nome localizzato");
        var quests = (JArray)snapshot["quests"]!;
        Assert.Equal(SpiralQuestSnapshot.MaxQuests, quests.Count);
        Assert.All(quests, q => Assert.Equal("Nome localizzato", (string?)q["name"]));
    }

    [Theory]
    [InlineData("2026-11-12T00:30:00+02:00", "2026-11-11")]
    [InlineData("2026-11-11T23:30:00-05:00", "2026-11-12")]
    [InlineData("2026-11-12T00:00:00Z", "2026-11-12")]
    public void SnapshotAndNewActivityUseTheSameUtcDayAcrossLocalMidnight(string instant, string expected)
    {
        var now = DateTimeOffset.Parse(instant);
        var snapshot = SpiralQuestSnapshot.Build(Array.Empty<(ActiveQuest?, QuestDefinition?)>(), null, null, now);
        Assert.Equal(expected, (string?)snapshot["date"]);
        var day = FeatureDayLogService.ActivityDay(now);
        Assert.Equal(expected, day.ToString("yyyy-MM-dd"));
        Assert.Equal(DateTimeKind.Utc, day.Kind);
    }
}
