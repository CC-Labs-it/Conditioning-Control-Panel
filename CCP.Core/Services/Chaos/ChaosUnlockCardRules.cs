using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>What one unlock card says, head-neutral: ribbon + accent, the item, a context line,
/// the placeholder glyph, where its art lives and which reward cue it plays. Each head turns
/// this into its own visual (WPF <c>ChaosUnlockCards</c>, Avalonia <c>ChaosUnlockCardLayer</c>).</summary>
public sealed record ChaosUnlockCardInfo(
    string Ribbon, uint AccentRgb, string Title, string Desc, string? Flavor, string? Context,
    string Glyph, string? IconFolder = null, string? IconId = null, string? IconPath = null)
{
    public (string cue, float vol) Cue => ChaosUnlockCardRules.CueFor(Ribbon);
}

/// <summary>
/// The unlock-card decisions, moved out of the WPF head's <c>ChaosUnlockCards</c>
/// (ChaosUnlockCardOverlay.cs:35) so both heads announce the same purchase with the same words.
/// Every builder returns null when there is nothing to announce.
/// </summary>
public static class ChaosUnlockCardRules
{
    public const string CapstoneRibbon = "CAPSTONE REACHED";
    public const string PocketRibbon = "POCKET SEWN";

    /// <summary>The reward cue by ribbon: pockets stitch, capstones bloom deeper, everything
    /// else gets the soft unlock shimmer.</summary>
    public static (string cue, float vol) CueFor(string ribbon) => ribbon switch
    {
        CapstoneRibbon => ("capstone_reached", 0.7f),
        PocketRibbon   => ("pocket_sewn", 0.7f),
        _              => ("unlock_card", 0.65f),
    };

    // Accents follow the announcer palette: toys mint, accessories warm gold, charms cyan.
    public const uint ToyAccent = 0x7AFFD2, AccessoryAccent = 0xFFD27A, CharmAccent = 0x7AE0FF,
                      HabitAccent = 0x9CE8A0, PocketAccent = 0xE84393, CapstoneAccent = 0xFFC83C;

    private static (string ribbon, uint accent) ByCategory(ChaosBoonCategory cat) => cat switch
    {
        ChaosBoonCategory.Skill     => ("NEW TOY UNLOCKED", ToyAccent),
        ChaosBoonCategory.Accessory => ("NEW ACCESSORY UNLOCKED", AccessoryAccent),
        _                           => ("NEW CHARM UNLOCKED", CharmAccent),
    };

    /// <summary>A lifetime boon's level 1 just bought (call AFTER the unlock so the auto-equip
    /// state reads true).</summary>
    public static ChaosUnlockCardInfo? ForBoonUnlock(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null) return null;
        var (ribbon, accent) = ByCategory(b.Category);
        string context =
            b.Category == ChaosBoonCategory.Utility ? "switched on — it works every descent."
            : ChaosMeta.IsBoonActive(id)            ? "slipped straight into a pocket — it rides with you next descent."
            : ChaosMeta.SlotsFor(b.Category) == 0   ? "no pocket to carry it yet — she sells one at her bench."
            :                                         "your pockets are full — swap it in from the BAG.";
        return new(ribbon, accent, b.Name, b.Desc, b.Flavor, context, b.Glyph, "boons", id);
    }

    /// <summary>A boon just deepened: only its FINAL level gets a card, and only when the
    /// capstone has text (mid levels just grow a number).</summary>
    public static ChaosUnlockCardInfo? ForCapstone(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        if (b == null || string.IsNullOrWhiteSpace(b.CapstoneDesc)) return null;
        return new(CapstoneRibbon, CapstoneAccent, b.Name, b.CapstoneDesc, b.Flavor,
            "fully deepened — its final gift is yours.", b.Glyph, "boons", id);
    }

    /// <summary>The hub's deepen click: the capstone card once the boon reaches its max level.</summary>
    public static ChaosUnlockCardInfo? ForUpgrade(string id)
    {
        var b = ChaosLifetimeBoons.ById(id);
        return b != null && ChaosMeta.BoonLevel(id) >= b.MaxLevel ? ForCapstone(id) : null;
    }

    /// <summary>A trained habit just bought.</summary>
    public static ChaosUnlockCardInfo? ForHabit(string id)
    {
        var u = ChaosUpgrades.ById(id);
        if (u == null) return null;
        return new("HABIT TRAINED", HabitAccent, u.Name, u.Desc, u.Flavor,
            "always on from your next descent — switch it off in the toybox anytime.",
            u.Glyph, u.IconPath == null ? "upgrades" : null, id, u.IconPath);
    }

    /// <summary>A pocket just sewn at her bench — the card explains what the slot does.</summary>
    public static ChaosUnlockCardInfo ForPocket(bool isToy, string label, string line)
    {
        int n = isToy ? ChaosMeta.State.ToyPockets : ChaosMeta.State.AccessoryPockets;
        string kind = isToy ? "toy" : "accessory";
        string desc = n == 1
            ? $"you can now carry one {kind} into the descent. unlocked {kind}s equip from the BAG."
            : $"you can now carry {n} {kind}s into the descent at once. pick yours from the BAG.";
        return new(PocketRibbon, PocketAccent, label, desc, line, null, "👝");
    }

    /// <summary>A bench row just bought: pockets get a card (a new SLOT is the thing players
    /// miss most easily); the other rows reveal their own surface instead.</summary>
    public static ChaosUnlockCardInfo? ForBench(string id)
    {
        bool toy = id is BenchIds.ToyPocket1 or BenchIds.ToyPocket2;
        if (!toy && id is not (BenchIds.AccPocket1 or BenchIds.AccPocket2)) return null;
        var item = ChaosBench.Items.FirstOrDefault(i => i.Id == id);
        return item == null ? null : ForPocket(toy, item.Label, item.Line);
    }

    /// <summary>A lesson just completed mid-run: the gated purchasable (boon or habit) is now buyable.</summary>
    public static ChaosUnlockCardInfo? ForLesson(string id)
    {
        const string context = "now for sale in the toybox — emotes will do the rest.";
        var b = ChaosLifetimeBoons.ById(id);
        if (b != null)
            return new("LESSON LEARNED", ByCategory(b.Category).accent, b.Name, b.Desc, b.Flavor,
                context, b.Glyph, "boons", id);
        var u = ChaosUpgrades.ById(id);
        return u == null ? null : new("LESSON LEARNED", HabitAccent, u.Name, u.Desc, u.Flavor,
            context, u.Glyph, u.IconPath == null ? "upgrades" : null, id, u.IconPath);
    }
}
