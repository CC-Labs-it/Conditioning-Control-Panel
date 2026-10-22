using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.JustDrop;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class PresetsTabView
{
    private bool _takeawayLoading;

    private void OnJustDropDoorChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = RefreshTakeawayAsync());

    internal async Task RefreshTakeawayAsync()
    {
        if (_takeawayLoading) return;
        _takeawayLoading = true;
        try { PaintTakeaway(await JustDropOrdersService.FetchAsync()); }
        catch (Exception ex) { Log.Debug(ex, "Takeaway drawer unavailable"); }
        finally { _takeawayLoading = false; }
    }

    /// <summary>WPF PaintTakeawayShelf: newest three, overflow containing every order, shop last.</summary>
    internal void PaintTakeaway(IReadOnlyList<JustDropOrdersService.Order> orders)
    {
        TakeawayShelf.Children.Clear();
        TakeawayTray.Children.Clear();
        TakeawayTrayHost.IsVisible = false;
        foreach (var order in orders)
        {
            if (TakeawayShelf.Children.Count < 3) TakeawayShelf.Children.Add(TakeawayReceipt(order, tray: false));
            TakeawayTray.Children.Add(TakeawayReceipt(order, tray: true));
        }
        if (orders.Count > 3)
        {
            var label = new IconLabel { Text = Loc.GetF("sd_takeaway_more", orders.Count - 3), Kind = IconKind.ChevronDown };
            var more = TakeawayChip("SdTakeawayChipAccent", label, () =>
            {
                TakeawayTrayHost.IsVisible = !TakeawayTrayHost.IsVisible;
                label.Kind = TakeawayTrayHost.IsVisible ? IconKind.ChevronUp : IconKind.ChevronDown;
            });
            more.Name = "TakeawayMore";
            ToolTip.SetTip(more, Loc.Get("tooltip_takeaway_more"));
            TakeawayShelf.Children.Add(more);
        }
        if (JustDropService.DoorAvailable)
        {
            var door = TakeawayChip("SdTakeawayChipDoor", new IconLabel
            { Kind = IconKind.Add, Text = Loc.Get("sd_takeaway_order") }, () => Shell?.ShowTab("justdrop"));
            door.Name = "TakeawayShop";
            ToolTip.SetTip(door, Loc.Get("tooltip_takeaway_order_drop"));
            TakeawayShelf.Children.Add(door);
        }
        TxtTakeawayCount.Text = orders.Count > 0 ? Loc.GetF("sd_takeaway_kept", orders.Count) : "";
        TakeawayShelf.IsVisible = orders.Count > 0 || JustDropService.DoorAvailable;
        TxtTakeawayEmpty.IsVisible = !TakeawayShelf.IsVisible;
    }

    private Border TakeawayReceipt(JustDropOrdersService.Order order, bool tray)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(new IconGlyph { Kind = IconKind.Box, Size = 12, Margin = new Thickness(0, 0, 6, 0) });
        line.Children.Add(new TextBlock
        {
            Theme = TabTheme(tray ? "SdTakeawayRowTitle" : "SdTakeawayChipTitle"),
            Text = order.DisplayName, MaxWidth = tray ? double.PositiveInfinity : 120, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        line.Children.Add(new TextBlock
        {
            Theme = TabTheme(tray ? "SdTakeawayRowMeta" : "SdTakeawayChipMeta"), Text = order.Meta,
            Margin = new Thickness(8, 0, 0, 0),
        });
        var copy = TakeawayChip("SdTakeawayCopy", new IconGlyph { Kind = IconKind.Link, Size = 10 },
            () => _ = CopyTakeaway(order));
        copy.Name = "TakeawayCopy";
        ToolTip.SetTip(copy, Loc.Get("tooltip_takeaway_copy_link"));
        line.Children.Add(copy);
        Control content = line;
        if (tray)
        {
            line.Children.Remove(copy);
            var title = line.Children[1];
            line.Children.Remove(title);
            var icon = line.Children[0];
            line.Children.Remove(icon);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto") };
            grid.Children.Add(icon);
            Grid.SetColumn(title, 1);
            grid.Children.Add(title);
            var values = new[] { order.MinutesLabel, order.DateLabel, order.AgeLabel(DateTimeOffset.UtcNow) };
            var widths = new[] { 58, 62, 78 };
            for (int i = 0; i < values.Length; i++)
            {
                var cell = new TextBlock
                {
                    Text = values[i], MinWidth = widths[i], Theme = TabTheme(i == 2 ? "SdTakeawayRowAge" : "SdTakeawayRowMeta"),
                };
                Grid.SetColumn(cell, i + 2);
                grid.Children.Add(cell);
            }
            Grid.SetColumn(copy, 5);
            grid.Children.Add(copy);
            content = grid;
        }
        var chip = TakeawayChip(tray ? "SdTakeawayRow" : "SdTakeawayChip", content, () =>
        {
            if (Shell is { } owner) GameWindow.LaunchJustDropReplay(order.Code, owner);
        });
        chip.Tag = order;
        ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_replay"));
        return chip;
    }

    private Border TakeawayChip(string theme, Control child, Action action)
    {
        var chip = new Border { Theme = TabTheme(theme), Child = child, Focusable = true, Cursor = new Cursor(StandardCursorType.Hand) };
        chip.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            e.Handled = true;
            action();
        };
        chip.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space)) return;
            e.Handled = true;
            action();
        };
        return chip;
    }

    private async Task CopyTakeaway(JustDropOrdersService.Order order)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(JustDropService.TasteUrl(order.Code));
            App.Notifications.Show(Loc.Get("toast_taste_link_copied"), Helpers.NotificationType.Success, TimeSpan.FromSeconds(4));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Takeaway copy failed");
            App.Notifications.Show(Loc.Get("toast_taste_link_copy_failed"), Helpers.NotificationType.Warning, TimeSpan.FromSeconds(4));
        }
    }
}
