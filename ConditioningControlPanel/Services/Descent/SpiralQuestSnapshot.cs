using System;
using System.Collections.Generic;
using System.Globalization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Descent;

/// <summary>Read-only current assignments. Never merge these into server descent history.</summary>
internal static class SpiralQuestSnapshot
{
    internal const int MaxQuests = 20;

    internal static JObject? Capture(QuestService? service)
    {
        if (service == null) return null;
        return Build(service.GetDailySlots(), service.Progress.WeeklyQuest,
            service.GetCurrentWeeklyDefinition(), DateTimeOffset.UtcNow, definition =>
            {
                var name = definition.LocalizedName;
                return name == $"quest_{definition.Id}_name" ? definition.Name : name;
            });
    }

    internal static JObject Build(
        IEnumerable<(ActiveQuest? Quest, QuestDefinition? Definition)> daily,
        ActiveQuest? weekly, QuestDefinition? weeklyDefinition, DateTimeOffset now,
        Func<QuestDefinition, string>? name = null)
    {
        var quests = new JArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(ActiveQuest? assignment, QuestDefinition? definition, string kind)
        {
            if (quests.Count >= MaxQuests || assignment == null || definition == null) return;
            var id = assignment.DefinitionId;
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id != definition.Id ||
                assignment.CurrentProgress < 0 || definition.TargetValue <= 0 ||
                !seen.Add(kind + ":" + id)) return;
            var label = (name?.Invoke(definition) ?? definition.Name ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(label)) label = id;
            if (label.Length > 240) label = label[..240];
            quests.Add(new JObject
            {
                ["id"] = id,
                ["name"] = label,
                ["kind"] = kind,
                ["progress"] = assignment.CurrentProgress,
                ["target"] = definition.TargetValue,
                ["completed"] = assignment.IsCompleted,
            });
        }

        foreach (var slot in daily)
        {
            Add(slot.Quest, slot.Definition, "daily");
            if (quests.Count >= MaxQuests) break;
        }
        Add(weekly, weeklyDefinition, "weekly");
        return new JObject
        {
            ["date"] = now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["quests"] = quests,
        };
    }
}
