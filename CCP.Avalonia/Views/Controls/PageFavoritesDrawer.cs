using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

/// <summary>WPF PageFavoritesDrawer (d77b98ce0): a 22 px handle and a 92 px body in their own
/// page column. The shell supplies the same chips as Home. Navigation always closes it.</summary>
public sealed class PageFavoritesDrawer : Grid
{
    internal Button Handle { get; }
    internal IconGlyph Chevron { get; }
    internal Grid Body { get; }
    internal Border Rail { get; }
    internal StackPanel FavoritesList { get; } = new();
    internal StackPanel RecentList { get; } = new();
    internal TextBlock FavoritesEmpty { get; }
    internal TextBlock RecentEmpty { get; }
    internal TextBlock FavoritesCaption { get; }
    internal TextBlock RecentCaption { get; }
    internal bool IsOpen { get; private set; }
    private readonly Border _face;
    private readonly TextBlock _word;
    private readonly IconGlyph _star;
    private Color _hue = Color.FromRgb(0xB7, 0x9C, 0xFF);

    public PageFavoritesDrawer()
    {
        ColumnDefinitions = new ColumnDefinitions("22,Auto");
        Chevron = new IconGlyph { Kind = IconKind.ChevronLeft, Size = 16, Margin = new Thickness(0, 0, 0, 7), HorizontalAlignment = HorizontalAlignment.Center };
        _star = new IconGlyph { Kind = IconKind.Star, Variant = IconVariant.Filled, Size = 13, Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        _word = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        Bind(_word, TextBlock.TextProperty, "rail_favorites_drawer");
        var word = new LayoutTransformControl { Child = _word, LayoutTransform = new RotateTransform(-90), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        SetRow(word, 1); SetRow(Chevron, 2);
        content.Children.Add(_star); content.Children.Add(word); content.Children.Add(Chevron);
        _face = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = content };
        Handle = new Button
        {
            Content = _face, Margin = new Thickness(2, 5, 2, 5), Padding = new Thickness(0), Cursor = FriendsDrawer.Hand(), // WPF PageFavoritesDrawer.cs:89
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Template = new FuncControlTemplate<Button>((button, _) => new ContentPresenter
            {
                Content = button.Content, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            }),
        };
        Bind(Handle, ToolTip.TipProperty, "tooltip_rail_favorites_drawer");
        Bind(Handle, AutomationProperties.NameProperty, "rail_favorites_drawer");
        Handle.Click += (_, _) => SetOpen(!IsOpen, true);
        Handle.PointerEntered += (_, _) => PaintHandle();
        Handle.PointerExited += (_, _) => PaintHandle();
        Handle.GotFocus += (_, _) => PaintHandle();
        Handle.LostFocus += (_, _) => PaintHandle();
        Children.Add(Handle);
        FavoritesCaption = Caption("rail_favorites_caption");
        RecentCaption = Caption("rail_recent_caption");
        RecentCaption.Margin = new Thickness(0, 8, 0, 6);
        FavoritesEmpty = Caption("rail_favorites_empty");
        RecentEmpty = Caption("rail_recent_empty");
        foreach (var caption in new[] { FavoritesCaption, RecentCaption })
        {
            caption.FontSize = 9; caption.FontWeight = FontWeight.Bold;
            caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("SecondaryBrush"));
        }
        FavoritesCaption.Margin = new Thickness(0, 0, 0, 3);
        foreach (var empty in new[] { FavoritesEmpty, RecentEmpty })
        {
            empty.FontSize = 8; empty.Opacity = 0.85; empty.Margin = new Thickness(0, 0, 0, 6);
            empty.Bind(TextBlock.ForegroundProperty, empty.GetResourceObservable("TextMutedBrush"));
        }
        var stack = new StackPanel { Width = 69, Margin = new Thickness(4, 10), HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in new Control[] { FavoritesCaption, FavoritesList, FavoritesEmpty, RecentCaption, RecentList, RecentEmpty }) stack.Children.Add(child);
        Rail = new Border
        {
            Width = 87, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 5, 5),
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
        Rail.Bind(Border.BackgroundProperty, Rail.GetResourceObservable("ElevatedSurfaceBrush"));
        Body = new Grid { Width = 0, ClipToBounds = true };
        Body.Children.Add(Rail); SetColumn(Body, 1); Children.Add(Body);
        Paint(_hue);
    }

    private static void Bind(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target.Bind(property, new Binding($"[{key}]") { Source = LocalizationManager.Instance });

    private static TextBlock Caption(string key)
    {
        var caption = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        Bind(caption, TextBlock.TextProperty, key);
        return caption;
    }

    internal void Paint(Color hue)
    {
        _hue = hue;
        Rail.BorderBrush = Tint(hue, 0x80);
        PaintHandle();
    }

    private void PaintHandle()
    {
        bool lit = Handle.IsPointerOver || Handle.IsKeyboardFocusWithin;
        _face.Background = Tint(_hue, lit ? (byte)0x4D : (byte)0x26);
        _face.BorderBrush = Tint(_hue, lit ? (byte)0xFF : (byte)0x80);
        var ink = Tint(Tabs.SettingsTabView.Lighten(_hue, lit ? 0.55 : 0.18), 0xFF);
        _word.Foreground = ink; _star.Foreground = ink; Chevron.Foreground = ink;
    }

    private static IBrush Tint(Color hue, byte alpha) => new SolidColorBrush(Color.FromArgb(alpha, hue.R, hue.G, hue.B));

    internal void SetOpen(bool open, bool animate)
    {
        IsOpen = open;
        Chevron.Kind = open ? IconKind.ChevronRight : IconKind.ChevronLeft;
        Body.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions && IsVisible
            ? new Transitions { new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() } }
            : null;
        Body.Width = open ? 92 : 0;
    }

    internal void OnNavigated(string? section)
    {
        SetOpen(false, false);
        IsVisible = !string.IsNullOrEmpty(section) && section != NavSections.Home;
    }
}
