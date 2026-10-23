using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

public enum SparklePointSource { LevelUp, BubbleMilestone }

/// <summary>A settled local credit, distinct from a balance restored or received from another device.</summary>
public readonly record struct SparklePointAward(int Amount, int Balance, SparklePointSource Source);

/// <summary>Shared local earn rules and settled-credit notifications. Restores and sync never call the earn rules.</summary>
public static class SparklePointRewards
{
    public static event EventHandler<SparklePointAward>? Awarded;

    public const int PointsPerLevel = 1;

    /// <summary>WPF SkillTreeService.OnLevelUp: one point per level and the season's peak.</summary>
    public static void CreditLevelUp(int newLevel, int levelsGained = 1)
    {
        if (levelsGained <= 0) return;
        var settings = CoreSettings.Current;
        settings.SeasonPeakLevel = Math.Max(settings.SeasonPeakLevel, newLevel);
        Credit(levelsGained * PointsPerLevel, SparklePointSource.LevelUp);
    }

    /// <summary>WPF AchievementService: every 100 lifetime pops, including all boundaries in a batch.
    /// Returns the boundaries crossed so the head can show one milestone popup.</summary>
    public static int CreditBubbleMilestones(int before, int after)
    {
        if (after <= before) return 0;
        var milestones = after / 100 - before / 100;
        if (milestones > 0) Credit(milestones, SparklePointSource.BubbleMilestone);
        return milestones;
    }

    private static void Credit(int amount, SparklePointSource source)
    {
        var settings = CoreSettings.Current;
        var previous = settings.SkillPoints;
        settings.SkillPoints += amount;
        var balance = settings.SkillPoints;
        CoreSettings.Save();
        PublishCredit(previous, balance, source);
        Serilog.Log.Information("Sparkle points from {Source}: awarded {Amount} (balance: {Balance})",
            source, balance - previous, balance);
    }

    /// <summary>Call only after a local award is saved. Capped and zero credits remain quiet.</summary>
    public static void PublishCredit(int previousBalance, int currentBalance, SparklePointSource source)
    {
        var balance = SparklePoints.Clamp(currentBalance);
        var amount = balance - SparklePoints.Clamp(previousBalance);
        if (amount <= 0) return;
        var listeners = Awarded;
        if (listeners == null) return;
        var award = new SparklePointAward(amount, balance, source);
        foreach (EventHandler<SparklePointAward> listener in listeners.GetInvocationList())
        {
            try { listener(null, award); }
            catch (Exception ex)
            {
                // A broken decoration cannot block another subscriber or unwind a settled credit.
                Serilog.Log.Debug("Sparkle point award subscriber failed: {Error}", ex.Message);
            }
        }
    }
}
