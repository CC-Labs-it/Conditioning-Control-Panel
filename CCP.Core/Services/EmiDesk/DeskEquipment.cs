using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace ConditioningControlPanel.Services.EmiDesk;
public sealed record DeskEquipmentLook(string? Hat, string? Toy)
{
    public static readonly DeskEquipmentLook Empty = new(null, null);
    public bool IsEmpty => Hat == null && Toy == null;
}
public sealed record DeskEquipmentItem(string Id, string Slot)
{
    public string Grant => "cos." + Id;
    public string NameKey => "shop_item_" + Id.Replace('.', '_').Replace('-', '_') + "_name";
}
public static class DeskEquipment
{
    public const string HatSlot = "hat";
    public const string ToySlot = "toy";
    public const string Folder = "desk-items";
    public static readonly IReadOnlyList<DeskEquipmentItem> Items = new[]
    {
        new DeskEquipmentItem("desk.hat.top-hat", HatSlot),
        new DeskEquipmentItem("desk.hat.party", HatSlot),
        new DeskEquipmentItem("desk.toy.rubber-duck", ToySlot),
        new DeskEquipmentItem("desk.toy.snow-globe", ToySlot),
    };
    public static IReadOnlyList<DeskEquipmentItem> InSlot(string slot) => Items.Where(i => i.Slot == slot).ToList();
    public static DeskEquipmentItem? Find(string? id, string? slot = null)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var item = Items.FirstOrDefault(i => string.Equals(i.Id, id.Trim(), StringComparison.Ordinal));
        return item != null && (slot == null || item.Slot == slot) ? item : null;
    }
    public static DeskEquipmentLook Clamp(DeskEquipmentLook? look, IEnumerable<string>? grants)
    {
        if (look == null) return DeskEquipmentLook.Empty;
        var owned = grants == null ? null : new HashSet<string>(grants, StringComparer.Ordinal);
        string? Keep(string? id, string slot)
        {
            var item = Find(id, slot);
            if (item == null) return null;
            return owned == null || owned.Contains(item.Grant) ? item.Id : null;
        }
        var hat = Keep(look.Hat, HatSlot);
        var toy = Keep(look.Toy, ToySlot);
        return hat == null && toy == null ? DeskEquipmentLook.Empty : new DeskEquipmentLook(hat, toy);
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
