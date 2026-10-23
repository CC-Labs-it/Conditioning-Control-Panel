using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services;

/// <summary>WPF ProfileSyncService's V2 stats / quest history and companion merge. No disk or UI ownership here;
/// the caller saves its stores. ponytail: only fields actually returned this session are echoed by SyncPush.</summary>
internal static class CloudSyncProgress
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string Day(DateTime d) => d.ToString("yyyy-MM-dd", Inv);

    internal static bool TryDay(string? value, out DateTime day)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", Inv, DateTimeStyles.None, out day))
        {
            if (!DateTimeOffset.TryParseExact(value, new[] { "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK" },
                Inv, DateTimeStyles.AssumeUniversal, out var stamp)) return false;
            day = stamp.Date; // the day as written, never converted to the machine's zone
        }
        day = day.Date;
        return day <= DateTime.Today.AddDays(1);
    }

    internal static Dictionary<string, object> Body(AppSettings s, AchievementProgress? p, QuestProgress? q)
    {
        var body = new Dictionary<string, object>();
        if (p != null)
        {
            body["completed_sessions"] = p.CompletedSessions.Count;
            body["longest_session_minutes"] = p.LongestSessionMinutes;
            body["highest_streak"] = s.HighestStreak;
            body["total_flashes"] = p.TotalFlashImages;
            body["consecutive_days"] = p.ConsecutiveDays;
            body["last_streak_date"] = p.LastLaunchDate.Date != default ? Day(p.LastLaunchDate) : "";
            body["total_bubbles_popped"] = p.TotalBubblesPopped;
            body["total_video_minutes"] = Math.Round(p.TotalVideoMinutes, 1);
            body["total_lock_cards_completed"] = p.TotalLockCardsCompleted;
            body["lifetime_points_spent"] = p.LifetimeSkillPointsSpent;
        }
        if (q != null)
        {
            body["daily_quest_streak"] = s.DailyQuestStreak;
            body["last_daily_quest_date"] = s.LastDailyQuestDate is { } date ? Day(date) : "";
            body["quest_completion_dates"] = q.DailyQuestCompletionDates.Select(Day).ToList();
            body["quest_completion_log"] = q.QuestCompletionLog
                .Where(e => e != null && !string.IsNullOrEmpty(e.D) && !string.IsNullOrEmpty(e.Q))
                .OrderBy(e => e.D, StringComparer.Ordinal).Reverse().Take(400).Reverse()
                .Select(e => new Dictionary<string, string> { ["d"] = e.D, ["q"] = e.Q }).ToList();
            body["total_daily_quests_completed"] = q.TotalDailyQuestsCompleted;
            body["total_weekly_quests_completed"] = q.TotalWeeklyQuestsCompleted;
            body["total_xp_from_quests"] = q.TotalXPFromQuests;
            body["daily_quests_completed_today"] = q.GetDailyQuestsCompletedToday();
            body["daily_completion_reset_date"] = q.DailyCompletionResetDate is { } reset ? Day(reset) : "";
        }
        return body;
    }

    internal static void QuestFlags(AppSettings s, JObject reply, QuestService? quests)
    {
        var forced = reply.Value<bool?>("force_streak_override") == true;
        if (reply.Value<bool?>("reset_weekly_quest") == true) quests?.ForceRegenerateWeeklyQuest();
        if (reply.Value<bool?>("reset_daily_quest") == true) quests?.ForceRegenerateDailyQuest();
        if (forced && reply["streak_stats"] is JObject correction)
        {
            s.DailyQuestStreak = correction.Value<int?>("daily_quest_streak") ?? 0;
            if (TryDay((string?)correction["last_daily_quest_date"], out var day)) s.LastDailyQuestDate = day;
            if (quests?.Progress is { } q)
            {
                if (correction["quest_completion_dates"] is JArray dates)
                    q.DailyQuestCompletionDates = dates.Values<string>().Where(d => TryDay(d, out _))
                        .Select(d => { TryDay(d, out var parsed); return parsed; }).ToList();
                q.TotalDailyQuestsCompleted = correction.Value<int?>("total_daily_quests_completed") ?? 0;
                q.TotalWeeklyQuestsCompleted = correction.Value<int?>("total_weekly_quests_completed") ?? 0;
                q.TotalXPFromQuests = correction.Value<int?>("total_xp_from_quests") ?? 0;
            }
        }
    }

    internal static void Adopt(AppSettings s, JObject reply, AchievementProgress? p, QuestService? quests)
    {
        var forced = reply.Value<bool?>("force_streak_override") == true;
        if (reply["user"]?["stats"] is JObject stats)
        {
            MaxStats(s, stats, p);
            if (!forced) MergeQuests(s, stats, quests);
            if (p != null)
            {
                s.CurrentStreak = p.ConsecutiveDays;
                s.LastStreakDate = p.LastLaunchDate;
            }
        }
        if (reply["user"]?["mobile_stats"] is JObject mobile)
        {
            // Display-only phone ledger: never echo these totals as desktop quest earnings.
            s.MobileQuestDailyCompleted = mobile.Value<int?>("total_daily_quests_completed") ?? 0;
            s.MobileQuestWeeklyCompleted = mobile.Value<int?>("total_weekly_quests_completed") ?? 0;
            s.MobileQuestXP = mobile.Value<int?>("total_xp_from_quests") ?? 0;
        }
        if (reply["total_conditioning_minutes"]?.Type is JTokenType.Integer or JTokenType.Float)
            s.TotalConditioningMinutes = Math.Max(s.TotalConditioningMinutes, reply.Value<double>("total_conditioning_minutes"));
        if (reply["companion_progress"] is JObject companions)
            foreach (var entry in companions.Properties())
            {
                if (!int.TryParse(entry.Name, out var id) || entry.Value is not JObject node) continue;
                var server = node.ToObject<CompanionProgress>()!;
                s.CompanionProgressData.TryGetValue(id, out var local);
                if (local == null || server.Level > local.Level || server.Level == local.Level && server.TotalXPEarned > local.TotalXPEarned)
                    s.CompanionProgressData[id] = server;
            }
        quests?.Save();
    }

    private static void MaxStats(AppSettings s, JObject stats, AchievementProgress? p)
    {
        if (p == null) return;
        if (stats["longest_session_minutes"] != null) p.LongestSessionMinutes = Math.Max(p.LongestSessionMinutes, stats.Value<double>("longest_session_minutes"));
        if (stats["total_flashes"] != null) p.TotalFlashImages = Math.Max(p.TotalFlashImages, stats.Value<int>("total_flashes"));
        if (stats["total_bubbles_popped"] != null) p.TotalBubblesPopped = Math.Max(p.TotalBubblesPopped, stats.Value<int>("total_bubbles_popped"));
        if (stats["total_video_minutes"] != null) p.TotalVideoMinutes = Math.Max(p.TotalVideoMinutes, stats.Value<double>("total_video_minutes"));
        if (stats["total_lock_cards_completed"] != null) p.TotalLockCardsCompleted = Math.Max(p.TotalLockCardsCompleted, stats.Value<int>("total_lock_cards_completed"));
        if (stats["highest_streak"] != null) s.HighestStreak = Math.Max(s.HighestStreak, stats.Value<int>("highest_streak"));
        if (stats["total_attention_checks_passed"] != null) p.TotalAttentionChecksPassed = Math.Max(p.TotalAttentionChecksPassed, stats.Value<int>("total_attention_checks_passed"));
        if (stats["video_attention_checks_passed"] != null) p.VideoAttentionChecksPassed = Math.Max(p.VideoAttentionChecksPassed, stats.Value<int>("video_attention_checks_passed"));
        if (stats["video_attention_checks_failed"] != null) p.VideoAttentionChecksFailed = Math.Max(p.VideoAttentionChecksFailed, stats.Value<int>("video_attention_checks_failed"));
        if (stats["total_attention_check_failures"] != null) p.AttentionCheckFailures = Math.Max(p.AttentionCheckFailures, stats.Value<int>("total_attention_check_failures"));
        if (stats["total_bubble_count_games"] != null) p.TotalBubbleCountGames = Math.Max(p.TotalBubbleCountGames, stats.Value<int>("total_bubble_count_games"));
        if (stats["total_bubble_count_correct"] != null) p.TotalBubbleCountCorrect = Math.Max(p.TotalBubbleCountCorrect, stats.Value<int>("total_bubble_count_correct"));
        if (stats["total_bubble_count_failed"] != null) p.TotalBubbleCountFailed = Math.Max(p.TotalBubbleCountFailed, stats.Value<int>("total_bubble_count_failed"));
        if (stats["bubble_count_best_streak"] != null) p.BubbleCountBestStreak = Math.Max(p.BubbleCountBestStreak, stats.Value<int>("bubble_count_best_streak"));
        if (stats["total_sessions_started"] != null) p.TotalSessionsStarted = Math.Max(p.TotalSessionsStarted, stats.Value<int>("total_sessions_started"));
        if (stats["total_sessions_abandoned"] != null) p.TotalSessionsAbandoned = Math.Max(p.TotalSessionsAbandoned, stats.Value<int>("total_sessions_abandoned"));
        if (stats["total_xp_earned"] != null) p.TotalXPEarned = Math.Max(p.TotalXPEarned, stats.Value<double>("total_xp_earned"));
        if (stats["total_skill_points_earned"] != null) p.TotalSkillPointsEarned = Math.Max(p.TotalSkillPointsEarned, stats.Value<int>("total_skill_points_earned"));
        if (stats["lifetime_points_spent"] != null) p.LifetimeSkillPointsSpent = Math.Max(p.LifetimeSkillPointsSpent, stats.Value<long>("lifetime_points_spent"));
        if (stats["total_pink_filter_minutes"] != null) p.TotalPinkFilterMinutes = Math.Max(p.TotalPinkFilterMinutes, stats.Value<double>("total_pink_filter_minutes"));
        if (stats["total_spiral_minutes"] != null) p.TotalSpiralMinutes = Math.Max(p.TotalSpiralMinutes, stats.Value<double>("total_spiral_minutes"));
        if (stats["consecutive_days"] != null)
        {
            DateTime? cloudDay = TryDay((string?)stats["last_streak_date"], out var day) ? day : null;
            var adopt = AchievementProgress.DecideLoginStreakAdopt(p.ConsecutiveDays, p.LastLaunchDate,
                stats.Value<int>("consecutive_days"), cloudDay, DateTime.Today);
            if (adopt is { } pair)
            {
                p.ConsecutiveDays = pair.Streak;
                if (pair.LastDate != default) p.LastLaunchDate = pair.LastDate;
            }
        }
    }

    private static void MergeQuests(AppSettings s, JObject stats, QuestService? quests)
    {
        var serverStreak = stats.Value<int?>("daily_quest_streak") ?? 0;
        s.DailyQuestStreak = Math.Max(s.DailyQuestStreak, serverStreak);
        if (TryDay((string?)stats["last_daily_quest_date"], out var last) && (s.LastDailyQuestDate == null || last > s.LastDailyQuestDate))
            s.LastDailyQuestDate = last;
        if (quests?.Progress is not { } q) return;
        if (stats["quest_completion_dates"] is JArray dates)
        {
            var added = false;
            foreach (var value in dates.Values<string>())
                if (TryDay(value, out var day) && !q.DailyQuestCompletionDates.Any(d => d.Date == day))
                { q.DailyQuestCompletionDates.Add(day); added = true; }
            if (added)
            {
                q.DailyQuestCompletionDates.RemoveAll(d => d.Date < DateTime.Today.AddDays(-90));
                quests.RecalculateStreak();
                s.DailyQuestStreak = Math.Max(s.DailyQuestStreak, serverStreak);
            }
        }
        if (stats["quest_completion_log"] is JArray log)
        {
            var cutoff = Day(DateTime.Today.AddDays(-90));
            var seen = q.QuestCompletionLog.Select(e => e.D + "|" + e.Q).ToHashSet(StringComparer.Ordinal);
            var added = false;
            foreach (var entry in log.OfType<JObject>())
            {
                var d = (string?)entry["d"]; var id = (string?)entry["q"];
                if (string.IsNullOrEmpty(d) || string.IsNullOrEmpty(id) || string.CompareOrdinal(d, cutoff) < 0 || !seen.Add(d + "|" + id)) continue;
                q.QuestCompletionLog.Add(new QuestLogEntry(d, id)); added = true;
            }
            if (added) q.QuestCompletionLog.RemoveAll(e => string.CompareOrdinal(e.D, cutoff) < 0);
        }
        q.TotalDailyQuestsCompleted = Math.Max(q.TotalDailyQuestsCompleted, stats.Value<int?>("total_daily_quests_completed") ?? 0);
        q.TotalWeeklyQuestsCompleted = Math.Max(q.TotalWeeklyQuestsCompleted, stats.Value<int?>("total_weekly_quests_completed") ?? 0);
        q.TotalXPFromQuests = Math.Max(q.TotalXPFromQuests, stats.Value<int?>("total_xp_from_quests") ?? 0);
        var count = stats.Value<int?>("daily_quests_completed_today") ?? 0;
        var evidence = q.DailyQuestCompletionDates.Any(d => d.Date == DateTime.Today);
        if (evidence && TryDay((string?)stats["daily_completion_reset_date"], out var reset) && reset == DateTime.Today && count > q.GetDailyQuestsCompletedToday())
        { q.DailyQuestsCompletedToday = count; q.DailyCompletionResetDate = DateTime.Today; }
        if (evidence && q.GetDailyQuestsCompletedToday() == 0)
        { q.DailyQuestsCompletedToday = 1; q.DailyCompletionResetDate = DateTime.Today; }
    }
}
