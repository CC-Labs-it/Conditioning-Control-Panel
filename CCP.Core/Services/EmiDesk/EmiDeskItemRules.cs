using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.EmiDesk;
public sealed record DeskItemLook(string? Hat, string? Toy)
{
    public static readonly DeskItemLook Empty = new(null, null);
    public bool IsEmpty => Hat == null && Toy == null;
}
public sealed record DeskItemDefinition(string Id, string Slot)
{
    public string Grant => "cos." + Id;
    public string NameKey => "shop_item_" + Id.Replace('.', '_').Replace('-', '_') + "_name";
}
public static class EmiDeskItemRules
{
    public const string HatSlot = "hat";
    public const string ToySlot = "toy";
    public const string Folder = "desk-items";

    public static readonly IReadOnlyList<DeskItemDefinition> Items = new[]
    {
        new DeskItemDefinition("desk.hat.top-hat", HatSlot),
        new DeskItemDefinition("desk.hat.party", HatSlot),
        new DeskItemDefinition("desk.toy.rubber-duck", ToySlot),
        new DeskItemDefinition("desk.toy.snow-globe", ToySlot),
    };

    public static IReadOnlyList<DeskItemDefinition> InSlot(string slot) => Items.Where(i => i.Slot == slot).ToList();

    public static DeskItemDefinition? Find(string? id, string? slot = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var item = Items.FirstOrDefault(i => string.Equals(i.Id, id.Trim(), StringComparison.Ordinal));
        return item != null && (slot == null || item.Slot == slot) ? item : null;
    }
    public static DeskItemLook Clamp(DeskItemLook? look, IEnumerable<string>? grants)
    {
        if (look == null) return DeskItemLook.Empty;
        var owned = grants == null ? null : new HashSet<string>(grants, StringComparer.Ordinal);
        string? Keep(string? id, string slot)
        {
            var item = Find(id, slot);
            if (item == null) return null;
            return owned == null || owned.Contains(item.Grant) ? item.Id : null;
        }
        var hat = Keep(look.Hat, HatSlot);
        var toy = Keep(look.Toy, ToySlot);
        return hat == null && toy == null ? DeskItemLook.Empty : new DeskItemLook(hat, toy);
    }
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

}
