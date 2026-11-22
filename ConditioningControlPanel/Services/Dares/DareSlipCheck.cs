using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// What a slip may say before it is sent (CONTRACT.md "Grammar"), the dare numbering ("(1)", "(2)" in
/// order of appearance: what first, then the from stake, then the to stake) and who has to do each
/// sealed dare. The server checks all of it again; this keeps the client from sending a slip it would
/// refuse. Pure.
/// </summary>
public static class DareSlipCheck
{
    /// <summary>The same slip with every dare numbered in order of appearance.</summary>
    public static DareSlip Numbered(DareSlip slip)
    {
        var n = 0;
        var what = slip.What.Kind == DareKind.Dare ? slip.What with { Target = (++n).ToString(System.Globalization.CultureInfo.InvariantCulture) } : slip.What;
        var from = slip.FromStake.Kind == DareStakeKind.Dare ? slip.FromStake with { Amount = 0, DareNumber = ++n } : slip.FromStake;
        var to = slip.ToStake.Kind == DareStakeKind.Dare ? slip.ToStake with { Amount = 0, DareNumber = ++n } : slip.ToStake;
        return slip with { What = what, FromStake = from, ToStake = to, Camera = slip.Camera && DareGrammar.CameraAllowed(slip.What.Kind) };
    }

    /// <summary>The sealed dares on a numbered slip and who does each: the what-dare is the receiver's
    /// (a "can't" bet is about them), a stake dare belongs to the side that would owe it.</summary>
    public static IReadOnlyList<DareSeal> Seals(DareSlip slip)
    {
        var s = Numbered(slip);
        var list = new List<DareSeal>();
        if (s.What.Kind == DareKind.Dare && int.TryParse(s.What.Target, out var w)) list.Add(new DareSeal(w, DareSide.To));
        if (s.FromStake.Kind == DareStakeKind.Dare) list.Add(new DareSeal(s.FromStake.DareNumber, DareSide.From));
        if (s.ToStake.Kind == DareStakeKind.Dare) list.Add(new DareSeal(s.ToStake.DareNumber, DareSide.To));
        return list;
    }

    /// <summary>Any dare on the slip: its words are written in Discord, so the sender needs Discord linked.</summary>
    public static bool HasDare(DareSlip slip) =>
        slip.What.Kind == DareKind.Dare || slip.FromStake.Kind == DareStakeKind.Dare || slip.ToStake.Kind == DareStakeKind.Dare;

    /// <summary>A Lockdown stake is for subscribers (owner, 2026-11-18): the side that would owe it needs
    /// effective tier 1 or 2. The server refuses it with <c>lockdown_needs_sub</c>; the slip shows the tile locked.</summary>
    public static bool LockdownTierOk(int tier) => tier >= 1;

    /// <summary>Does <paramref name="side"/> put up a Lockdown on this slip (that side needs <see cref="LockdownTierOk"/>).</summary>
    public static bool StakesLockdown(DareSlip slip, DareSide side) =>
        (side == DareSide.From ? slip.FromStake : slip.ToStake).Kind == DareStakeKind.Lockdown;

    /// <summary>True when every blank holds a value the grammar offers.</summary>
    public static bool IsValid(DareSlip slip)
    {
        if (slip == null) return false;
        if (!DareGrammar.KindsFor(slip.Type).Contains(slip.What.Kind)) return false;
        if (slip.Type == DareType.Duel)
        {
            if (!string.IsNullOrEmpty(slip.What.Target)) return false;
        }
        else if (slip.What.Kind != DareKind.Dare && !DareGrammar.TargetsFor(slip.What.Kind).Contains(slip.What.Target ?? ""))
            return false;
        if (slip.Camera && !DareGrammar.CameraAllowed(slip.What.Kind)) return false;
        return StakeValid(slip.FromStake) && StakeValid(slip.ToStake);
    }

    public static bool StakeValid(DareStake s) => s.Kind switch
    {
        DareStakeKind.Dare => true,
        _ => DareGrammar.AmountsFor(s.Kind).Contains(s.Amount),
    };

    /// <summary>The session difficulty rank of a target id ("easy" 0, "medium" 1, "hard" 2); -1 for anything else.</summary>
    public static int SessionRank(string? target) => target switch
    {
        "easy" => 0,
        "medium" => 1,
        "hard" => 2,
        _ => -1,
    };

    /// <summary>A finished session of <paramref name="difficulty"/> makes a session bet of <paramref name="target"/>:
    /// the named difficulty or harder (Extreme is harder than Hard).</summary>
    public static bool SessionMakes(ConditioningControlPanel.Models.SessionDifficulty difficulty, string? target)
    {
        var need = SessionRank(target);
        if (need < 0) return false;
        var got = difficulty switch
        {
            ConditioningControlPanel.Models.SessionDifficulty.Easy => 0,
            ConditioningControlPanel.Models.SessionDifficulty.Medium => 1,
            ConditioningControlPanel.Models.SessionDifficulty.Hard => 2,
            _ => 3,
        };
        return got >= need;
    }
}
