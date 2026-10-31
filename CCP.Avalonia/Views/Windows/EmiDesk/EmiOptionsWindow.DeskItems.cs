using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.EmiDesk;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiOptionsWindow.DeskItems.cs. B3: owned picks only; no Avalonia shop door.
public partial class EmiOptionsWindow
{
    private void BuildDeskPicks()
    {
        var look = EmiDeskWindow.CurrentDeskItems();
        FillDeskSlot("DeskHatPicks", DeskEquipment.HatSlot, look.Hat);
        FillDeskSlot("DeskToyPicks", DeskEquipment.ToySlot, look.Toy);
    }
    private void FillDeskSlot(string name, string slot, string? worn)
    {
        var host = this.FindControl<WrapPanel>(name)!;
        host.Children.Clear();
        Add(null, Loc.Get("emi_desk_opt_none"), true);
        foreach (var item in DeskEquipment.InSlot(slot))
            Add(item.Id, Loc.Get(item.NameKey), PrizeOwnership.IsGranted(item.Grant));
        void Add(string? id, string text, bool owned)
        {
            var button = new RadioButton { Content = text, GroupName = "desk-" + slot,
                IsChecked = worn == id, IsEnabled = owned && _owner != null, Margin = new Thickness(0,0,0,4),
                Theme = (ControlTheme)this.FindResource("EmiPanelSegment")! };
            if (!owned) { button.Opacity = .45; ToolTip.SetTip(button, Loc.Get("shop_locked_in_counter")); ToolTip.SetShowOnDisabled(button, true); }
            button.IsCheckedChanged += (_, _) =>
            {
                if (!_loading && button.IsChecked == true && button.IsEnabled) _owner?.PickDeskItem(slot, id);
            };
            host.Children.Add(button);
        }
    }
}
