using System;
using Serilog;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>Where a change to the Sparkles balance came from.</summary>
public enum SparkleSource
{
    LevelUp,
    BubbleMilestone,
    /// <summary>A game host's place claim (Sparkles by place).</summary>
    Place,
    /// <summary>The Back Room page moved the balance (a win, a spin, a buy in the room).</summary>
    BackRoom,
    /// <summary>A PvP stake settled.</summary>
    Stake,
    /// <summary>A Prize Counter buy settlement (the counter page, or a "Get it" row in the panel).
    /// A raise-only counter read is <see cref="Synced"/>.</summary>
    CounterBuy,
    /// <summary>A gift reached this PC. Reserved: no desktop writer today.</summary>
    Gift,
    /// <summary>A sync or profile load from the server.</summary>
    Synced,
    /// <summary>A skill tree purchase, its refusal correction, or a forced reset refund.</summary>
    SkillTree,
    /// <summary>Signed out: the local copy goes back to zero. Never celebrated.</summary>
    Reset,
}

/// <summary>One change to the balance: signed amount, the balance before and after, and why.</summary>
public readonly record struct SparkleChange(int Amount, int Before, int Balance, SparkleSource Source)
{
    public bool IsGain => Amount > 0;
}

/// <summary>
/// THE ONE DOOR to <see cref="AppSettings.SkillPoints"/>. Every write the desktop makes to the
/// balance goes through <see cref="Write"/>, which does the write exactly as the caller computed it
/// (the arithmetic stays with the callers and with <see cref="V2WalletAdoption"/> /
/// <see cref="V2WalletNet"/>) and then raises <see cref="Changed"/> with the signed amount the
/// setter's clamp actually let through.
///
/// <para>Presentation only: a listener can never touch the ledger, a throwing listener never stops
/// the next one or the caller, and a write that changes nothing raises nothing.
/// <see cref="SparklePointRewards"/> stays as it is: it is the narrower "a local credit landed"
/// cue the panel's gain badge listens to.</para>
/// </summary>
public static class SparklesLedger
{
    /// <summary>Raised after the write, on the writer's thread. The sender is the settings object
    /// written to, so a listener can tell the live profile from a stale one.</summary>
    public static event EventHandler<SparkleChange>? Changed;

    /// <summary>Pure: what a write from <paramref name="before"/> to <paramref name="after"/> is,
    /// after the cap clamp, or null when nothing moved.</summary>
    public static SparkleChange? Describe(int before, int after, SparkleSource source)
    {
        var b = SparklePoints.Clamp(before);
        var a = SparklePoints.Clamp(after);
        return a == b ? null : new SparkleChange(a - b, b, a, source);
    }

    /// <summary>Writes <paramref name="next"/> into the balance and reports the change.</summary>
    /// <returns>True when the balance moved.</returns>
    public static bool Write(AppSettings settings, int next, SparkleSource source)
    {
        if (settings == null) return false;
        var before = settings.SkillPoints;
        settings.SkillPoints = next;
        if (Describe(before, settings.SkillPoints, source) is not { } change) return false;
        Raise(settings, change);
        return true;
    }

    private static void Raise(AppSettings settings, SparkleChange change)
    {
        var listeners = Changed;
        if (listeners == null) return;
        foreach (EventHandler<SparkleChange> listener in listeners.GetInvocationList())
        {
            try { listener(settings, change); }
            catch (Exception ex)
            {
                // A broken decoration cannot block another listener or unwind a settled write.
                Log.Debug("Sparkles ledger listener failed: {Error}", ex.Message);
            }
        }
    }
}
