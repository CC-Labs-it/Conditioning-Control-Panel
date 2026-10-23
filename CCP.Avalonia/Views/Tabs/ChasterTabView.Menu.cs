// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: 5. the menu (BuildMenu, Row,
// PaintRowLit, PriceToggle_Changed :1176-1290), ApplyPriceToggles (:1149) and the red flash switch
// (ChkFlashDodge_Changed :1168), on Core TabPrices / TabPageText / TabMenuCopy.
// ponytail: the row trailer (hover popup, Row_MouseEnter/Leave) and FxRow / FxKeyTurned (Fx.cs) are not
// ported (views-tab-chaster).
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private static readonly FontFamily Mono = new("Consolas, Courier New");
        private readonly Dictionary<string, ToggleButton> _priceToggles = new();
        private readonly Dictionary<string, Border> _rowDims = new();
        private bool _menuBuilt;

        internal IReadOnlyDictionary<string, ToggleButton> PriceRows => _priceToggles;

        internal void BuildMenu()
        {
            if (_menuBuilt) return;
            _menuBuilt = true;
            var (costs, earnBacks) = TabPageText.Split(TabPrices.All);
            foreach (var price in costs) CostRows.Children.Add(Row(price, CostColour));
            foreach (var price in earnBacks) EarnRows.Children.Add(Row(price, EarnColour));
            JackpotArt.Source = Helpers.ModArt.TryLoad(TabMenuCopy.ArtFor(TabMenuCopy.JackpotId), 88);
        }

        /// <summary>Push the saved set onto the rows. Never the other way round: the settings list
        /// is the truth and the rows are a view of it.</summary>
        internal void ApplyPriceToggles()
        {
            BuildMenu();
            var on = new HashSet<string>(CoreSettings.Current.ChasterPrices ?? new List<string>(), StringComparer.Ordinal);
            _loading = true;
            try
            {
                foreach (var (id, row) in _priceToggles)
                {
                    row.IsChecked = on.Contains(id);
                    PaintRowLit(id, on.Contains(id));
                }
                ChkFlashDodge.IsChecked = CoreSettings.Current.ChasterFlashDodge;
            }
            finally { _loading = false; }
            PaintStamps();
        }

        /// <summary>A picture, a short name, where it happens with the tier sign, and the price on a
        /// stamp. The row that charges for staying away says how in its tooltip.</summary>
        private ToggleButton Row(TabPrice price, Color colour)
        {
            var brush = new SolidColorBrush(colour);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

            // the picture, dimmed to a shade while the row is off
            var thumb = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0) };
            var plate = new Panel();
            if (Helpers.ModArt.TryLoad(TabMenuCopy.ArtFor(price.Id), 80) is { } art)
                plate.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
            else plate.Background = new SolidColorBrush(Color.FromArgb(0x30, colour.R, colour.G, colour.B));
            var dim = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x30)), Opacity = 0.62, IsHitTestVisible = false };
            plate.Children.Add(dim);
            thumb.Child = plate;
            grid.Children.Add(thumb);
            _rowDims[price.Id] = dim;

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock
            {
                Text = TabMenuCopy.ShortName(price.Id, Loc.Get), FontFamily = Display, FontSize = 13.5, FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("TextLightBrush"),
            });
            var whereRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
            var where = new TextBlock { FontSize = 10.5, Opacity = 0.85, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Brush("TextMutedBrush") };
            where.Bind(TextBlock.TextProperty, Bound(TabMenuCopy.WhereKey(price.Id)));
            where.Bind(ToolTip.TipProperty, Bound(TabMenuCopy.WhereKey(price.Id)));
            whereRow.Children.Add(where);
            var tier = TabMenuCopy.BadgeTier(price.Gate);
            if (tier > 0)
                whereRow.Children.Add(new TierBadge { Tier = tier, MaxWidthOverride = 46, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            words.Children.Add(whereRow);
            // WPF TAB-13: the horizontal row measures at unlimited width, so cap the line at the column less the sign.
            var signRoom = tier > 0 ? 52.0 : 0.0;
            words.SizeChanged += (_, e) => where.MaxWidth = Math.Max(0, e.NewSize.Width - signRoom);
            Grid.SetColumn(words, 1);
            grid.Children.Add(words);

            var stamp = new TextBlock
            {
                Text = TabPageText.Price(price, Loc.Get("chaster_each")),
                FontFamily = Mono, FontSize = 12.5, FontWeight = FontWeight.Bold,
                Foreground = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetColumn(stamp, 2);
            grid.Children.Add(stamp);
            WireStamp(price, stamp);

            var row = new ToggleButton
            {
                Theme = (ControlTheme)this.FindResource("CirceRow")!,
                Tag = price.Id,
                Background = brush,
                BorderBrush = brush,
                Content = grid,
            };
            if (price.Id == CircesMisses.EventId)
            {
                var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
                tip.Bind(TextBlock.TextProperty, Bound("chaster_misses_hint"));
                ToolTip.SetTip(row, tip);
            }
            row.Click += PriceToggle_Changed;
            _priceToggles[price.Id] = row;
            return row;
        }

        private void PaintRowLit(string id, bool on)
        {
            if (_rowDims.TryGetValue(id, out var dim)) dim.Opacity = on ? 0 : 0.62;
        }

        private static Binding Bound(string key) =>
            new($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay };

        private void PriceToggle_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading || sender is not ToggleButton { Tag: string id } row) return;
            var settings = CoreSettings.Current;
            var on = row.IsChecked == true;
            // A new list every time: the service reads the setting from another thread.
            var next = new List<string>(settings.ChasterPrices ?? new List<string>());
            next.Remove(id);
            if (on) next.Add(id);
            settings.ChasterPrices = next;
            CoreSettings.Save();
            PaintRowLit(id, on);
            // One hand-flipped row can land exactly on a preset, or step off one.
            RefreshPresets();
        }

        /// <summary>Red flashes with a 4 s ring to dodge them, or none at all (the default).</summary>
        private void ChkFlashDodge_Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            CoreSettings.Current.ChasterFlashDodge = ChkFlashDodge.IsChecked == true;
            CoreSettings.Save();
        }
    }
}
