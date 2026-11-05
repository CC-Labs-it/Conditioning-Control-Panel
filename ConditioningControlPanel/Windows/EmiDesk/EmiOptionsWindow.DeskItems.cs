using System;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel;

/// <summary>
/// HER THINGS: where the desk hat and toy are chosen (B3-P2 equip). One segmented row per slot, "None" plus the
/// slot's Prize Counter items. Owned items pick and save at once (DeskEquipSync.Save: local first, PUT behind);
/// the rest stay visible, disabled, and say In the Prize Counter. The button opens the counter on its desk tab.
/// </summary>
public partial class EmiOptionsWindow
{
    private void BuildDeskPicks()
    {
        try
        {
            var current = EmiDeskItems.Current();
            FillSlot(DeskHatPicks, EmiDeskItems.HatSlot, current.Hat);
            FillSlot(DeskToyPicks, EmiDeskItems.ToySlot, current.Toy);
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] desk picks build failed"); }
    }

    private void FillSlot(WrapPanel host, string slot, string? worn)
    {
        host.Children.Clear();
        var group = "desk-" + slot;
        host.Children.Add(Segment(group, Loc.Get("emi_desk_opt_none"), worn == null, true, null, () => Pick(slot, null)));
        foreach (var item in EmiDeskItems.InSlot(slot))
        {
            bool owned = EmiDeskItems.IsOwned(item);
            host.Children.Add(Segment(group, Loc.Get(item.NameKey), worn == item.Id, owned,
                owned ? null : Loc.Get("shop_locked_in_counter"), () => Pick(slot, item.Id)));
        }
    }

    private RadioButton Segment(string group, string text, bool on, bool enabled, string? tip, Action picked)
    {
        var b = new RadioButton
        {
            GroupName = group,
            Content = text,
            IsChecked = on,
            IsEnabled = enabled,
            Margin = new Thickness(0, 0, 0, 4),
            Style = (Style)FindResource("EmiPanelSegment"),
        };
        if (!enabled)
        {
            b.Opacity = 0.45;
            b.ToolTip = tip;
            ToolTipService.SetShowOnDisabled(b, true);
        }
        b.Checked += (_, _) =>
        {
            if (_loading) return;
            picked();
        };
        return b;
    }

    private void Pick(string slot, string? itemId)
    {
        try
        {
            var now = EmiDeskItems.Current();
            var next = slot == EmiDeskItems.HatSlot ? now with { Hat = itemId } : now with { Toy = itemId };
            DeskEquipSync.Save(next);
            _owner.RefreshDeskItems();
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] desk pick failed"); }
    }

    private void OpenCounterForDesk()
    {
        try
        {
            ClosePanel();
            Services.PrizeCounter.PrizeCounterHostService.Launch("wallet", "desk");
        }
        catch (Exception ex) { Log.Debug(ex, "[EmiDesk] counter open failed"); }
    }
}
