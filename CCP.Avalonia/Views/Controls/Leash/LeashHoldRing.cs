// PORTED from WPF ConditioningControlPanel/Controls/Leash/LeashHoldRing.cs (+ LeashLook.Ring :274).
// The hold-to-cut countdown: while the panic key is held on a leashed account, a small topmost pill
// near the bottom of the primary screen counts 5..1 round a ring. It never takes focus or clicks
// (X11Overlay.SetClickThrough = WS_EX_TRANSPARENT; ShowActivated off = WS_EX_NOACTIVATE), and it has no
// animation of its own: the number and the arc move once a second (Core LeashHoldFlow decides when).
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashHoldRing : Window
{
    private static LeashHoldRing? _open;
    private readonly Border _ringHost = new() { Width = 46, Height = 46 };
    private int _shown = -1;

    /// <summary>WPF FriendsLook.GlassBrush: #241742 down to the panel colour.</summary>
    internal static IBrush Glass() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(0x24, 0x17, 0x42), 0), new GradientStop(Color.FromRgb(0x1C, 0x12, 0x33), 1) },
    };

    /// <summary>Show (or move) the ring for a hold that has run <paramref name="held"/>.</summary>
    public static void ShowHeld(TimeSpan held)
    {
        try
        {
            if (_open == null)
            {
                _open = new LeashHoldRing();
                _open.Show();
            }
            _open.Paint(held);
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash hold ring failed: {E}", ex.Message); }
    }

    public static void Dismiss()
    {
        var w = _open;
        _open = null;
        try { w?.Close(); } catch { }
    }

    internal static bool IsShown => _open != null;
    internal static int Number => _open?._shown ?? -1;
    internal static LeashHoldRing? Current => _open;

    /// <summary>Also the render-all sample (two seconds into a hold: 3).</summary>
    private LeashHoldRing()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = Loc.Get("leash_title");

        var words = F.Label(Loc.Get("leash_hold_ring"), 14, F.Text, F.Display, FontWeight.SemiBold);
        words.Margin = new Thickness(10, 0, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 16, 8), Children = { _ringHost, words } };
        Content = new Border
        {
            Background = Glass(), BorderBrush = F.Mint, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(999), Child = row, Tag = "leash-hold-ring",
        };
        Paint(TimeSpan.FromSeconds(2));

        Opened += (_, _) =>
        {
            X11Overlay.SetClickThrough(this, true);
            if (Screens.Primary is not { } screen) return;
            var wa = screen.WorkingArea;
            var s = screen.Scaling;
            var w = (int)(Bounds.Width * s);
            var h = (int)(Bounds.Height * s);
            Position = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Bottom - h - (int)(48 * s));
        };
    }

    private void Paint(TimeSpan held)
    {
        var n = LeashHoldTick.SecondsLeft(held);
        if (n == _shown) return;
        _shown = n;
        _ringHost.Child = Ring(LeashHoldTick.Fraction(held), n.ToString(CultureInfo.InvariantCulture), Color.FromRgb(0x5F, 0xFF, 0xD0), 46);
    }

    /// <summary>WPF LeashLook.Ring: a faint track, a round-capped arc for <paramref name="fraction"/>
    /// clockwise from twelve with a soft glow, the figure inside in mono bold.</summary>
    internal static Control Ring(double fraction, string figure, Color color, double size = 50)
    {
        var g = new Grid { Width = size, Height = size, Tag = "leash-ring" };
        double stroke = size * 0.11;
        g.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)), StrokeThickness = stroke });
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction > 0.001)
        {
            g.Children.Add(new Arc
            {
                StartAngle = -90, SweepAngle = fraction * 360,
                Stroke = new SolidColorBrush(color), StrokeThickness = stroke, StrokeLineCap = PenLineCap.Round,
                Effect = new DropShadowEffect { BlurRadius = 8, OffsetX = 0, OffsetY = 0, Opacity = 0.5, Color = color },
                Tag = "leash-ring-arc",
            });
        }
        g.Children.Add(new TextBlock
        {
            Text = figure, FontFamily = F.Mono, FontWeight = FontWeight.Bold, FontSize = size * 0.28, Foreground = F.Text,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-ring-figure",
        });
        return g;
    }
}
