using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.PrizeCounter;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>One look on a Studio "Look" row: the shop's row id, its price, and whether this account owns and wears it.</summary>
public sealed record LookChoice(string Id, string Slot, int Price, bool Owned, bool Worn)
{
    /// <summary>The shop's own name key (<c>shop_item_fx_bubble_heart_name</c>).</summary>
    public string NameKey => "shop_item_" + Id.Replace('.', '_') + "_name";
    /// <summary>The shop's one-line explanation (<c>shop_sheet_tip_fx_bubble_heart</c>).</summary>
    public string TipKey => "shop_sheet_tip_" + Id.Replace('.', '_');
}

/// <summary>Who answers "owned, worn, wear it" for the wardrobe. One per build mode (see <see cref="LookWardrobe"/>).</summary>
internal interface ILookStore
{
    bool Owns(string itemId);
    /// <summary>The row worn in a slot, or null for its free default. <c>mix</c>: the Two-tone id while on.</summary>
    string? Worn(string slot);
    /// <summary>Wear (or with null take off) in a slot; false = refused. Raises <see cref="WornLooks.Changed"/> on a change.</summary>
    bool Equip(string slot, string? itemId);
}

/// <summary>
/// No account equip yet: only the free row of each slot is owned and worn, everything else is locked, and nothing
/// is ever sent anywhere.
/// <para>THE REAL ACCOUNT EQUIP PLUGS IN HERE (later lane, CCP-Server shop-routes.js header): owned = the grants of
/// <c>/v2/shop/state</c>, worn = <c>GET /v2/equip</c> family <c>looks</c>, wear = <c>POST /v2/shop/equip { slot, itemId }</c>,
/// then <see cref="WornLooks.Raise"/>. Replace this class (or give <see cref="LookWardrobe.Store"/> a new one); the
/// Studio rows and the effects do not change.</para>
/// </summary>
internal sealed class FreeOnlyLookStore : ILookStore
{
    public bool Owns(string itemId) => LookWardrobe.Catalogue.Any(e => e.Id == itemId && e.Price == 0);
    public string? Worn(string slot) => null;
    public bool Equip(string slot, string? itemId) => itemId == null || itemId == WornLooks.DefaultOf(slot);
}

#if DEBUG
/// <summary>The DEBUG shop preview: the SAME state file and rules the shop page uses, so a look worn here shows as
/// worn in the shop on its next open, and the other way round.</summary>
internal sealed class PreviewLookStore : ILookStore
{
    private readonly ShopPreviewBackend _backend;
    public PreviewLookStore(ShopPreviewBackend backend) { _backend = backend; }
    public bool Owns(string itemId) => _backend.Owns(itemId);
    public string? Worn(string slot) => _backend.Worn(slot);
    public bool Equip(string slot, string? itemId)
        => _backend.Handle(ShopPreviewBackend.OpEquip, new Newtonsoft.Json.Linq.JObject { ["slot"] = slot, ["itemId"] = itemId }, null)["ok"]?.ToObject<bool>() == true;
}
#endif

/// <summary>
/// THE LOOK WARDROBE: the one seam the Studio "Look" rows talk to (and nothing else does). What this account owns
/// and wears per slot, wear one, Two-tone's switch and second colour, and a <see cref="Changed"/> event on the UI
/// thread. What the EFFECTS read stays <see cref="WornLooks"/>; a wear here lands there at once (new bubbles, the
/// next card, the next word, the fog's next frame).
/// <para>A look is not a session value: sessions and presets never write it.</para>
/// </summary>
public static class LookWardrobe
{
    /// <summary>The desktop's catalogue of looks, in shelf order (prices as the shop's). "Not out yet" rows are left out.</summary>
    internal static IEnumerable<PreviewEffect> Catalogue => ShopPreviewRules.Effects.Where(e => !e.Todo);

    private static ILookStore? _store;
    internal static ILookStore Store
    {
        get => _store ??= DefaultStore();
        set => _store = value;
    }

    private static ILookStore DefaultStore()
    {
#if DEBUG
        if (ShopPreviewBackend.Enabled) return new PreviewLookStore(ShopPreviewBackend.Shared);
#endif
        return new FreeOnlyLookStore();
    }

    /// <summary>The looks of a slot for a Studio row: the free row first, owned and worn marked. Fog has none (Two-tone is its own block).</summary>
    public static IReadOnlyList<LookChoice> Choices(string slot)
    {
        var worn = Worn(slot);
        return Catalogue.Where(e => e.Slot == slot && !e.Special)
            .Select(e => new LookChoice(e.Id, slot, e.Price, Owned(e.Id), e.Id == worn))
            .ToList();
    }

    /// <summary>The free row of a slot is always owned.</summary>
    public static bool Owned(string itemId)
    {
        if (Catalogue.Any(e => e.Id == itemId && e.Price == 0)) return true;
        try { return Store.Owns(itemId); } catch { return false; }
    }

    /// <summary>The row worn in a slot; its free row when nothing (or something no longer owned) is worn.</summary>
    public static string Worn(string slot)
    {
        string? id = null;
        try { id = Store.Worn(slot); } catch { }
        return id != null && Owned(id) ? id : WornLooks.DefaultOf(slot) ?? "";
    }

    /// <summary>Wear a look. Refused (false) for an unowned or unknown id, or one of another slot.</summary>
    public static bool Wear(string slot, string itemId)
    {
        var e = Catalogue.FirstOrDefault(x => x.Id == itemId);
        if (e == null || e.Slot != slot || e.Special || !Owned(itemId)) return false;
        if (Worn(slot) == itemId) return true;
        try { return Store.Equip(slot, e.Price == 0 ? null : itemId); } catch { return false; }
    }

    public static bool TwoToneOwned => Owned(SuperCore.FogLooks.Twotone);

    /// <summary>Two-tone's switch. Setting it is refused while Two-tone is not owned.</summary>
    public static bool TwoToneOn
    {
        get { try { return TwoToneOwned && Store.Worn(WornLooks.Mix) != null; } catch { return false; } }
        set { if (TwoToneOwned && value != TwoToneOn) { try { Store.Equip(WornLooks.Mix, value ? SuperCore.FogLooks.Twotone : null); } catch { } } }
    }

    /// <summary>Two-tone's second colour (a <c>FogLooks.Palette</c> row). Setting it is refused while Two-tone is not owned or for a non-palette id.</summary>
    public static string SecondFogColour
    {
        get { string? id = null; try { id = Store.Worn(WornLooks.Fog2); } catch { } return SuperCore.FogLooks.IsPalette(id) ? id! : SuperCore.FogLooks.DefaultSecond; }
        set { if (TwoToneOwned && SuperCore.FogLooks.IsPalette(value) && value != SecondFogColour) { try { Store.Equip(WornLooks.Fog2, value); } catch { } } }
    }

    /// <summary>The price of a row (0 when unknown).</summary>
    public static int PriceOf(string itemId) => Catalogue.FirstOrDefault(e => e.Id == itemId)?.Price ?? 0;

    private static event Action<string>? _changed;
    private static bool _hooked;

    /// <summary>A slot was put on or taken off (here, in the shop, anywhere). Always raised on the UI thread.</summary>
    public static event Action<string> Changed
    {
        add { Hook(); _changed += value; }
        remove { _changed -= value; }
    }

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        WornLooks.Changed += slot =>
        {
            var d = System.Windows.Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) _changed?.Invoke(slot);
            else d.BeginInvoke(new Action(() => _changed?.Invoke(slot)));
        };
    }
}
