using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Announcements;

namespace ConditioningControlPanel;

/// <summary>The pieces of the promo card, built from the promo: the menu pills and the countdown
/// cells. Numbers are the concept's (bf50-2026 index.html, the .nb rules).</summary>
public partial class PromoCardWindow
{
    private static readonly Color Ink = Rgb(0x1B, 0x1A, 0x17), Pink = Rgb(0xFF, 0x4F, 0xA8), Violet = Rgb(0x7B, 0x5C, 0xFF),
        Gold = Rgb(0xFF, 0xD2, 0x3F), Paper = Rgb(0xF7, 0xF5, 0xEF), Plum = Rgb(0x2A, 0x0A, 0x1C), Stamp = Rgb(0xA3, 0x23, 0x2B),
        Slate = Rgb(0x5C, 0x5A, 0x52);
    private static readonly Brush HardShadow = Frozen(new SolidColorBrush(Color.FromArgb(0x38, 0, 0, 0)));

    private FontFamily Round => (FontFamily)FindResource("PromoRound");
    private FontFamily Mono => (FontFamily)FindResource("PromoMono");
    private TextBlock? _copyMeta;

    private void Build(string title, string message)
    {
        var hook = _promo.Hook ?? "";
        HookText.Text = hook;
        HookText.Visibility = hook.Length > 0 ? Visibility.Visible : Visibility.Collapsed; // no hook: the close coin gets the top row to itself
        if (hook.Length == 0) Body.Margin = new Thickness(22, 54, 22, 22);

        TitleText.Text = title;
        MessageText.Text = message;
        MessageScroll.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        HeroBox.Visibility = Visibility.Collapsed;
        CountdownPanel.Visibility = _promo.EndsAt != null ? Visibility.Visible : Visibility.Collapsed;
        HeroLayout();

        if (!string.IsNullOrWhiteSpace(_promo.Tag))
        {
            TagText.Text = _promo.Tag;
            TagStamp.Visibility = Visibility.Visible;
        }

        // The menu, the title screen's own: black Continue row = the code, then the offers, the
        // pink button, the dashed "?" row = Not now.
        if (_promo.Code != null)
        {
            var code = Pill(_promo.Code, Loc.Get("promo_copy_code"), Ink, Colors.White, 48, 33, 12.5, new Thickness(20, 0, 13, 0), peel: true);
            _copyMeta = code.Meta;
            MenuStack.Children.Add(Clickable(code.Visual, -1.6, -18, OnCopy, $"{_promo.Code}, {Loc.Get("promo_copy_code")}", 0));
        }
        var fills = _promo.Rows.Count == 1 ? new[] { Violet } : new[] { Pink, Violet, Paper };
        double[] tilts = { 1.4, -1.2, 1.0 }, shifts = { 12, -8, 6 };
        for (var i = 0; i < _promo.Rows.Count; i++)
        {
            var row = _promo.Rows[i];
            var on = fills[i] == Pink ? Plum : fills[i] == Paper ? Ink : Colors.White;
            var pill = Pill(row.Label, row.Sub, fills[i], on, 32, 18.5, 11.5, new Thickness(15, 0, 9, 0));
            MenuStack.Children.Add(Tilted(pill.Visual, tilts[i], shifts[i], MenuStack.Children.Count == 0 ? 0 : 12));
        }
        if (_promo.Cta != null)
        {
            var cta = Pill(_promo.Cta.Label, Loc.Get("promo_opens_browser"), Pink, Plum, 50, 23, 11.5, new Thickness(24, 0, 14, 0));
            MenuStack.Children.Add(Clickable(cta.Visual, 1.3, 16, OnCta, _promo.Cta.Label, 16));
        }
        MenuStack.Children.Add(Clickable(NotNowPill(), -0.6, 0, OnNotNow, Loc.Get("promo_not_now"), 16));
        AutomationProperties.SetName(BtnClose, Loc.Get("btn_close"));
    }

    /// <summary>Stickers and countdown share a row; either can be absent, and the row goes when both are.</summary>
    private void HeroLayout()
    {
        var stickers = HeroBox.Visibility == Visibility.Visible;
        var clock = CountdownPanel.Visibility == Visibility.Visible;
        CountdownPanel.Margin = new Thickness(stickers ? 22 : 0, 0, 0, 0);
        HeroRow.Visibility = stickers || clock ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- pills ----

    /// <summary>A title-screen row: a pill of <paramref name="fill"/> in a 3 px white ring with a
    /// hard 2, 4 shadow, the label and a small tag pill beside it.</summary>
    private (FrameworkElement Visual, TextBlock? Meta) Pill(string label, string? meta, Color fill, Color on,
        double height, double size, double metaSize, Thickness pad, bool peel = false)
    {
        var r = (height + 6) / 2;
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(Text(label, Round, size, FontWeights.SemiBold, on, new Thickness(0, 0, 0, size * 0.06)));
        TextBlock? metaText = null;
        if (!string.IsNullOrWhiteSpace(meta))
        {
            metaText = Text(meta!, Round, metaSize, FontWeights.SemiBold, on, new Thickness(0));
            line.Children.Add(new Border
            {
                Margin = new Thickness(9, 0, 0, 0), Padding = new Thickness(metaSize * 0.75, 2, metaSize * 0.75, 3),
                CornerRadius = new CornerRadius(99), VerticalAlignment = VerticalAlignment.Center,
                Background = Frozen(new SolidColorBrush(Color.FromArgb(0x2E, on.R, on.G, on.B))),
                Child = metaText, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(),
            });
        }
        var g = new Grid { Height = height + 6 };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(r), Background = HardShadow, RenderTransform = new TranslateTransform(2, 4) });
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(r), Background = Frozen(new SolidColorBrush(fill)),
            BorderBrush = Brushes.White, BorderThickness = new Thickness(3), Padding = pad, Child = line,
        });
        if (peel)
        {
            // The peeled sticker corner of the code row.
            const string corner = "M 0,0 L 0,13 Q 0,19 6,19 L 19,19 Z";
            g.Children.Add(new Path { Data = Geometry.Parse(corner), Fill = HardShadow, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -1, 4, 0) });
            g.Children.Add(new Path { Data = Geometry.Parse(corner), Fill = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 2, 0) });
        }
        return (g, metaText);
    }

    /// <summary>The title screen's dashed "?" row: paper, a dashed slate outline, no shadow.</summary>
    private FrameworkElement NotNowPill()
    {
        var g = new Grid { Height = 38 };
        g.Children.Add(new Rectangle
        {
            RadiusX = 19, RadiusY = 19, Fill = Frozen(new SolidColorBrush(Color.FromArgb(0xD1, Paper.R, Paper.G, Paper.B))),
            Stroke = Frozen(new SolidColorBrush(Slate)), StrokeThickness = 2.5, StrokeDashArray = new DoubleCollection { 2.4, 1.6 },
        });
        g.Children.Add(Text(Loc.Get("promo_not_now"), Round, 16, FontWeights.SemiBold, Slate, new Thickness(24, 0, 24, 1)));
        return g;
    }

    /// <summary>Tilts a row and shifts it sideways, the menu's hand-placed look.</summary>
    private static FrameworkElement Tilted(FrameworkElement el, double tilt, double dx, double gapAbove)
    {
        el.Margin = new Thickness(0, gapAbove, 0, 0);
        el.HorizontalAlignment = HorizontalAlignment.Center;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var pop = new ScaleTransform();
        var turn = new RotateTransform(tilt);
        el.RenderTransform = new TransformGroup { Children = { pop, turn, new TranslateTransform(dx, 0) } };
        el.Tag = (pop, turn, tilt);
        return el;
    }

    /// <summary>A tilted row that is a button: hover or keyboard focus lifts it straight and a
    /// little bigger, a press squashes it.</summary>
    private Button Clickable(FrameworkElement visual, double tilt, double dx, RoutedEventHandler click, string name, double gapAbove)
    {
        var b = new Button { Style = (Style)FindResource("PromoBareButton"), Content = visual };
        Tilted(b, tilt, dx, gapAbove);
        AutomationProperties.SetName(b, name);
        b.Click += click;
        void Lift() => Shape(b, b.IsPressed ? 0.96 : b.IsMouseOver || b.IsKeyboardFocused ? 1.07 : 1, b.IsMouseOver || b.IsKeyboardFocused);
        b.MouseEnter += (_, _) => Lift();
        b.MouseLeave += (_, _) => Lift();
        b.IsKeyboardFocusedChanged += (_, _) => Lift();
        b.PreviewMouseLeftButtonDown += (_, _) => Shape(b, 0.96, true);
        b.PreviewMouseLeftButtonUp += (_, _) => Lift();
        return b;
    }

    private static void Shape(FrameworkElement el, double scale, bool straight)
    {
        if (el.Tag is not (ScaleTransform pop, RotateTransform turn, double tilt)) return;
        var angle = straight ? 0 : tilt;
        if (!MotionFx.AllowTransitions)
        {
            pop.ScaleX = pop.ScaleY = scale; turn.Angle = angle;
            return;
        }
        var d = TimeSpan.FromMilliseconds(200);
        var ease = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut };
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, d) { EasingFunction = ease });
        turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, d) { EasingFunction = ease });
    }

    /// <summary>Pops an element in from <paramref name="from"/> scale (and from clear, for an entrance).</summary>
    private static void PopIn(FrameworkElement el, int delayMs, double from = 0.6)
    {
        var pop = el.Tag is (ScaleTransform p, RotateTransform, double) ? p : el.RenderTransform as ScaleTransform;
        if (pop == null) return;
        var d = TimeSpan.FromMilliseconds(360);
        var grow = new DoubleAnimation(from, 1, d)
        {
            BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        pop.ScaleX = pop.ScaleY = from;
        grow.Completed += (_, _) => { pop.ScaleX = pop.ScaleY = 1; };
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        if (from >= 1) return;
        el.Opacity = 0;
        var show = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = grow.BeginTime, FillBehavior = FillBehavior.Stop };
        show.Completed += (_, _) => el.Opacity = 1;
        el.BeginAnimation(OpacityProperty, show);
    }

    // ---- countdown ----

    private void DrawCountdown(TimeSpan left)
    {
        CountdownPanel.Children.Clear();
        var tag = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 3, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransform = new RotateTransform(-2), Child = Text(Loc.Get("promo_ends_in").ToUpperInvariant(), Mono, 12, FontWeights.Bold, Stamp, new Thickness(0)),
        };
        var stamp = new Grid { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
        stamp.Children.Add(new Border { Background = Frozen(new SolidColorBrush(Color.FromArgb(0x33, 0, 0, 0))), CornerRadius = new CornerRadius(4),
            RenderTransform = new TransformGroup { Children = { new RotateTransform(-2), new TranslateTransform(1, 2) } } });
        stamp.Children.Add(tag);
        CountdownPanel.Children.Add(stamp);

        var cells = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var i = 0;
        foreach (var (value, unit) in PromoCountdown.Parts(left))
        {
            var word = Loc.Get(unit switch { PromoCountdown.Unit.Days => "promo_days", PromoCountdown.Unit.Hours => "promo_hours", _ => "promo_minutes" });
            var face = new StackPanel();
            face.Children.Add(Text(value.ToString("00"), Round, 30, FontWeights.SemiBold, Colors.White, new Thickness(0, -2, 0, 0)));
            face.Children.Add(Text(word.ToUpperInvariant(), Mono, 10.5, FontWeights.Bold, Gold, new Thickness(0, 2, 0, 0)));
            var cell = new Grid { Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0), MinWidth = 74, RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(i % 2 == 0 ? -1.4 : 1.2) };
            cell.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = HardShadow, RenderTransform = new TranslateTransform(2, 4) });
            cell.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = Frozen(new SolidColorBrush(Ink)), BorderBrush = Brushes.White,
                BorderThickness = new Thickness(3), Padding = new Thickness(8, 8, 8, 8), Child = face });
            cells.Children.Add(cell);
            i++;
        }
        CountdownPanel.Children.Add(cells);
        AutomationProperties.SetName(CountdownPanel, $"{Loc.Get("promo_ends_in")} {PromoCountdown.Format(left)}");
    }

    private static TextBlock Text(string text, FontFamily family, double size, FontWeight weight, Color color, Thickness margin) => new()
    {
        Text = text, FontFamily = family, FontSize = size, FontWeight = weight, Margin = margin,
        Foreground = Frozen(new SolidColorBrush(color)), VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
