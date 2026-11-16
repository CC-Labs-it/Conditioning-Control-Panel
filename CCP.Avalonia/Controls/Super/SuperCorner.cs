using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Avalonia.Controls.Super;

/// <summary>The cut corner of a feature tile. The host routes its toggle gesture here.</summary>
public sealed class SuperCorner : Control, ICustomHitTest
{
    public const double Leg = 30, Inset = 5;
    private static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#FFCF6B"));
    private static readonly IBrush On = new SolidColorBrush(Color.Parse("#D0FFCF6B"));
    private static readonly IBrush OnHot = new SolidColorBrush(Color.Parse("#F2FFE09A"));
    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#8C120E22"));
    private static readonly IBrush OffHot = new SolidColorBrush(Color.Parse("#C83A3052"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#1A1030"));
    private static readonly IBrush Star = new SolidColorBrush(Color.Parse("#FFD985"));
    private static readonly Pen Edge = new(Star, 1.6);
    private static readonly Pen Glow = new(new SolidColorBrush(Color.Parse("#50FFCF6B")), 5);
    private readonly Geometry _shape;
    private bool _lit;

    public SuperEffect Effect { get; }
    public bool BottomRight { get; }
    public Func<bool> Inverted { get; set; } = () => false;
    public bool Lit => _lit;

    public SuperCorner(SuperEffect effect, bool bottomRight)
    {
        Effect = effect;
        BottomRight = bottomRight;
        Width = Height = Leg;
        HorizontalAlignment = bottomRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        VerticalAlignment = bottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Margin = bottomRight ? new Thickness(0, 0, Inset, Inset) : new Thickness(Inset, Inset, 0, 0);
        Cursor = new Cursor(StandardCursorType.Hand);
        var triangle = StreamGeometry.Parse(bottomRight ? "M30,30 L0,30 L30,0 Z" : "M0,0 L30,0 L0,30 Z");
        var rounded = new RectangleGeometry(bottomRight ? new Rect(-170, -170, 200, 200) : new Rect(0, 0, 200, 200)) { RadiusX = 6, RadiusY = 6 };
        _shape = new CombinedGeometry(GeometryCombineMode.Intersect, triangle, rounded);
        ToolTip.SetShowDelay(this, 350);
        UpdateTip();
        PointerEntered += (_, _) => { UpdateTip(); InvalidateVisual(); };
        PointerExited += (_, _) => InvalidateVisual();
        Loaded += (_, _) => { SuperAccess.HookTierEvents(); SuperAccess.Changed += OnChanged; Refresh(); };
        Unloaded += (_, _) => SuperAccess.Changed -= OnChanged;
        Refresh();
    }

    private void OnChanged(SuperEffect effect)
    {
        if (effect == Effect) Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        _lit = SuperAccess.IsUnlocked(Effect) && SuperAccess.IsSwitchedOn(Effect);
        InvalidateVisual();
    }

    public void Press() { SuperDoor.Press(Effect); Refresh(); }

    public bool HitTest(Point p) => p.X >= 0 && p.Y >= 0 && p.X <= Leg && p.Y <= Leg
        && (BottomRight ? p.X + p.Y >= Leg : p.X + p.Y <= Leg);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawGeometry(_lit ? (IsPointerOver ? OnHot : On) : (IsPointerOver ? OffHot : Off), null, _shape);
        var a = BottomRight ? new Point(0, Leg) : new Point(Leg, 0);
        var b = BottomRight ? new Point(Leg, 0) : new Point(0, Leg);
        using (context.PushGeometryClip(_shape)) context.DrawLine(Glow, a, b);
        context.DrawLine(Edge, a, b);
        var text = new FormattedText("★", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Symbol, Segoe UI"), 13, _lit ? Ink : Star);
        double center = BottomRight ? Leg * 0.64 : Leg * 0.36;
        context.DrawText(text, new Point(center - text.Width / 2, center - text.Height / 2));
    }

    private void UpdateTip() => ToolTip.SetTip(this, new ToolTip
    {
        Content = BuildTip(), Padding = new Thickness(10, 8),
        Background = new SolidColorBrush(Color.Parse("#F51A1A2E")), BorderBrush = Gold, BorderThickness = new Thickness(1)
    });

    internal Control BuildTip()
    {
        var panel = new StackPanel { MaxWidth = 250 };
        panel.Children.Add(new TextBlock { Text = Loc.GetF("super_corner_title", Loc.Get("super_name_" + Effect.ToString().ToLowerInvariant())), FontWeight = FontWeight.SemiBold, Foreground = Gold, FontSize = 12.5 });
        panel.Children.Add(new TextBlock { Text = SuperDoor.Twist(Effect), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Opacity = 0.85, FontSize = 11.5, Margin = new Thickness(0, 3, 0, 5) });
        panel.Children.Add(new TextBlock { Text = Loc.Get(Inverted() ? "avalonia_super_corner_tip_inverted" : "avalonia_super_corner_tip"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Opacity = 0.6, FontSize = 11 });
        if (!SuperAccess.IsUnlocked(Effect)) panel.Children.Add(new TextBlock { Text = Loc.Get("super_for_supporters"), Foreground = Gold, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        return panel;
    }
}
