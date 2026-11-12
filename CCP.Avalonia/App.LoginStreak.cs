using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia;

public partial class App
{
    private static (string Id, int? Streak, DateTime? Date)? _pendingLoginStreak;

    private static (int Streak, DateTime Date)? LoginStreakForSync(string id)
    {
        var engine = Achievements;
        if (engine == null || CoreSettings.Service?.Current.UnifiedId != id) return null;
        lock (engine)
        {
            if (!ReferenceEquals(Achievements, engine) || CoreSettings.Service?.Current.UnifiedId != id) return null;
            var progress = engine.Progress;
            return (progress.ConsecutiveDays, progress.LastLaunchDate);
        }
    }

    // WPF AchievementProgressStreak.cs:19-99,192-257 and AchievementService.cs:93,146.
    private static void AdvanceLoginStreak(DateTime utcNow, bool resolve = false)
    {
        var engine = Achievements;
        var settings = CoreSettings.Service?.Current;
        if (_exiting || engine == null || settings == null) return;
        var progress = engine.Progress;
        bool wasPending, spent;
        lock (engine)
        {
            if (!ReferenceEquals(engine.Progress, progress)) return;
            wasPending = progress.PendingStreakBreak;
            spent = LoginStreak.Advance(progress, settings, utcNow.ToLocalTime().Date, utcNow, resolve);
            settings.CurrentStreak = progress.ConsecutiveDays;
            settings.LastStreakDate = progress.LastLaunchDate;
        }
        if (!wasPending && progress.PendingStreakBreak)
            _ = ResolveStreakAfterTimeout(engine, progress, settings);
        engine.IsDirty = true;
        engine.Save();
        CoreSettings.Save();
        Platform.AccountSeed.Sync?.Nudge("login-streak");
        if (spent && !string.IsNullOrEmpty(settings.UnifiedId) && !settings.OfflineMode)
            _ = ReportInsuranceSpend(engine, progress, settings, utcNow.ToLocalTime().Date);
        if (progress.PendingStreakBonus && CoreProgression.AddXPProvider != null)
        {
            progress.PendingStreakBonus = false;
            var xp = LoginStreak.Bonus(progress.ConsecutiveDays);
            if (xp > 0) CoreProgression.AddXP(xp, "Other");
        }
    }

    private static async Task ResolveStreakAfterTimeout(AchievementEngine engine, AchievementProgress progress, AppSettings settings)
    {
        await Task.Delay(TimeSpan.FromSeconds(120));
        if (_exiting) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_exiting && ReferenceEquals(Achievements, engine) && ReferenceEquals(engine.Progress, progress)
                && ReferenceEquals(CoreSettings.Service?.Current, settings) && progress.PendingStreakBreak)
                AdvanceLoginStreak(DateTime.UtcNow, resolve: true);
        });
    }

    private static async Task ReportInsuranceSpend(AchievementEngine engine, AchievementProgress progress, AppSettings settings, DateTime today)
    {
        var id = settings.UnifiedId;
        var (ok, _, credits) = await new StreakFix(() => settings).UseAsync(today);
        if (ok && credits.HasValue && ReferenceEquals(Achievements, engine) && ReferenceEquals(engine.Progress, progress)
            && ReferenceEquals(CoreSettings.Service?.Current, settings) && settings.UnifiedId == id)
        {
            settings.StreakFixCharges = credits.Value;
            CoreSettings.Save();
        }
    }

    private static void ReceiveLoginStreak(string id, int? streak, DateTime? date)
    {
        var receiptEngine = Achievements;
        var receiptProgress = receiptEngine?.Progress;
        var receiptSettings = CoreSettings.Service?.Current;
        Dispatcher.UIThread.Post(() =>
        {
            var engine = Achievements;
            var settings = CoreSettings.Service?.Current;
            if (_exiting || settings == null || !ReferenceEquals(settings, receiptSettings) || settings.UnifiedId != id
                || (receiptEngine != null && (!ReferenceEquals(engine, receiptEngine) || !ReferenceEquals(engine?.Progress, receiptProgress)))) return;
            if (engine == null)
            {
                _pendingLoginStreak = (id, streak, date);
                return;
            }
            var p = engine.Progress;
            bool pending;
            lock (engine)
            {
                if (streak.HasValue) LoginStreak.Adopt(p, streak.Value, date, DateTime.Today);
                pending = p.PendingStreakBreak;
                if (!pending)
                {
                    settings.CurrentStreak = p.ConsecutiveDays;
                    settings.LastStreakDate = p.LastLaunchDate;
                    SeasonActivity.TrackStreakPeak(settings, p.ConsecutiveDays, DateTime.UtcNow);
                }
            }
            if (pending) AdvanceLoginStreak(DateTime.UtcNow, resolve: true);
            else { engine.Save(); CoreSettings.Save(); }
        });
    }
}
