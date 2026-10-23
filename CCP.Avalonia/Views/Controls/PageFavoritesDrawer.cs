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
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

/// <summary>WPF Controls/NavRail/PageFavoritesDrawer: pushes every non-Home page by 92 DIP,
/// starts closed and closes on navigation. Shares Home's chips; never writes Home's open setting.</summary>
public sealed class PageFavoritesDrawer : Grid
{
    internal Button Handle { get; }
    internal Grid Body { get; }
    internal Border Rail { get; }
    internal StackPanel FavoritesList { get; } = new() { Name = "PageFavoritesList" };
    internal StackPanel RecentList { get; } = new();
    internal TextBlock FavoritesEmpty { get; }
    internal TextBlock RecentEmpty { get; }
    internal TextBlock FavoritesCaption { get; }
    internal TextBlock RecentCaption { get; }
    internal bool IsOpen { get; private set; }
    private readonly IconGlyph _chevron;
    private readonly Border _face;
    private readonly IconGlyph _star;
    private readonly TextBlock _word;
    private Color _hue;

    public PageFavoritesDrawer()
    {
        ColumnDefinitions = new ColumnDefinitions("22,Auto");
        _chevron = IconGlyph.Of(IconKind.ChevronLeft, size: 16);
        _chevron.Margin = new Thickness(0, 0, 0, 7);
        _star = IconGlyph.Of(IconKind.Star, size: 13);
        _star.Margin = new Thickness(0, 7, 0, 0);
        _word = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
        BindText(_word, TextBlock.TextProperty, "rail_favorites_drawer");
        var rotated = new LayoutTransformControl { LayoutTransform = new RotateTransform(-90), Child = _word,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var face = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        SetRow(rotated, 1); SetRow(_chevron, 2);
        face.Children.Add(_star); face.Children.Add(rotated); face.Children.Add(_chevron);
        _face = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = face };
        Handle = new Button { Name = "PageFavoritesHandle", Content = _face, Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(2, 5, 2, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Template = new FuncControlTemplate<Button>((b, _) => new ContentPresenter { Content = b.Content }) };
        BindText(Handle, ToolTip.TipProperty, "tooltip_rail_favorites_drawer");
        BindText(Handle, AutomationProperties.NameProperty, "rail_favorites_drawer");
        Handle.Click += (_, _) => SetOpen(!IsOpen, animate: true);
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
            caption.FontSize = 9;
            caption.FontWeight = FontWeight.Bold;
            caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("SecondaryBrush"));
        }
        FavoritesCaption.Margin = new Thickness(0, 0, 0, 3);
        foreach (var empty in new[] { FavoritesEmpty, RecentEmpty })
        {
            empty.FontSize = 8;
            empty.Opacity = 0.85;
            empty.Margin = new Thickness(0, 0, 0, 6);
            empty.Bind(TextBlock.ForegroundProperty, empty.GetResourceObservable("TextMutedBrush"));
        }
        var stack = new StackPanel { Margin = new Thickness(4, 10), Width = 69, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var child in new Control[] { FavoritesCaption, FavoritesList, FavoritesEmpty, RecentCaption, RecentList, RecentEmpty })
            stack.Children.Add(child);
        Rail = new Border { Width = 87, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 5, 5, 5),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack } };
        Rail.Bind(BackgroundProperty, Rail.GetResourceObservable("ElevatedSurfaceBrush"));
        Body = new Grid { Name = "PageFavoritesBody", Width = 0, ClipToBounds = true, Children = { Rail } };
        SetColumn(Body, 1);
        Children.Add(Body);
        var scope = new NameScope();
        NameScope.SetNameScope(this, scope);
        scope.Register(Handle.Name!, Handle);
        scope.Register(Body.Name!, Body);
        scope.Register(FavoritesList.Name!, FavoritesList);
        Paint(AmbientFxCanvas.Env.GlowColor);
    }

    private static void BindText(Control target, AvaloniaProperty property, string key) =>
        target.Bind(property, new Binding($"[{key}]") { Source = LocalizationManager.Instance });

    private static TextBlock Caption(string key)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        BindText(text, TextBlock.TextProperty, key);
        return text;
    }

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
        var ink = new SolidColorBrush(Views.Tabs.SettingsTabView.Lighten(_hue, lit ? 0.55 : 0.18));
        _word.Foreground = _star.Foreground = _chevron.Foreground = ink;
    }

    internal void SetOpen(bool open, bool animate)
    {
        IsOpen = open;
        _chevron.Kind = open ? IconKind.ChevronRight : IconKind.ChevronLeft;
        Body.Transitions = animate && IsEffectivelyVisible && AmbientFxCanvas.Env.AllowTransitions
            ? new Transitions { new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() } }
            : null;
        Body.Width = open ? 92 : 0;
    }

    internal void OnNavigated(string? section)
    {
        SetOpen(false, animate: false);
        IsVisible = !string.IsNullOrEmpty(section) && section != NavSections.Home;
    }
}
