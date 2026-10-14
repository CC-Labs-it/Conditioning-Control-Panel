using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The pure payout maths of a Chaos descent, shared by both heads (WPF ChaosModeService.EndRun
/// and AwardLoopTip; Avalonia ChaosRunHost). Banking (AddXP, AwardRunRewards, reveals) stays
/// with the caller; these only compute.
/// </summary>
public static class ChaosPayout
{
    /// <summary>XP the recap shows: <c>BaseXp</c> is what is banked (WPF AddXP(baseXp)),
    /// <c>FinalXp</c> = base × skill is the headline figure.</summary>
    public readonly record struct RunXp(double BaseXp, double SkillMult, double FinalXp);

    /// <summary>WPF EndRun: score, capped at 250 XP per minute of run × difficulty, then × skill.</summary>
    public static RunXp Xp(double runDurationSec, double difficultyMult, double score, double skillMult)
    {
        double durMin = Math.Max(1, runDurationSec) / 60.0;
        double capBase = 250.0 * durMin * difficultyMult;
        double baseXp = Math.Min(score, capBase);
        return new RunXp(baseXp, skillMult, baseXp * skillMult);
    }

    /// <summary>WPF AwardLoopTip: <paramref name="roll"/> (3..6) × difficulty, doubled for a
    /// clean loop (zero detonations), doubled again in the Relapse loop (GoldScaled).</summary>
    public static int LoopTip(int roll, double difficultyMult, bool clean, ChaosRunState? state)
    {
        int tip = (int)Math.Round(roll * difficultyMult);
        if (clean) tip *= 2;
        return ChaosRunEngine.GoldScaled(state, tip);
    }

    /// <summary>WPF EndRun rank spine: the rank the run just reached when it is above the last
    /// card shown, else null (only the highest new rank gets a card).</summary>
    public static ChaosRank? RankUp(int runsCompleted, int lastRankSeen)
    {
        var now = ChaosRanks.For(runsCompleted);
        return (int)now > lastRankSeen ? now : null;
    }
}
