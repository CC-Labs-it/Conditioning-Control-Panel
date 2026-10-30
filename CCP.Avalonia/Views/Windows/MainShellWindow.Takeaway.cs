// WPF MainWindow.Takeaway: three pinned receipts plus the complete overflow tray.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.JustDrop;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _takeawayLoading, _takeawayReload, _takeawayTrayOpen;
        private int _takeawayMoreCount;
        private TextBlock? _takeawayMoreLabel;
        private IReadOnlyList<JustDropOrdersService.Order> _takeawayOrders = Array.Empty<JustDropOrdersService.Order>();
        internal Task TakeawayLoad { get; private set; } = Task.CompletedTask;
        private PresetsTabView? TakeawayTab => Named<PresetsTabView>("PresetsTab");
        private T? Takeaway<T>(string name) where T : Control => TakeawayTab?.FindControl<T>(name);

        private void HookTakeawayShelf()
        {
            EventHandler identity = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                JustDropOrdersService.Invalidate();
                PaintTakeawayShelf(Array.Empty<JustDropOrdersService.Order>());
                RefreshTakeawayShelf();
            });
            EventHandler language = (_, _) => PaintTakeawayShelf(_takeawayOrders);
            CoreAccount.UnifiedIdentityChanged += identity;
            LocalizationManager.Instance.LanguageChanged += language;
            Closed += (_, _) =>
            {
                CoreAccount.UnifiedIdentityChanged -= identity;
                LocalizationManager.Instance.LanguageChanged -= language;
            };
        }

        internal void RefreshTakeawayShelf()
        {
            if (TakeawayTab is null) return;
            // A refresh asked for mid-fetch (account switch) runs once the fetch lands.
            if (_takeawayLoading) { _takeawayReload = true; return; }
            _takeawayLoading = true;
            TakeawayLoad = LoadTakeawayShelfAsync();
        }

        private async Task LoadTakeawayShelfAsync()
        {
            try { PaintTakeawayShelf(await JustDropOrdersService.FetchAsync()); }
            catch (Exception ex) { Log.Debug("Takeaway refresh failed: {Kind}", ex.GetType().Name); }
            finally
            {
                _takeawayLoading = false;
                if (_takeawayReload) { _takeawayReload = false; RefreshTakeawayShelf(); }
            }
        }

        internal void PaintTakeawayShelf(IReadOnlyList<JustDropOrdersService.Order> orders)
        {
            if (Takeaway<StackPanel>("TakeawayShelf") is not { } shelf || Takeaway<StackPanel>("TakeawayTray") is not { } tray) return;
            _takeawayOrders = orders;
            shelf.Children.Clear(); tray.Children.Clear();
            _takeawayMoreLabel = null;
            int pinned = 0;
            foreach (var order in orders.Where(o => !string.IsNullOrWhiteSpace(o.Code)))
            {
                if (pinned < 3) { shelf.Children.Add(BuildTakeawayReceipt(order, false)); pinned++; }
                tray.Children.Add(BuildTakeawayReceipt(order, true));
            }
            _takeawayMoreCount = Math.Max(0, tray.Children.Count - pinned);
            if (_takeawayMoreCount > 0)
            {
                _takeawayMoreLabel = new TextBlock { Text = TakeawayText.More(_takeawayMoreCount, false), VerticalAlignment = VerticalAlignment.Center };
                var more = TakeawayChip("SdTakeawayChipAccent", () => SetTakeawayTrayOpen(!_takeawayTrayOpen));
                more.Name = "TakeawayMore"; more.Classes.Add("takeaway-affordance"); more.Child = _takeawayMoreLabel;
                ToolTip.SetTip(more, Loc.Get("tooltip_takeaway_more"));
                shelf.Children.Add(more);
            }
            SetTakeawayTrayOpen(false);
            if (JustDropService.DoorAvailable)
            {
                var door = TakeawayChip("SdTakeawayChipDoor", () => { if (JustDropService.DoorAvailable) OpenJustDrop(); });
                TakeawayTab?.PrepareChipFx(door); // WPF MainWindow.Takeaway.cs:499
                door.Name = "TakeawayDoor"; door.Classes.Add("takeaway-affordance");
                var line = Line(); var plus = IconGlyph.Of(IconKind.Add, size: 15); plus.Margin = new Thickness(0, 0, 6, 0); line.Children.Add(plus);
                line.Children.Add(new TextBlock { Text = Loc.Get("sd_takeaway_order"), FontWeight = FontWeight.Bold, FontSize = 11.5 });
                door.Child = line; ToolTip.SetTip(door, Loc.Get("tooltip_takeaway_order_drop")); shelf.Children.Add(door);
            }
            Takeaway<TextBlock>("TxtTakeawayCount")!.Text = orders.Count > 0 ? Loc.GetF("sd_takeaway_kept", orders.Count) : "";
            bool empty = pinned == 0 && !JustDropService.DoorAvailable;
            Takeaway<TextBlock>("TxtTakeawayEmpty")!.IsVisible = empty;
            shelf.IsVisible = !empty;
            Takeaway<Border>("TakeawayZone")!.IsVisible = true;
        }

        private void SetTakeawayTrayOpen(bool open)
        {
            _takeawayTrayOpen = open && _takeawayMoreCount > 0;
            if (Takeaway<Border>("TakeawayTrayHost") is { } host) host.IsVisible = _takeawayTrayOpen;
            if (_takeawayMoreLabel is { } label) label.Text = TakeawayText.More(_takeawayMoreCount, _takeawayTrayOpen);
        }

        private Border BuildTakeawayReceipt(JustDropOrdersService.Order order, bool row)
        {
            var chip = TakeawayChip(row ? "SdTakeawayRow" : "SdTakeawayChip", () => GameWindow.LaunchJustDropReplay(order.Code, this));
            if (!row) TakeawayTab?.PrepareChipFx(chip); // WPF MainWindow.Takeaway.cs:244
            chip.Tag = order;
            ToolTip.SetTip(chip, Loc.Get("tooltip_takeaway_replay"));
            var title = TakeawayTextBlock(TakeawayText.Name(order), row ? "SdTakeawayRowTitle" : "SdTakeawayChipTitle");
            var copy = TakeawayChip("SdTakeawayCopy", () => _ = CopyTakeawayLinkAsync(order));
            copy.Name = "TakeawayCopy";
            copy.Child = IconGlyph.Of(IconKind.Link, size: 10);
            ToolTip.SetTip(copy, Loc.Get("tooltip_takeaway_copy_link"));
            var icon = IconGlyph.Of(IconKind.Box, size: row ? 13 : 12);
            if (row)
            {
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto") };
                icon.Margin = new Thickness(0, 0, 8, 0);
                Control[] cells = { icon, title, RowMeta(TakeawayText.Minutes(order), 58, false),
                    RowMeta(TakeawayText.Date(order), 62, false), RowMeta(TakeawayText.Age(order, DateTimeOffset.UtcNow), 78, true), copy };
                for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
                chip.Child = grid;
            }
            else
            {
                var line = Line(); title.MaxWidth = 120; icon.Margin = new Thickness(0, 0, 6, 0);
                line.Children.Add(icon); line.Children.Add(title);
                line.Children.Add(TakeawayTextBlock(TakeawayText.Meta(order), "SdTakeawayChipMeta"));
                line.Children.Add(copy); chip.Child = line;
            }
            return chip;
        }

        internal async Task CopyTakeawayLinkAsync(JustDropOrdersService.Order order)
        {
            if (string.IsNullOrWhiteSpace(order.Code)) return;
            try
            {
                if (Clipboard is not { } clipboard) throw new InvalidOperationException("Clipboard unavailable");
                await clipboard.SetTextAsync(JustDropService.TasteUrl(order.Code));
                App.Notifications.Show(Loc.Get("toast_taste_link_copied"), NotificationType.Success, TimeSpan.FromSeconds(4));
            }
            catch (Exception ex)
            {
                Log.Debug("Takeaway clipboard failed: {Kind}", ex.GetType().Name);
                App.Notifications.Show(Loc.Get("toast_taste_link_copy_failed"), NotificationType.Warning, TimeSpan.FromSeconds(4));
            }
        }

        private static StackPanel Line() => new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private Border TakeawayChip(string theme, Action click)
        {
            var chip = new Border { Theme = TakeawayTab?.Resources[theme] as ControlTheme, Focusable = true };
            chip.PointerReleased += (_, e) => { if (e.InitialPressMouseButton != MouseButton.Left) return; e.Handled = true; click(); };
            chip.KeyDown += (_, e) => { if (e.Key is not (Key.Enter or Key.Space)) return; e.Handled = true; click(); };
            return chip;
        }
        private TextBlock TakeawayTextBlock(string text, string theme) => new() { Text = text, Theme = TakeawayTab?.Resources[theme] as ControlTheme };
        private TextBlock RowMeta(string text, double width, bool age)
        {
            var block = TakeawayTextBlock(text, age ? "SdTakeawayRowAge" : "SdTakeawayRowMeta");
            block.MinWidth = width; return block;
        }
    }
}
