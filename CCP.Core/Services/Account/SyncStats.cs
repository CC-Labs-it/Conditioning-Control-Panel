using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>The <c>/v2/user/sync</c> lifetime stats block both ways (WPF ProfileSyncService.cs:1574-1610,3365-3420,
    /// 3448-3730). The login streak pair (consecutive_days, last_streak_date) is adopted by the head's login streak
    /// path, not here.</summary>
    internal static class SyncStats
    {
        private const int LogCap = 400;   // WPF QuestCompletionLogWireCap and FeatureDayLogWireCap
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string Day(DateTime d) => d.ToString("yyyy-MM-dd", Inv);

        /// <summary>WPF ProfileSyncService.cs:1574-1610: the outbound stats, keys in WPF order.</summary>
        internal static Dictionary<string, object> Build(AppSettings s, AchievementProgress a, QuestProgress? q,
            List<Dictionary<string, object>>? dayLog) => new()
        {
            ["completed_sessions"] = a.CompletedSessions?.Count ?? 0,
            ["longest_session_minutes"] = a.LongestSessionMinutes,
            ["highest_streak"] = s.HighestStreak,
            ["total_flashes"] = a.TotalFlashImages,
            ["consecutive_days"] = a.ConsecutiveDays,
            ["last_streak_date"] = a.LastLaunchDate.Date != default ? Day(a.LastLaunchDate) : "",
            ["total_bubbles_popped"] = a.TotalBubblesPopped,
            ["total_video_minutes"] = Math.Round(a.TotalVideoMinutes, 1),
            ["total_lock_cards_completed"] = a.TotalLockCardsCompleted,
            ["lifetime_points_spent"] = a.LifetimeSkillPointsSpent,
            ["daily_quest_streak"] = s.DailyQuestStreak,
            ["last_daily_quest_date"] = s.LastDailyQuestDate is { } last ? Day(last) : "",
            ["quest_completion_dates"] = q?.DailyQuestCompletionDates?.Select(Day).ToList() ?? new List<string>(),
            ["quest_completion_log"] = (q?.QuestCompletionLog ?? new List<QuestLogEntry>())
                .Where(e => e != null && !string.IsNullOrEmpty(e.D) && !string.IsNullOrEmpty(e.Q))
                .OrderBy(e => e.D, StringComparer.Ordinal).Reverse().Take(LogCap).Reverse()
                .Select(e => new Dictionary<string, string> { ["d"] = e.D, ["q"] = e.Q }).ToList(),
            ["feature_day_log"] = dayLog ?? new List<Dictionary<string, object>>(),
            ["total_daily_quests_completed"] = q?.TotalDailyQuestsCompleted ?? 0,
            ["total_weekly_quests_completed"] = q?.TotalWeeklyQuestsCompleted ?? 0,
            ["total_xp_from_quests"] = q?.TotalXPFromQuests ?? 0,
            ["daily_quests_completed_today"] = q?.GetDailyQuestsCompletedToday() ?? 0,
            ["daily_completion_reset_date"] = q?.DailyCompletionResetDate is { } reset ? Day(reset) : "",
        };

        // WPF ProfileSyncService.TryParseDayKey (:3343): day keys and legacy ISO stamps, never past tomorrow.
        internal static bool TryParseDay(string? value, out DateTime date)
        {
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", Inv, DateTimeStyles.None, out date))
            {
                string[] iso = { "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK" };
                if (!DateTimeOffset.TryParseExact(value, iso, Inv, DateTimeStyles.AssumeUniversal, out var dto)) return false;
                date = dto.Date;
            }
            date = date.Date;
            return date <= DateTime.Today.AddDays(1);
        }

        private static bool Num(JObject o, string key, out double value)
        {
            value = 0;
            var t = o[key];
            if (t?.Type is JTokenType.Integer or JTokenType.Float) { value = t.Value<double>(); return true; }
            return t?.Type == JTokenType.String && double.TryParse((string?)t, NumberStyles.Float, Inv, out value);
        }

        private static void Higher(JObject o, string key, int local, Action<int> set, ref bool changed)
        {
            if (Num(o, key, out var v) && Convert.ToInt32(v) is var n && n > local) { set(n); changed = true; }
        }

        private static void Higher(JObject o, string key, double local, Action<double> set, ref bool changed)
        {
            if (Num(o, key, out var v) && v > local) { set(v); changed = true; }
        }

        /// <summary>WPF MergeV2CloudStatsIntoLocalProgress (ProfileSyncService.cs:3448-3721): take-higher lifetime
        /// counters; unless an override is active, the quest streak, date, dates, log, totals and today count.
        /// Does not save. True when anything changed.</summary>
        internal static bool Merge(AppSettings s, AchievementProgress? p, QuestProgress? q, Action? recalcQuestStreak,
            JObject stats, bool forceOverride)
        {
            var changed = false;
            if (p != null)
            {
                Higher(stats, "longest_session_minutes", p.LongestSessionMinutes, v => p.LongestSessionMinutes = v, ref changed);
                Higher(stats, "total_flashes", p.TotalFlashImages, v => p.TotalFlashImages = v, ref changed);
                Higher(stats, "total_bubbles_popped", p.TotalBubblesPopped, v => p.TotalBubblesPopped = v, ref changed);
                Higher(stats, "total_video_minutes", p.TotalVideoMinutes, v => p.TotalVideoMinutes = v, ref changed);
                Higher(stats, "total_lock_cards_completed", p.TotalLockCardsCompleted, v => p.TotalLockCardsCompleted = v, ref changed);
                Higher(stats, "highest_streak", s.HighestStreak, v => s.HighestStreak = v, ref changed);
                Higher(stats, "total_attention_checks_passed", p.TotalAttentionChecksPassed, v => p.TotalAttentionChecksPassed = v, ref changed);
                Higher(stats, "video_attention_checks_passed", p.VideoAttentionChecksPassed, v => p.VideoAttentionChecksPassed = v, ref changed);
                Higher(stats, "video_attention_checks_failed", p.VideoAttentionChecksFailed, v => p.VideoAttentionChecksFailed = v, ref changed);
                Higher(stats, "total_attention_check_failures", p.AttentionCheckFailures, v => p.AttentionCheckFailures = v, ref changed);
                Higher(stats, "total_bubble_count_games", p.TotalBubbleCountGames, v => p.TotalBubbleCountGames = v, ref changed);
                Higher(stats, "total_bubble_count_correct", p.TotalBubbleCountCorrect, v => p.TotalBubbleCountCorrect = v, ref changed);
                Higher(stats, "total_bubble_count_failed", p.TotalBubbleCountFailed, v => p.TotalBubbleCountFailed = v, ref changed);
                Higher(stats, "bubble_count_best_streak", p.BubbleCountBestStreak, v => p.BubbleCountBestStreak = v, ref changed);
                Higher(stats, "total_sessions_started", p.TotalSessionsStarted, v => p.TotalSessionsStarted = v, ref changed);
                Higher(stats, "total_sessions_abandoned", p.TotalSessionsAbandoned, v => p.TotalSessionsAbandoned = v, ref changed);
                Higher(stats, "total_xp_earned", p.TotalXPEarned, v => p.TotalXPEarned = v, ref changed);
                Higher(stats, "total_skill_points_earned", p.TotalSkillPointsEarned, v => p.TotalSkillPointsEarned = v, ref changed);
                if (Num(stats, "lifetime_points_spent", out var spent) && Convert.ToInt64(spent) > p.LifetimeSkillPointsSpent)
                { p.LifetimeSkillPointsSpent = Convert.ToInt64(spent); changed = true; }
                Higher(stats, "total_pink_filter_minutes", p.TotalPinkFilterMinutes, v => p.TotalPinkFilterMinutes = v, ref changed);
                Higher(stats, "total_spiral_minutes", p.TotalSpiralMinutes, v => p.TotalSpiralMinutes = v, ref changed);
            }
            if (forceOverride) return changed;   // ApplyForceOverride owns the quest fields
            Higher(stats, "daily_quest_streak", s.DailyQuestStreak, v => s.DailyQuestStreak = v, ref changed);
            if (TryParseDay((string?)(stats["last_daily_quest_date"] as JValue), out var lastQuest)
                && (s.LastDailyQuestDate is not { } local || lastQuest > local.Date))
            { s.LastDailyQuestDate = lastQuest; changed = true; }
            if (q == null) return changed;
            var cutoff = DateTime.Today.AddDays(-90);
            if (stats["quest_completion_dates"] is JArray dates)
            {
                var have = new HashSet<DateTime>(q.DailyQuestCompletionDates.Select(d => d.Date));
                var added = false;
                foreach (var t in dates)
                    if (t is JValue { Type: JTokenType.String } v && TryParseDay((string?)v, out var d) && have.Add(d))
                    { q.DailyQuestCompletionDates.Add(d); added = true; }
                if (added)
                {
                    q.DailyQuestCompletionDates.RemoveAll(d => d.Date < cutoff);
                    changed = true;
                    recalcQuestStreak?.Invoke();
                    Higher(stats, "daily_quest_streak", s.DailyQuestStreak, v => s.DailyQuestStreak = v, ref changed);
                }
            }
            if (stats["quest_completion_log"] is JArray log)
            {
                q.QuestCompletionLog ??= new List<QuestLogEntry>();
                var seen = new HashSet<string>(q.QuestCompletionLog.Where(e => e != null).Select(e => e.D + "|" + e.Q), StringComparer.Ordinal);
                var dayCut = Day(cutoff);
                var added = false;
                foreach (var e in log.OfType<JObject>())
                {
                    var (d, id) = ((string?)(e["d"] as JValue), (string?)(e["q"] as JValue));
                    if (string.IsNullOrEmpty(d) || string.IsNullOrEmpty(id) || string.CompareOrdinal(d, dayCut) < 0 || !seen.Add(d + "|" + id)) continue;
                    q.QuestCompletionLog.Add(new QuestLogEntry(d, id));
                    added = true;
                }
                if (added) { q.QuestCompletionLog.RemoveAll(e => e == null || string.CompareOrdinal(e.D, dayCut) < 0); changed = true; }
            }
            Higher(stats, "total_daily_quests_completed", q.TotalDailyQuestsCompleted, v => q.TotalDailyQuestsCompleted = v, ref changed);
            Higher(stats, "total_weekly_quests_completed", q.TotalWeeklyQuestsCompleted, v => q.TotalWeeklyQuestsCompleted = v, ref changed);
            Higher(stats, "total_xp_from_quests", q.TotalXPFromQuests, v => q.TotalXPFromQuests = v, ref changed);
            var doneToday = q.DailyQuestCompletionDates.Any(d => d.Date == DateTime.Today);
            if (Num(stats, "daily_quests_completed_today", out var today)
                && TryParseDay((string?)(stats["daily_completion_reset_date"] as JValue), out var resetDay) && resetDay == DateTime.Today
                && Convert.ToInt32(today) > q.GetDailyQuestsCompletedToday() && doneToday)
            {
                q.DailyQuestsCompletedToday = Convert.ToInt32(today);
                q.DailyCompletionResetDate = DateTime.Today;
                changed = true;
            }
            if (doneToday && q.GetDailyQuestsCompletedToday() == 0)
            {
                q.DailyQuestsCompletedToday = 1;
                q.DailyCompletionResetDate = DateTime.Today;
                changed = true;
            }
            return changed;
        }

        /// <summary>WPF ApplyForceStreakOverride (ProfileSyncService.cs:3727-3772): the admin's quest streak values,
        /// even when lower. Does not save.</summary>
        internal static void ApplyForceOverride(AppSettings s, QuestProgress? q, JObject streak)
        {
            static int Int(JObject o, string k) => Num(o, k, out var v) ? Convert.ToInt32(v) : 0;
            s.DailyQuestStreak = Int(streak, "daily_quest_streak");
            if (TryParseDay((string?)(streak["last_daily_quest_date"] as JValue), out var last)) s.LastDailyQuestDate = last;
            if (q == null) return;
            if (streak["quest_completion_dates"] is JArray dates)
            {
                q.DailyQuestCompletionDates.Clear();
                foreach (var t in dates)
                    if (t is JValue { Type: JTokenType.String } v && TryParseDay((string?)v, out var d)) q.DailyQuestCompletionDates.Add(d);
            }
            q.TotalDailyQuestsCompleted = Int(streak, "total_daily_quests_completed");
            q.TotalWeeklyQuestsCompleted = Int(streak, "total_weekly_quests_completed");
            q.TotalXPFromQuests = Int(streak, "total_xp_from_quests");
        }
    }
}
