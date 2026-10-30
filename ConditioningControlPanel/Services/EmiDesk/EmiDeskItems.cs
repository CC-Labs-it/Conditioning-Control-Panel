using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>What EMI wears on the desk from the Prize Counter: one hat and one held toy, each a shop id or null.</summary>
public sealed record DeskLook(string? Hat, string? Toy)
{
    public static readonly DeskLook Empty = new(null, null);
    public bool IsEmpty => Hat == null && Toy == null;
}

/// <summary>One EMI desk item (B3 section 2, "EMI desk" tab): sold as grant <c>cos.&lt;id&gt;</c>.</summary>
public sealed record EmiDeskItem(string Id, string Slot)
{
    public string Grant => "cos." + Id;

    /// <summary>Loc key of the item's shop name (<c>shop_item_desk_hat_top_hat_name</c>).</summary>
    public string NameKey => "shop_item_" + Id.Replace('.', '_').Replace('-', '_') + "_name";
}

/// <summary>
/// THE DESK ITEMS. Astra's ten-pose over-layer sheets (<c>Resources/emi/desk-items/&lt;id&gt;/over-body-*.png</c>,
/// the same 859x869 canvas and file names as an outfit's overlay, see ConditioningControlPanel/CLAUDE.md item 15)
/// for two hats and two toys. They draw ABOVE the outfit overlay, so the skin law holds: no face art over them.
///
/// <para><b>Ownership.</b> An item is worn only when the account holds its grant (contract 2); Prime is never
/// asked. <see cref="Clamp"/> is the one funnel: the settings value, a server copy and a pick all pass through
/// it. Unknown grants never strip: the caller passes null and the look stands as stored, the server clamps
/// its own copy.</para>
/// </summary>
public static class EmiDeskItems
{
    public const string HatSlot = "hat";
    public const string ToySlot = "toy";
    public const string Folder = "desk-items";

    public static readonly IReadOnlyList<EmiDeskItem> Items = new[]
    {
        new EmiDeskItem("desk.hat.top-hat", HatSlot),
        new EmiDeskItem("desk.hat.party", HatSlot),
        new EmiDeskItem("desk.toy.rubber-duck", ToySlot),
        new EmiDeskItem("desk.toy.snow-globe", ToySlot),
    };

    public static IReadOnlyList<EmiDeskItem> InSlot(string slot) => Items.Where(i => i.Slot == slot).ToList();

    public static EmiDeskItem? Find(string? id, string? slot = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var item = Items.FirstOrDefault(i => string.Equals(i.Id, id.Trim(), StringComparison.Ordinal));
        return item != null && (slot == null || item.Slot == slot) ? item : null;
    }

    /// <summary>
    /// Keep only ids that are this slot's items and, when <paramref name="grants"/> is known, owned. Pure.
    /// </summary>
    public static DeskLook Clamp(DeskLook? look, IEnumerable<string>? grants)
    {
        if (look == null) return DeskLook.Empty;
        var owned = grants == null ? null : new HashSet<string>(grants, StringComparer.Ordinal);
        string? Keep(string? id, string slot)
        {
            var item = Find(id, slot);
            if (item == null) return null;
            return owned == null || owned.Contains(item.Grant) ? item.Id : null;
        }
        var hat = Keep(look.Hat, HatSlot);
        var toy = Keep(look.Toy, ToySlot);
        return hat == null && toy == null ? DeskLook.Empty : new DeskLook(hat, toy);
    }

    /// <summary>
    /// The over-layer file for one item and pose, under <paramref name="root"/> (the app's base directory by
    /// default), or null when the item is unknown or that pose's sheet is missing. Never throws.
    /// </summary>
    public static string? OverPath(string? id, string? frame, string? root = null)
    {
        try
        {
            var item = Find(id);
            if (item == null) return null;
            var path = Path.Combine(root ?? AppContext.BaseDirectory, "Resources", "emi", Folder, item.Id,
                EmiChains.OverFileName(frame));
            return File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    /// <summary>The account's grants, or null while none are knowable (no snapshot yet).</summary>
    public static ISet<string>? KnownGrants()
    {
        try
        {
            var own = App.Ownership;
            if (own == null || own.Revision <= 0) return null;
            return own.Grants.ToHashSet(StringComparer.Ordinal);
        }
        catch { return null; }
    }

    /// <summary>True when the signed-in account holds the item's grant.</summary>
    public static bool IsOwned(EmiDeskItem item)
    {
        try { return App.Ownership?.IsGranted(item.Grant) == true; }
        catch { return false; }
    }

    /// <summary>What she wears now: the stored pick, clamped to what the account owns.</summary>
    public static DeskLook Current()
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return DeskLook.Empty;
            return Clamp(new DeskLook(s.EmiDeskHat, s.EmiDeskToy), KnownGrants());
        }
        catch { return DeskLook.Empty; }
    }
}
