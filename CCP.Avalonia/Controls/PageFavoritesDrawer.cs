using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Controls;

// WPF d77b98ce0: a separate push drawer on non-Home pages, sharing Home's chip builder.
public sealed class PageFavoritesDrawer : Grid
{
    internal readonly Button Handle;
    internal readonly TextBlock Chevron = new() { Text = "‹", HorizontalAlignment = HorizontalAlignment.Center };
    internal readonly Grid Body = new() { Width = 0, ClipToBounds = true };
    internal readonly StackPanel FavoritesList = new(), RecentList = new();
    internal readonly TextBlock FavoritesEmpty = Caption("rail_favorites_empty"), RecentEmpty = Caption("rail_recent_empty");
    private readonly Border _rail, _face;
    private Color _hue;
    internal bool IsOpen { get; private set; }
    public PageFavoritesDrawer()
    {
        ColumnDefinitions = new ColumnDefinitions("22,Auto");
        var word = Caption("rail_favorites_drawer");
        var rotated = new LayoutTransformControl { LayoutTransform = new RotateTransform(-90), Child = word };
        var face = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        face.Children.Add(new TextBlock { Text = "★", HorizontalAlignment = HorizontalAlignment.Center });
        SetRow(rotated, 1); face.Children.Add(rotated);
        SetRow(Chevron, 2); face.Children.Add(Chevron);
        _face = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1.5), Child = face };
        Handle = new Button { Content = _face, Padding = new Thickness(0), Margin = new Thickness(2,5),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Handle.Bind(ToolTip.TipProperty, Text("tooltip_rail_favorites_drawer"));
        Handle.Bind(AutomationProperties.NameProperty, Text("rail_favorites_drawer"));
        Handle.Click += (_, _) => SetOpen(!IsOpen, true);
        Handle.PointerEntered += (_, _) => Paint(_hue);
        Handle.PointerExited += (_, _) => Paint(_hue);
        Handle.GotFocus += (_, _) => Paint(_hue);
        Handle.LostFocus += (_, _) => Paint(_hue);
        Children.Add(Handle);
        var stack = new StackPanel { Width = 69, Margin = new Thickness(4,10) };
        foreach (var c in new Control[] { Caption("rail_favorites_caption"), FavoritesList, FavoritesEmpty,
                     Caption("rail_recent_caption"), RecentList, RecentEmpty }) stack.Children.Add(c);
        _rail = new Border { Width = 87, Margin = new Thickness(0,5,5,5), CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right,
            Child = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled } };
        _rail.Bind(BackgroundProperty, new DynamicResourceExtension("ElevatedSurfaceBrush"));
        Body.Children.Add(_rail); SetColumn(Body,1); Children.Add(Body);
        Paint(Color.FromRgb(183,156,255));
    }
    private static Binding Text(string key) => new($"[{key}]") { Source = LocalizationManager.Instance };
    private static TextBlock Caption(string key)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        t.Bind(TextBlock.TextProperty, Text(key)); return t;
    }
    internal void Paint(Color hue)
    {
        _hue = hue;
        bool lit = Handle.IsPointerOver || Handle.IsKeyboardFocusWithin;
        _rail.BorderBrush = new SolidColorBrush(hue, 0.5);
        _face.Background = new SolidColorBrush(hue, lit ? 0.3 : 0.15);
        _face.BorderBrush = new SolidColorBrush(hue, lit ? 1 : 0.5);
    }
    internal void SetOpen(bool open, bool animate)
    {
        IsOpen = open; Chevron.Text = open ? "›" : "‹";
        Body.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions
            ? new Transitions { new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut() } } : null;
        Body.Width = open ? 92 : 0;
    }
    internal void OnNavigated(string? section)
    {
        SetOpen(false, false);
        IsVisible = !string.IsNullOrEmpty(section) && section != NavSections.Home;
    }
}
