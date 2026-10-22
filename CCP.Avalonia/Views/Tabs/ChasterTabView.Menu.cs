// Adapted from WPF ChasterTabView.xaml.cs:1147-1306 and ChasterTabView.Help.cs.
// ponytail: row/trailer FX and the browser trailer remain wire-views-tab-chaster-b.
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
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;
namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private readonly Dictionary<string, ToggleButton> _priceToggles = new();
    private readonly Dictionary<string, Border> _rowDims = new();

    private void MenuInit()
    {
        var (costs, earnBacks) = TabPageText.Split(TabPrices.All);
        foreach (var price in costs) CostRows.Children.Add(MenuRow(price, CostColour));
        foreach (var price in earnBacks) EarnRows.Children.Add(MenuRow(price, EarnColour));
        // The render proof frames the new menu; production keeps WPF's initial hero position.
        if (RenderProof.Rendering)
            AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { LimitsCard.IsVisible = true; Boards.BringIntoView(); });
        DetachedFromVisualTree += (_, _) => LocalizationManager.Instance.LanguageChanged -= MenuLanguageChanged;
        AttachedToVisualTree += (_, _) =>
        {
            LocalizationManager.Instance.LanguageChanged -= MenuLanguageChanged;
            LocalizationManager.Instance.LanguageChanged += MenuLanguageChanged;
        };
    }

    private void MenuLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var (id, row) in _priceToggles)
            ((Grid)row.Content!).Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().First().Text = TabMenuCopy.ShortName(id, Loc.Get);
        PaintStamps();
        PaintLimits(ChasterHead.Service?.Caps ?? TabLimits.Default);
    }

    private ToggleButton MenuRow(TabPrice price, Color colour)
    {
        var brush = new SolidColorBrush(colour);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var plate = new Panel { Background = new SolidColorBrush(Color.FromArgb(0x30, colour.R, colour.G, colour.B)) };
        if (TabMenuCopy.ArtFor(price.Id) is { } art)
        {
            try { plate.Children.Add(new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + art))), Stretch = Stretch.UniformToFill }); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] row art {Art}", art); }
        }
        var dim = new Border { Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x30)), Opacity = 0.62, IsHitTestVisible = false };
        plate.Children.Add(dim);
        grid.Children.Add(new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(8), ClipToBounds = true, Margin = new Thickness(0, 0, 10, 0), Child = plate });
        _rowDims[price.Id] = dim;
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = TabMenuCopy.ShortName(price.Id, Loc.Get), FontFamily = Display, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Brush("TextLightBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
        var whereRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 0) };
        var where = new TextBlock { FontSize = 10.5, Opacity = 0.85, Foreground = Brush("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        where.Bind(TextBlock.TextProperty, new Binding("[" + TabMenuCopy.WhereKey(price.Id) + "]") { Source = LocalizationManager.Instance });
        where.Bind(ToolTip.TipProperty, new Binding("[" + TabMenuCopy.WhereKey(price.Id) + "]") { Source = LocalizationManager.Instance });
        whereRow.Children.Add(where);
        var tier = TabMenuCopy.BadgeTier(price.Gate);
        if (tier > 0) whereRow.Children.Add(new TierBadge { Tier = tier, MaxWidthOverride = 46, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        words.SizeChanged += (_, e) => where.MaxWidth = Math.Max(0, e.NewSize.Width - (tier > 0 ? 52 : 0));
        words.Children.Add(whereRow);
        Grid.SetColumn(words, 1); grid.Children.Add(words);
        var stamp = new TextBlock { FontFamily = new FontFamily("Consolas, Courier New"), FontSize = 12.5, FontWeight = FontWeight.Bold, Foreground = brush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(stamp, 2); grid.Children.Add(stamp); WireStamp(price, stamp);
        var row = new ToggleButton { Theme = (ControlTheme)Resources["CirceRow"]!, Tag = price.Id, BorderBrush = brush, BorderThickness = new Thickness(1), Background = brush, Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 8, 8) };
        if (price.Id == CircesMisses.EventId)
        {
            // WPF (ChasterTabView.xaml.cs:1271): a bound, wrapping tip, so it follows a language switch.
            var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
            tip.Bind(TextBlock.TextProperty, new Binding("[chaster_misses_hint]") { Source = LocalizationManager.Instance });
            ToolTip.SetTip(row, tip);
        }
        row.Click += PriceToggle_Changed;
        _priceToggles[price.Id] = row;
        return row;
    }

    internal void ApplyPriceToggles()
    {
        var on = new HashSet<string>(CoreSettings.Current.ChasterPrices ?? new List<string>(), StringComparer.Ordinal);
        _loading = true;
        try
        {
            foreach (var (id, row) in _priceToggles)
            {
                row.IsChecked = on.Contains(id);
                _rowDims[id].Opacity = on.Contains(id) ? 0 : 0.62;
            }
            ChkFlashDodge.IsChecked = CoreSettings.Current.ChasterFlashDodge;
        }
        finally { _loading = false; }
        PaintStamps();
    }

    private void PriceToggle_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading || sender is not ToggleButton { Tag: string id } row) return;
        var settings = CoreSettings.Current;
        var next = new List<string>(settings.ChasterPrices ?? new List<string>());
        next.Remove(id); if (row.IsChecked == true) next.Add(id);
        settings.ChasterPrices = next;
        CoreSettings.Save();
        ApplyPriceToggles(); RefreshPresets(); RefreshHero();
    }

    private void ChkFlashDodge_Changed(object? sender, RoutedEventArgs e)
    {
        if (_loading) return;
        CoreSettings.Current.ChasterFlashDodge = ChkFlashDodge.IsChecked == true;
        CoreSettings.Save();
    }

    private void BtnHowItWorks_Click(object? sender, RoutedEventArgs e) => HowItWorksCard.IsVisible = !HowItWorksCard.IsVisible;
    internal void PaintMenuHelp()
    {
        var open = FiguresEditable();
        TxtMenuHint.Text = Loc.Get(open ? "chaster_menu_hint_edit" : "chaster_menu_hint_locked");
        var overrides = CoreSettings.Current.ChasterPriceOverrides;
        BtnResetPrices.IsVisible = open && overrides is { Count: > 0 } && overrides.Keys.Any(id => TabPriceEdit.IsEdited(id, overrides));
        var natashaOn = CoreSettings.Current.ChasterPrices?.Contains(NatashasFavourite.EventId) == true;
        TxtFlashDodgeSub.Text = natashaOn ? Loc.GetF("chaster_flash_dodge_sub", NatashasFavourite.DodgeMs / 1000, CircesTab.Format(TabPriceEdit.Effective(NatashasFavourite.EventId, overrides), signed: false)) : Loc.Get("chaster_flash_dodge_needs");
        FlashDodgeRow.Opacity = natashaOn ? 1 : 0.7;
    }

    private void BtnResetPrices_Click(object? sender, RoutedEventArgs e)
    {
        if (!FiguresEditable()) return;
        CancelPriceEdit();
        CoreSettings.Current.ChasterPriceOverrides = new();
        CoreSettings.Save();
        Serilog.Log.Information("[Chaster] prices reset to the table by the player"); // WPF Help.cs:45
        PaintStamps();
    }
}
