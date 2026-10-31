// PORTED from ConditioningControlPanel/Controls/NavRail/PageFavoritesDrawer.cs:14-214.
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
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed class PageFavoritesDrawer : Grid
{
    internal Button Handle { get; }
    internal Grid Body { get; }
    internal Border Rail { get; }
    internal StackPanel FavoritesList { get; } = new();
    internal StackPanel RecentList { get; } = new();
    internal TextBlock FavoritesEmpty { get; }
    internal TextBlock RecentEmpty { get; }
    internal bool IsOpen { get; private set; }
    private readonly IconGlyph _chevron;
    private readonly Border _face;
    private Color _hue;

    public PageFavoritesDrawer()
    {
        ColumnDefinitions = new ColumnDefinitions("22,Auto");
        _chevron = new IconGlyph { Kind = IconKind.ChevronLeft, Size = 16, HorizontalAlignment = HorizontalAlignment.Center };
        // WPF PageFavoritesDrawer.cs:66-72: the handle word inherits its mod ink.
        var word = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        Bind(word, TextBlock.TextProperty, "rail_favorites_drawer");
        var rotated = new LayoutTransformControl { LayoutTransform = new RotateTransform(-90), Child = word,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var face = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        face.Children.Add(new IconGlyph { Kind = IconKind.Star, Variant = IconVariant.Filled, Size = 13, HorizontalAlignment = HorizontalAlignment.Center });
        SetRow(rotated, 1); SetRow(_chevron, 2);
        face.Children.Add(rotated); face.Children.Add(_chevron);
        _face = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = face };
        Handle = new Button { Cursor = new Cursor(StandardCursorType.Hand), Content = _face, Margin = new Thickness(2, 5), Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Template = new FuncControlTemplate<Button>((b, _) => new ContentPresenter { Content = b.Content }) };
        Bind(Handle, ToolTip.TipProperty, "tooltip_rail_favorites_drawer");
        Bind(Handle, AutomationProperties.NameProperty, "rail_favorites_drawer");
        Handle.Click += (_, _) => SetOpen(!IsOpen, true);
        Handle.PointerEntered += (_, _) => PaintHandle();
        Handle.PointerExited += (_, _) => PaintHandle();
        Handle.GotFocus += (_, _) => PaintHandle();
        Handle.LostFocus += (_, _) => PaintHandle();
        Children.Add(Handle);
        var stack = new StackPanel { Width = 69, Margin = new Thickness(4, 10), HorizontalAlignment = HorizontalAlignment.Center };
        FavoritesEmpty = Caption("rail_favorites_empty", 8);
        RecentEmpty = Caption("rail_recent_empty", 8);
        var favorites = Caption("rail_favorites_caption", 9);
        var recent = Caption("rail_recent_caption", 9);
        recent.Margin = new Thickness(0, 8, 0, 6);
        foreach (var child in new Control[] { favorites, FavoritesList, FavoritesEmpty, recent, RecentList, RecentEmpty })
            stack.Children.Add(child);
        Rail = new Border { Width = 87, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 5, 5),
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Rail.Bind(BackgroundProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("ElevatedSurfaceBrush"));
        Body = new Grid { Width = 0, ClipToBounds = true };
        Body.Children.Add(Rail); SetColumn(Body, 1); Children.Add(Body);
        Paint(AmbientFxCanvas.Env.GlowColor);
    }

    private static void Bind(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target.Bind(property, new Binding($"[{key}]") { Source = LocalizationManager.Instance });

    private static TextBlock Caption(string key, double size)
    {
        // WPF SettingsTabView.xaml:295-309: Home's caption and empty text styles.
        var text = new TextBlock { FontSize = size, TextAlignment = TextAlignment.Center,
            TextWrapping = size == 8 ? TextWrapping.Wrap : TextWrapping.NoWrap,
            FontWeight = size == 9 ? FontWeight.Bold : FontWeight.Normal,
            Margin = new Thickness(0, 0, 0, size == 8 ? 6 : 3), Opacity = size == 8 ? .85 : 1 };
        Bind(text, TextBlock.TextProperty, key);
        text.Bind(TextBlock.ForegroundProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
            size == 8 ? "TextMutedBrush" : "SecondaryBrush"));
        return text;
    }

    // WPF PageFavoritesDrawer.cs:141-155: active mod hue, pointer and keyboard focus paint.
    internal void Paint(Color hue)
    {
        _hue = hue;
        Rail.BorderBrush = Tint(0x80);
        PaintHandle();
    }
    private SolidColorBrush Tint(byte alpha) => new(Color.FromArgb(alpha, _hue.R, _hue.G, _hue.B));
    private void PaintHandle()
    {
        bool lit = Handle.IsPointerOver || Handle.IsFocused;
        _face.Background = Tint(lit ? (byte)0x4D : (byte)0x26);
        _face.BorderBrush = Tint(lit ? (byte)0xFF : (byte)0x80);
        Handle.Foreground = new SolidColorBrush(global::ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.Lighten(_hue, lit ? .55 : .18));
    }

    // WPF PageFavoritesDrawer.cs:185-214: no persistence, 200 ms slide obeys MotionFx.
    internal void SetOpen(bool open, bool animate)
    {
        IsOpen = open;
        _chevron.Kind = open ? IconKind.ChevronRight : IconKind.ChevronLeft;
        Body.Transitions = animate && IsVisible && AmbientFxCanvas.Env.AllowTransitions
            ? new Transitions { new DoubleTransition { Property = WidthProperty,
                Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() } } : null;
        Body.Width = open ? 92 : 0;
    }
    internal void OnNavigated(string? section)
    {
        SetOpen(false, false);
        IsVisible = !string.IsNullOrEmpty(section) && section != NavSections.Home;
    }
}
