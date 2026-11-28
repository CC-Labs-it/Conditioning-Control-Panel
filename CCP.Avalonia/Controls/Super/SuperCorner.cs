using System;
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

/// <summary>
/// WPF Controls/SuperCorner: the cut corner on a dashboard tile (owner, 2026-11-15), the switch for
/// the feature's V2. Gold glass when on, a dark dog-ear with a gold edge when off; see-through so
/// the art shows. The host routes the clicks with the tile's own rule: the on/off click calls
/// <see cref="Press"/> (a free account gets the tier refusal, never a flip), the other click opens
/// the feature like the rest of the tile. The star is drawn as a path (filled on, outline off,
/// ccp-bugs #1635), so no symbol font is needed on Linux. Shown for every account, locked included:
/// the tip says what the tier buys. An effect this head does not host draws no corner at all.
/// </summary>
public sealed class SuperCorner : Control, ICustomHitTest
{
    public const double Leg = 30;
    public const double Inset = 5;

    private static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B);
    private static readonly IBrush GoldBrush = new SolidColorBrush(Gold);
    private static readonly IBrush OnFill = new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xCF, 0x6B));
    private static readonly IBrush GoldHot = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xE0, 0x9A));
    private static readonly IBrush OffFill = new SolidColorBrush(Color.FromArgb(0x8C, 0x12, 0x0E, 0x22));
    private static readonly IBrush OffHot = new SolidColorBrush(Color.FromArgb(0xC8, 0x3A, 0x30, 0x52));
    private static readonly IPen EdgeGlow = new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xCF, 0x6B)), 5);
    private static readonly IBrush Ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x10, 0x30));
    private static readonly IBrush DimStar = new SolidColorBrush(Color.FromRgb(0xFF, 0xD9, 0x85));
    private static readonly IPen Edge = new Pen(DimStar, 1.6);
    private static readonly IPen StarLine = new Pen(DimStar, 1.1, lineJoin: PenLineJoin.Round);

    public SuperEffect Kind { get; }
    public bool BottomRight { get; }
    /// <summary>Does the host's on/off click sit on the LEFT button right now (clicks inverted).</summary>
    public Func<bool> Inverted { get; set; } = () => false;

    private bool _hot;

    public SuperCorner(SuperEffect effect, bool bottomRight)
    {
        Kind = effect;
        BottomRight = bottomRight;
        Width = Height = Leg;
        HorizontalAlignment = bottomRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        VerticalAlignment = bottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Margin = bottomRight ? new Thickness(0, 0, Inset, Inset) : new Thickness(Inset, Inset, 0, 0);
        Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetShowDelay(this, 350);
        ToolTip.SetTip(this, "");
        ToolTip.AddToolTipOpeningHandler(this, (_, _) => ToolTip.SetTip(this, BuildTip()));
        PointerEntered += (_, _) => { _hot = true; InvalidateVisual(); };
        PointerExited += (_, _) => { _hot = false; InvalidateVisual(); };
        global::Avalonia.Automation.AutomationProperties.SetName(this, Loc.GetF("super_corner_title", Name()));
    }

    private string Name() => Loc.Get("super_name_" + Kind.ToString().ToLowerInvariant());

    /// <summary>Is the V2 drawn as on: unlocked and switched on.</summary>
    public bool Lit => SuperAccess.IsUnlocked(Kind) && SuperAccess.IsSwitchedOn(Kind);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SuperAccess.HookTierEvents();
        SuperAccess.Changed += OnChanged;
        SuperPreview.StateChanged += InvalidateVisual;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SuperAccess.Changed -= OnChanged;
        SuperPreview.StateChanged -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnChanged(SuperEffect e)
    {
        if (e != Kind) return;
        if (Dispatcher.UIThread.CheckAccess()) InvalidateVisual(); else Dispatcher.UIThread.Post(InvalidateVisual);
    }

    /// <summary>The on/off click landed on the corner (WPF SuperCorner.Press through SuperDoor).</summary>
    public void Press()
    {
        SuperDoor.Press(Kind);
        InvalidateVisual();
    }

    public bool HitTest(Point p) =>
        p.X >= 0 && p.Y >= 0 && p.X <= Leg && p.Y <= Leg && (BottomRight ? p.X + p.Y >= Leg : p.X + p.Y <= Leg);

    private Geometry Shape()
    {
        var tri = new StreamGeometry();
        using (var c = tri.Open())
        {
            if (BottomRight) { c.BeginFigure(new Point(Leg, Leg), true); c.LineTo(new Point(0, Leg)); c.LineTo(new Point(Leg, 0)); }
            else { c.BeginFigure(new Point(0, 0), true); c.LineTo(new Point(Leg, 0)); c.LineTo(new Point(0, Leg)); }
            c.EndFigure(true);
        }
        // The outer tip rounds like the inside of the tile's ring, so the corner tucks into its arc.
        var card = BottomRight ? new Rect(Leg - 200, Leg - 200, 200, 200) : new Rect(0, 0, 200, 200);
        return new CombinedGeometry(GeometryCombineMode.Intersect, tri, new RectangleGeometry(card, 6, 6));
    }

    /// <summary>A five-point star centred on <paramref name="c"/>, outer radius <paramref name="r"/>.</summary>
    internal static Geometry Star(Point c, double r)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int i = 0; i < 10; i++)
            {
                double rad = i % 2 == 0 ? r : r * 0.45, a = -Math.PI / 2 + i * Math.PI / 5;
                var p = new Point(c.X + rad * Math.Cos(a), c.Y + rad * Math.Sin(a));
                if (i == 0) ctx.BeginFigure(p, true); else ctx.LineTo(p);
            }
            ctx.EndFigure(true);
        }
        return g;
    }

    public override void Render(DrawingContext dc)
    {
        bool lit = Lit;
        var shape = Shape();
        dc.DrawGeometry(lit ? (_hot ? GoldHot : OnFill) : (_hot ? OffHot : OffFill), null, shape);
        var a = BottomRight ? new Point(0, Leg) : new Point(Leg, 0);
        var b = BottomRight ? new Point(Leg, 0) : new Point(0, Leg);
        using (dc.PushGeometryClip(shape)) dc.DrawLine(EdgeGlow, a, b);
        dc.DrawLine(Edge, a, b);
        double k = BottomRight ? Leg * 0.64 : Leg * 0.36;
        var star = Star(new Point(k, k), 5.6);
        if (lit) dc.DrawGeometry(Ink, null, star);
        else dc.DrawGeometry(null, StarLine, star);
    }

    private Control BuildTip()
    {
        var panel = new StackPanel { MaxWidth = 250 };
        panel.Children.Add(new TextBlock { Text = Loc.GetF("super_corner_title", Name()), FontWeight = FontWeight.SemiBold, Foreground = GoldBrush, FontSize = 12.5 });
        panel.Children.Add(new TextBlock
        {
            Text = SuperDoor.Twist(Kind), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Opacity = 0.85,
            FontSize = 11.5, Margin = new Thickness(0, 3, 0, 5),
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.Get(Inverted() ? "super_corner_tip_inverted" : "super_corner_tip"), TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White, Opacity = 0.6, FontSize = 11,
        });
        if (!SuperAccess.IsUnlocked(Kind))
            panel.Children.Add(new TextBlock { Text = Loc.Get("super_for_supporters"), Foreground = GoldBrush, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
        return panel;
    }

    /// <summary>The corner for a tile's effect name, or null for none or an effect this head does not host.</summary>
    public static SuperCorner? For(string? name, bool bottomRight, Func<bool> inverted)
        => Enum.TryParse<SuperEffect>(name, out var effect) && SuperBox.HostedOnThisHead.Contains(effect)
            ? new SuperCorner(effect, bottomRight) { Inverted = inverted }
            : null;
}
