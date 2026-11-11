using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF AchievementProgressStreak startup and gap decision.</summary>
internal static class LoginStreakHead
{
    private static string? _loadedId;
    private static JObject? _loadedStats;
    private static AchievementEngine? _questXpEngine;
    private static DispatcherTimer? _rolloverTimer;
    internal static int ProfileGeneration { get; private set; }

    internal static void Start(AchievementEngine engine, AppSettings settings)
    {
        _questXpEngine = null;
        _rolloverTimer?.Stop();
        _rolloverTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _rolloverTimer.Tick += (_, _) =>
        {
            try { TickCurrent(DateTime.Today); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Login streak rollover failed"); }
        };
        _rolloverTimer.Start();
        Update(engine.Progress, settings, DateTime.Today, false);
        SaveAndPay(engine, settings);
        if (engine.Progress.PendingStreakBreak)
        {
            var id = settings.UnifiedId;
            if (_loadedId == id) ApplyCloud(engine, settings, _loadedStats, DateTime.Today);
            if (engine.Progress.PendingStreakBreak) _ = Timeout(engine, settings, id);
        }
        else if (_loadedId == settings.UnifiedId && !string.IsNullOrEmpty(_loadedId))
            ApplyCloud(engine, settings, _loadedStats, DateTime.Today);
    }

    // WPF :19/:107. A signed-in gap waits for the cloud before spending any protection.
    internal static bool Update(AchievementProgress p, AppSettings s, DateTime today, bool resolveGap)
    {
        today = today.Date;
        MarkSeason(s, p.ConsecutiveDays, DateTime.UtcNow);
        var last = p.LastLaunchDate.Date;
        if (p.PendingStreakBreak && !resolveGap) return false;
        if (resolveGap) p.PendingStreakBreak = false;
        if (last == default)
        {
            p.ConsecutiveDays = Math.Max(1, p.ConsecutiveDays);
        }
        else if (last == today)
        {
            s.CurrentStreak = p.ConsecutiveDays;
            s.LastStreakDate = p.LastLaunchDate;
            return false;
        }
        else if (last == today.AddDays(-1))
        {
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true;
        }
        else if (!resolveGap && !s.OfflineMode && !string.IsNullOrEmpty(s.UnifiedId))
        {
            p.PendingStreakBreak = true;
            return false;
        }
        else if (SkillTreeRules.HasSkill(s, "good_girl_streak") && s.StreakShieldsRemaining > 0)
        {
            s.StreakShieldsRemaining--;
            p.ConsecutiveDays++;
            p.PendingStreakBonus = true;
            for (var d = last < today ? last.AddDays(1) : today; d < today; d = d.AddDays(1))
                if (!s.StreakShieldUsedDates.Contains(d)) s.StreakShieldUsedDates.Add(d);
        }
        else if (SkillTreeRules.HasSkill(s, "oopsie_insurance") && s.StreakFixCharges > 0)
        {
            s.StreakFixCharges--;
            s.SeasonalStreakRecoveryUsed = true;
            if (!s.OfflineMode && !string.IsNullOrEmpty(s.UnifiedId)) _ = SyncInsurance(s, s.UnifiedId, today, ProfileGeneration);
        }
        else p.ConsecutiveDays = 1;

        p.PendingStreakBreak = false;
        p.LastLaunchDate = today;
        s.CurrentStreak = p.ConsecutiveDays;
        s.LastStreakDate = today;
        MarkSeason(s, p.ConsecutiveDays, DateTime.UtcNow);
        return true;
    }

    // WPF SeasonRecapService.cs:146 and :159 use UTC for the season day.
    internal static void MarkSeason(AppSettings s, int streak, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(s.SeasonStatsSeason))
            s.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(s, utcNow);
        var key = utcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!s.SeasonActiveDays.Contains(key)) s.SeasonActiveDays.Add(key);
        s.SeasonPeakStreak = Math.Max(s.SeasonPeakStreak, streak);
    }

    internal static bool Rollover(AchievementEngine engine, AppSettings settings, DateTime today)
    {
        var p = engine.Progress;
        if (p.PendingStreakBreak || !AchievementProgress.ShouldBankDayRollover(p.LastLaunchDate, today)) return false;
        var wasPending = p.PendingStreakBreak;
        var banked = Update(p, settings, today, false);
        if (!wasPending && p.PendingStreakBreak) _ = Timeout(engine, settings, settings.UnifiedId);
        SaveAndPay(engine, settings);
        if (banked && App.Quests is { } quests) App.CreditBankedLoginStreak(quests, p, today);
        return banked;
    }

    private static async Task SyncInsurance(AppSettings settings, string id, DateTime today, int generation)
    {
        var (ok, _, credits) = await new StreakFix(() => settings).UseAsync(today);
        if (ok && credits.HasValue && generation == ProfileGeneration
            && ReferenceEquals(CoreSettings.Current, settings) && settings.UnifiedId == id)
        {
            settings.StreakFixCharges = credits.Value;
            CoreSettings.Save();
        }
    }

    internal static bool TimerRunning => _rolloverTimer?.IsEnabled == true;

    internal static bool TickCurrent(DateTime today)
    {
        var engine = App.Achievements;
        return engine != null && Rollover(engine, CoreSettings.Current, today);
    }

    // Logout resets the existing engine, but does not tear down the quest XP listener.
    // The timer resolves the current profile on each tick instead of capturing the old one.
    internal static void ClearProfile() { ProfileGeneration++; _loadedId = null; _loadedStats = null; }

    internal static void Stop()
    {
        _rolloverTimer?.Stop();
        _rolloverTimer = null;
        _questXpEngine = null;
        ClearProfile();
    }

    internal static void ProfileLoaded(string id, JObject? stats)
    {
        _loadedId = id;
        _loadedStats = stats;
        var generation = ProfileGeneration;
        var engine = App.Achievements;
        if (engine == null) return; // Start consumes the cached profile if it has not built yet.
        var progress = engine.Progress;
        var settings = CoreSettings.Current;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != ProfileGeneration || !ReferenceEquals(App.Achievements, engine)
                || !ReferenceEquals(engine.Progress, progress) || !ReferenceEquals(CoreSettings.Current, settings)
                || settings.UnifiedId != id) return;
            ApplyCloud(engine, settings, stats, DateTime.Today);
        });
    }

    // WPF ProfileSyncService.cs:3567 and AchievementProgressStreak.cs:192.
    internal static void ApplyCloud(AchievementEngine engine, AppSettings s, JObject? stats, DateTime today)
    {
        var p = engine.Progress;
        if (int.TryParse(stats?["consecutive_days"]?.ToString(), out var count))
        {
            DateTime? date = TryParseCloudDay(stats?["last_streak_date"]?.ToString(), today, out var parsed) ? parsed : null;
            var adopt = AchievementProgress.DecideLoginStreakAdopt(p.ConsecutiveDays, p.LastLaunchDate, count, date, today);
            if (adopt is { } a) { p.ConsecutiveDays = a.Streak; p.LastLaunchDate = a.LastDate; }
        }
        if (p.PendingStreakBreak) Update(p, s, today, true);
        else { s.CurrentStreak = p.ConsecutiveDays; s.LastStreakDate = p.LastLaunchDate; }
        MarkSeason(s, p.ConsecutiveDays, DateTime.UtcNow);
        SaveAndPay(engine, s);
        if (App.Quests is { } quests) App.CreditBankedLoginStreak(quests, p, today);
    }

    // WPF ProfileSyncService.TryParseDayKey: invariant day keys and legacy ISO timestamps.
    internal static bool TryParseCloudDay(string? value, DateTime today, out DateTime date)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            string[] formats = { "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK" };
            if (!DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var timestamp)) return false;
            date = timestamp.Date;
        }
        date = date.Date;
        return date <= today.Date.AddDays(1);
    }

    internal static async Task Timeout(AchievementEngine engine, AppSettings settings, string? id, Task? delay = null)
    {
        var progress = engine.Progress;
        var generation = ProfileGeneration;
        await (delay ?? Task.Delay(TimeSpan.FromSeconds(120)));
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != ProfileGeneration || !ReferenceEquals(App.Achievements, engine)
                || !ReferenceEquals(engine.Progress, progress)
                || !ReferenceEquals(CoreSettings.Current, settings)
                || settings.UnifiedId != id || !engine.Progress.PendingStreakBreak) return;
            Update(engine.Progress, settings, DateTime.Today, true);
            SaveAndPay(engine, settings);
            if (App.Quests is { } quests) App.CreditBankedLoginStreak(quests, engine.Progress, DateTime.Today);
        });
    }

    private static void SaveAndPay(AchievementEngine engine, AppSettings settings)
    {
        engine.Save();
        CoreSettings.SaveImmediate();
        if (ReferenceEquals(_questXpEngine, engine)) PayPendingBonus(engine);
    }

    internal static void QuestXpReady(AchievementEngine? engine)
    {
        _questXpEngine = engine;
        if (engine != null) PayPendingBonus(engine);
    }

    private static void PayPendingBonus(AchievementEngine engine)
    {
        var p = engine.Progress;
        if (!p.PendingStreakBonus) return;
        p.PendingStreakBonus = false;
        var xp = p.ConsecutiveDays switch { <= 0 => 0, <= 3 => 50, <= 6 => 100, <= 13 => 150, <= 29 => 200, _ => 300 };
        if (xp > 0) ProgressionBank.Add(xp, "Other");
        engine.Save();
        CoreSettings.SaveImmediate();
    }
}
