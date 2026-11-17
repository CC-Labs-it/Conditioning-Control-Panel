using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;


namespace ConditioningControlPanel.Controls;

/// <summary>
/// The gold SHOP tag (owner, 2026-11-17: "write SHOP in bold golden on the launcher, and whenever we can open the
/// shop"). One small sticker that rides inside every door into the shop sheet: the launcher's wallet chip, the panel's
/// wallet, the Studio "More in the shop" chip, EMI's desk Prize Counter button. It is part of its door, not a button of
/// its own: a click on it is the door's click, and it reads the door's hover and press. Depth law: raised at rest (the
/// house sheen, one lamp top-left), lifts a pixel on hover, sits in a well while pressed or while the sheet it opened
/// is down (<see cref="CounterSheetView.PressChip"/>). Gold comes from the house tier-gold brushes, never a hand pick.
/// Words: <c>shop_door_label</c> (shown and the automation name), <c>shop_door_tip</c> (the tooltip), both live-bound.
/// </summary>
public sealed class ShopTag : Border
{
    public enum Look { Rest, Hot, Pressed }

    /// <summary>The tag's face for its door's state: pressed (or held in by an open sheet) wins over hover.</summary>
    public static Look LookOf(bool hot, bool down, bool pressedIn) => down || pressedIn ? Look.Pressed : hot ? Look.Hot : Look.Rest;

    /// <summary>How far the word sits from rest, in DIPs (up is negative): a pixel up on hover, a pixel down in the well.
    /// Gentle visuals keep it still (fades only), so only the brushes change there.</summary>
    public static double FaceDip(Look look, bool gentle) => gentle ? 0 : look switch { Look.Hot => -1, Look.Pressed => 1, _ => 0 };

    private readonly TextBlock _word = new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontWeight = FontWeights.Black,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        TextTrimming = TextTrimming.None,
    };
    private readonly Border _sheen = new() { IsHitTestVisible = false };
    private readonly TranslateTransform _face = new();
    private ButtonBase? _door;
    private bool _pressedIn;
    private Look _shown = Look.Rest;
    private static readonly DependencyPropertyDescriptor PressedDesc = DependencyPropertyDescriptor.FromProperty(ButtonBase.IsPressedProperty, typeof(ButtonBase));

    public ShopTag()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        _word.RenderTransform = _face;
        var grid = new Grid();
        grid.Children.Add(_sheen);
        grid.Children.Add(_word);
        Child = grid;
        _word.SetBinding(TextBlock.TextProperty, Bound("shop_door_label"));
        SetBinding(AutomationProperties.NameProperty, Bound("shop_door_label"));
        SetBinding(ToolTipProperty, Bound("shop_door_tip"));
        ApplySize();
        Paint(Look.Rest, animate: false);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    /// <summary>The small form for a tight door (the panel wallet's label line): smaller word, no vertical padding.</summary>
    public bool Compact { get => _compact; set { _compact = value; ApplySize(); } }
    private bool _compact;

    /// <summary>ON IS PRESSED IN: true while the sheet this door opened is down.</summary>
    public bool PressedIn { get => _pressedIn; set { if (_pressedIn == value) return; _pressedIn = value; Refresh(); } }

    /// <summary>A door's content: its words, then the tag (for a plain text button that opens the shop).</summary>
    public static FrameworkElement Beside(string words)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new ShopTag());
        return row;
    }

    /// <summary>Every tag inside a door (the logical tree, then the visual tree for templated content).</summary>
    public static IEnumerable<ShopTag> In(DependencyObject? root)
    {
        if (root == null) yield break;
        var seen = new HashSet<ShopTag>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is ShopTag t && seen.Add(t)) yield return t;
            foreach (var c in LogicalTreeHelper.GetChildren(n)) if (c is DependencyObject d) stack.Push(d);
            if (n is Visual or System.Windows.Media.Media3D.Visual3D)
            {
                int count = 0;
                try { count = VisualTreeHelper.GetChildrenCount(n); } catch (InvalidOperationException) { }
                for (int i = 0; i < count; i++) stack.Push(VisualTreeHelper.GetChild(n, i));
            }
        }
    }

    private void ApplySize()
    {
        _word.FontSize = _compact ? 8 : 11;
        _word.Margin = _compact ? new Thickness(0) : new Thickness(0, 0, 0, 0.5);
        if (_compact) { _word.LineHeight = 9; _word.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; }
        else { _word.ClearValue(TextBlock.LineHeightProperty); _word.ClearValue(TextBlock.LineStackingStrategyProperty); }
        Padding = _compact ? new Thickness(4, 0, 4, 0) : new Thickness(6, 1, 6, 1);
        CornerRadius = new CornerRadius(_compact ? 3.5 : 5);
        _sheen.CornerRadius = new CornerRadius(_compact ? 2.5 : 4);
        _sheen.Margin = new Thickness(-Padding.Left + 1, -Padding.Top, -Padding.Right + 1, -Padding.Bottom);
        BorderThickness = new Thickness(_compact ? 1 : 1.25);
        Margin = _compact ? new Thickness(0, 0, 0, 1) : new Thickness(7, 0, 0, 0);
    }

    // ------------------------------------------------------------------ the door's states

    private void Attach()
    {
        Detach();
        _door = FindDoor(this);
        if (_door == null) return;
        _door.MouseEnter += OnDoorChanged;
        _door.MouseLeave += OnDoorChanged;
        PressedDesc.AddValueChanged(_door, OnDoorChanged);
        Refresh();
    }

    private void Detach()
    {
        if (_door == null) return;
        _door.MouseEnter -= OnDoorChanged;
        _door.MouseLeave -= OnDoorChanged;
        PressedDesc.RemoveValueChanged(_door, OnDoorChanged);
        _door = null;
    }

    private void OnDoorChanged(object? sender, EventArgs e) => Refresh();

    private static ButtonBase? FindDoor(DependencyObject from)
    {
        for (DependencyObject? p = from; p != null; p = VisualTreeHelper.GetParent(p) ?? LogicalTreeHelper.GetParent(p))
            if (p is ButtonBase b) return b;
        return null;
    }

    private void Refresh()
    {
        bool hot = _door?.IsMouseOver == true || IsMouseOver;
        var look = LookOf(hot, _door?.IsPressed == true, _pressedIn);
        if (look == _shown) return;
        Paint(look, animate: true);
    }

    /// <summary>Draws one look (also used by offscreen renders): the house gold on the word and rim, sheen at rest and
    /// on hover, the pressed shade and bevel in the well. The word's move eases in and back out; motion Off cuts it.</summary>
    internal void Paint(Look look, bool animate)
    {
        _shown = look;
        _word.SetResourceReference(TextBlock.ForegroundProperty, "Tier1GoldHoverBrush");
        _word.Opacity = look == Look.Pressed ? 0.88 : 1;
        SetResourceReference(BorderBrushProperty, look == Look.Pressed ? "DepthPressedBevel" : look == Look.Hot ? "Tier1GoldHoverBrush" : "Tier1GoldBorderBrush");
        SetResourceReference(BackgroundProperty, look == Look.Pressed ? "DepthPressedShade" : "DepthShadeBrush");
        _sheen.SetResourceReference(BackgroundProperty, look == Look.Pressed ? "DepthPressedShade" : look == Look.Hot ? "DepthPlankSheen" : "DepthRaisedSheen");
        bool gentle = false;
        try { gentle = Services.Safety.GentleVisuals.On; } catch { }
        double to = FaceDip(look, gentle);
        bool move = animate && SafeAllowTransitions();
        if (!move) { _face.BeginAnimation(TranslateTransform.YProperty, null); _face.Y = to; return; }
        var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(look == Look.Pressed ? 70 : 140)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        _face.BeginAnimation(TranslateTransform.YProperty, a);
    }

    private static bool SafeAllowTransitions()
    {
        try { return Services.MotionFx.AllowTransitions; } catch { return false; }
    }

    private static Binding Bound(string key) => new($"[{key}]") { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay };
}
