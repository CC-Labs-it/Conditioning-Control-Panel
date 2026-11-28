using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls.Documents;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Controls.Dares;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Dares;

// WPF c2cf10de4d Controls/Dares/DareLook.cs:31-73, 307-318.
internal static class DareLook
{
    // WPF 6247691138 Controls/Dares/DareLook.cs:165-201.
    public static Color ToneColor(DareTone tone) => tone switch
    {
        DareTone.Cant or DareTone.Dare => Color.FromRgb(0xC2, 0x2B, 0x2B),
        DareTone.Duel or DareTone.CameraOn => Color.FromRgb(0x2C, 0x8A, 0x57),
        DareTone.Pics => Color.FromRgb(0xE0, 0x66, 0x3A),
        DareTone.Minutes => Color.FromRgb(0x1F, 0x8A, 0x8A),
        DareTone.Video => Color.FromRgb(0xCC, 0x3D, 0x7A),
        DareTone.Session => Color.FromRgb(0x8A, 0x3F, 0x8F),
        DareTone.Bubbles => Color.FromRgb(0x2F, 0x86, 0xC9),
        DareTone.Sp => Color.FromRgb(0xB0, 0x7A, 0x00),
        DareTone.Lock => Color.FromRgb(0x33, 0x61, 0x8F),
        DareTone.CameraOff => Color.FromRgb(0x7A, 0x6A, 0x8F),
        _ => Color.FromRgb(0x3A, 0x1D, 0x78),
    };

    public static string ToneIcon(DareTone tone) => tone switch
    {
        DareTone.Pics => "picture",
        DareTone.Minutes or DareTone.When => "clock",
        DareTone.Video => "video",
        DareTone.Session => "play",
        DareTone.Bubbles => "bubble",
        DareTone.Sp => "star",
        DareTone.Lock => "lock",
        DareTone.Lockdown => "ldn",
        DareTone.CameraOn => "eye",
        DareTone.CameraOff => "eyeoff",
        DareTone.Cant => "cross",
        DareTone.Duel => "versus",
        _ => "envelope",
    };

    public static string ToneGlyph(DareTone tone) => tone switch
    {
        DareTone.Pics => "\uE8B9", DareTone.Minutes => "\uE823", DareTone.Video => "\uE714",
        DareTone.Session => "\uE768", DareTone.Bubbles => "\uEA3A", DareTone.Dare => "\uE715",
        DareTone.Sp => "\uE734", DareTone.Lock => "\uE72E", DareTone.Lockdown => "\uEA18",
        DareTone.When => "\uE787", DareTone.CameraOn => "\uE7B3", DareTone.CameraOff => "\uED1A",
        DareTone.Cant => "\uE711", DareTone.Duel => "\uE8AB", _ => "\uE77B",
    };

    // WPF 8d480c7d3f Controls/Dares/DareLook.cs:774-790: choices have a flat base edge.
    public static Button Choice(string label, Color color, string? tip, Action onClick, string tag)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xEA, 0xFF)) },
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x12, 0x33)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xA0, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1, 1, 1, 4),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 6, 14, 5),
            Margin = new Thickness(0, 0, 8, 8),
            Tag = tag,
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => onClick();
        return button;
    }

    // ---- the cards (WPF Controls/Dares/DareLook.cs:40-89, 504-631, 919-1229) ----

    public static readonly Color SlipPaper = Color.FromRgb(0xFF, 0xD3, 0xE6), SlipLine = Color.FromRgb(0xF3, 0xA9, 0xC9),
        Print = Color.FromRgb(0x4B, 0x1F, 0x45), Ink = Color.FromRgb(0x3A, 0x1D, 0x78), StampRed = Color.FromRgb(0xD2, 0x26, 0x5F),
        TileFace = Color.FromRgb(0xF7, 0xEE, 0xF8), CardInk = Color.FromRgb(0x2E, 0x18, 0x3A);
    public static readonly IBrush ChalkBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0xE6, 0xFF));
    public static readonly IBrush MutedBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0x93, 0xCF));
    public static readonly IBrush WarnBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xCF, 0x6B));
    public static readonly IBrush PrintBrush = new SolidColorBrush(Print), InkBrush = new SolidColorBrush(Ink),
        CardInkBrush = new SolidColorBrush(CardInk), GoodOnCardBrush = new SolidColorBrush(Color.FromRgb(0x17, 0x80, 0x5A)),
        WarnOnCardBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0x6A, 0x00));

    // WPF :86-89, the same OFL files (ConditioningControlPanel/Fonts/Dares).
    public static readonly FontFamily PrintFont = new("avares://CCP.Avalonia/Resources/fonts/dares#Barlow Condensed");
    public static readonly FontFamily HandFont = new("avares://CCP.Avalonia/Resources/fonts/dares#Caveat");

    private static readonly System.Collections.Generic.Dictionary<string, string> Paths = new()
    {
        ["envelope"] = "M3 6 H21 V19 H3 Z M3.5 7.5 L12 13.5 L20.5 7.5",
        ["versus"] = "M4 8 H17 L14 5 M20 16 H7 L10 19",
        ["clock"] = "M20.5 12 A8.5 8.5 0 1 1 3.5 12 A8.5 8.5 0 1 1 20.5 12 Z M12 7.5 V12 L15 14",
        ["star"] = "M12 3.5 L14.6 8.9 L20.5 9.7 L16.2 13.8 L17.2 19.6 L12 16.9 L6.8 19.6 L7.8 13.8 L3.5 9.7 L9.4 8.9 Z",
        ["ldn"] = "M3 4 H21 V17 H3 Z M9 21 H15 M12 17 V21 M9.5 10 H14.5 V14 H9.5 Z M10.5 10 V9 A1.5 1.5 0 0 1 13.5 9 V10",
        ["eye"] = "M2.5 12 C2.5 12 6 5.5 12 5.5 C18 5.5 21.5 12 21.5 12 C21.5 12 18 18.5 12 18.5 C6 18.5 2.5 12 2.5 12 Z M14.8 12 A2.8 2.8 0 1 1 9.2 12 A2.8 2.8 0 1 1 14.8 12 Z",
        ["cross"] = "M6 6 L18 18 M18 6 L6 18",
        ["arrow"] = "M5 12 H18 M13 7 L18 12 L13 17",
    };

    /// <summary>WPF :139: a line icon on a 24 px grid.</summary>
    public static Control Icon(string name, double size, IBrush stroke, double thickness = 1.7) => new Viewbox
    {
        Width = size, Height = size,
        Child = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(Paths.TryGetValue(name, out var d) ? d : Paths["envelope"]), Width = 24, Height = 24,
            Stroke = stroke, StrokeThickness = thickness, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        },
    };

    /// <summary>WPF :690: a kind's 46 px disc with its white icon.</summary>
    public static Control Disc(DareTone tone)
    {
        var g = new Grid { Width = 46, Height = 46 };
        g.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Fill = new SolidColorBrush(ToneColor(tone)) });
        g.Children.Add(Icon(ToneIcon(tone), 28, Brushes.White));
        return g;
    }

    public static TextBlock Words(string text, double size, IBrush? fg = null, string? tag = null) => new()
    {
        Text = text, FontFamily = PrintFont, FontWeight = FontWeight.Medium, FontSize = size, Foreground = fg ?? ChalkBrush,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Tag = tag,
    };

    /// <summary>WPF :248: handwriting (Caveat Bold).</summary>
    public static TextBlock Hand(string text, double size, IBrush fg) => new()
    {
        Text = text, FontFamily = HandFont, FontWeight = FontWeight.Bold, FontSize = size, Foreground = fg,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>WPF :267: spaced capitals.</summary>
    public static TextBlock Caps(string text, double size, IBrush fg, FontWeight weight, double trackingEm) => new()
    {
        Text = text.ToUpper(System.Globalization.CultureInfo.CurrentCulture), FontFamily = PrintFont, FontSize = size,
        FontWeight = weight, Foreground = fg, LetterSpacing = size * trackingEm, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>WPF :985: the small round (?), its rule in a tooltip, pinned open on click.</summary>
    public static Button Help(string key, IBrush? fg = null)
    {
        var text = Loc.Get(key);
        var brush = fg ?? MutedBrush;
        var g = new Grid { Width = 22, Height = 22 };
        g.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Stroke = brush, StrokeThickness = 1.5 });
        g.Children.Add(new TextBlock { Text = "?", FontFamily = PrintFont, FontSize = 15, FontWeight = FontWeight.Bold, Foreground = brush,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        var b = Bare(g, "dares-help:" + key);
        b.Margin = new Thickness(8, 0, 0, 0);
        AutomationProperties.SetName(b, text);
        ToolTip.SetTip(b, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 });
        ToolTip.SetPlacement(b, PlacementMode.Bottom);
        b.Click += (_, _) => ToolTip.SetIsOpen(b, !ToolTip.GetIsOpen(b));
        b.PointerExited += (_, _) => ToolTip.SetIsOpen(b, false);
        return b;
    }

    private static Button Bare(Control content, string tag) => new()
    {
        Content = content, Tag = tag, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
        Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>WPF :577: the slip's head, the title in spaced capitals over a rule, its (?) and close cross.</summary>
    public static Control SlipHead(string title, string? helpKey, Action? onClose)
    {
        var dock = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        if (helpKey != null) right.Children.Add(Help(helpKey, PrintBrush));
        if (onClose != null)
        {
            var x = Bare(Icon("cross", 18, PrintBrush, 2), "dares-close");
            x.Margin = new Thickness(6, 0, -6, 0);
            AutomationProperties.SetName(x, Loc.Get("dares_close"));
            x.Click += (_, _) => onClose();
            right.Children.Add(x);
        }
        DockPanel.SetDock(right, Dock.Right);
        dock.Children.Add(right);
        dock.Children.Add(Caps(title, 19, PrintBrush, FontWeight.Bold, 0.16));
        return new Border { Child = dock, MinHeight = 32, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(0, 0, 0, 5),
            BorderBrush = PrintBrush, BorderThickness = new Thickness(0, 0, 0, 2), Tag = "dares-head" };
    }

    /// <summary>WPF :604: a heading on the desk (the debt card), chalk spaced capitals and an optional (?).</summary>
    public static Control DeskHead(string title, string? helpKey)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14), Tag = "dares-head" };
        sp.Children.Add(Caps(title, 19, ChalkBrush, FontWeight.Bold, 0.16));
        if (helpKey != null) sp.Children.Add(Help(helpKey, ChalkBrush));
        return sp;
    }

    /// <summary>WPF :551: the pink betting slip, perforated top, ruled lines, a -0.6 deg tilt and a shadow.
    /// <paramref name="paper"/> is where a stamp lands.</summary>
    public static Control Slip(Control head, Control body, out Panel paper)
    {
        var content = new StackPanel { Margin = new Thickness(22, 26, 22, 22) };
        content.Children.Add(head);
        content.Children.Add(body);
        var p = new Grid { Tag = "dares-slip-paper" };
        p.Children.Add(new SlipPaperFace());
        p.Children.Add(content);
        paper = p;
        return new Border
        {
            Child = p, Margin = new Thickness(2, 4, 2, 8), RenderTransform = new RotateTransform(-0.6), Tag = "dares-slip",
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 26, Color = Color.FromArgb(0x99, 0, 0, 0) }),
        };
    }

    /// <summary>WPF :516-538: the paper, its rules every 44 px from 70 px down, the scalloped top.</summary>
    private sealed class SlipPaperFace : Control
    {
        private static readonly IPen Rule = new Pen(new SolidColorBrush(SlipLine), 1);
        public override void Render(DrawingContext ctx)
        {
            var w = Bounds.Width;
            ctx.FillRectangle(new SolidColorBrush(SlipPaper), new Rect(Bounds.Size));
            for (var y = 69.5; y < Bounds.Height; y += 44) ctx.DrawLine(Rule, new Point(0, y), new Point(w, y));
            for (var x = 9.0; x < w + 9; x += 18) ctx.DrawEllipse(new SolidColorBrush(Color.FromRgb(0x1A, 0x10, 0x30)), null, new Point(x, -1), 5.3, 5.3);
        }
    }

    /// <summary>WPF :623: the light card the debt card and its consent sit on; the stamp lands on it.</summary>
    public static Panel LockCard(out StackPanel inner)
    {
        inner = new StackPanel { Margin = new Thickness(18) };
        var g = new Grid { Tag = "dares-lockcard" };
        g.Children.Add(new Border { Background = new SolidColorBrush(TileFace), CornerRadius = new CornerRadius(10), Child = inner,
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 26, Color = Color.FromArgb(0x99, 0, 0, 0) }) });
        TextElement.SetForeground(g, CardInkBrush);
        return g;
    }

    /// <summary>WPF DareLook.Big (go, a face on its ledge) and Quiet (an outline, a small line under it).</summary>
    public static Button Act(string label, string? sub, bool big, Action onClick, string tag, bool onCard = false)
    {
        var fg = big ? new SolidColorBrush(Color.FromRgb(0x05, 0x33, 0x24)) : onCard ? CardInkBrush : ChalkBrush;
        var words = new StackPanel();
        words.Children.Add(Caps(label, big ? 24 : 19, fg, big ? FontWeight.Bold : FontWeight.Medium, big ? 0.14 : 0.1));
        if (sub != null) words.Children.Add(new TextBlock { Text = sub, FontFamily = PrintFont, FontSize = 13, Foreground = fg, Opacity = 0.75 });
        var face = big
            ? new Border { Child = words, Background = new SolidColorBrush(Color.FromRgb(0x2F, 0xD3, 0x9A)), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(30, 9, 30, 10), BorderBrush = new SolidColorBrush(Color.FromRgb(0x17, 0x80, 0x5A)), BorderThickness = new Thickness(0, 0, 0, 6) }
            : new Border { Child = words, BorderBrush = onCard ? new SolidColorBrush(Color.FromRgb(0xC9, 0xAF, 0xCC)) : MutedBrush,
                BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 8) };
        var button = Bare(face, tag);
        button.VerticalAlignment = VerticalAlignment.Top;
        button.Margin = new Thickness(0, 0, 12, 10);
        AutomationProperties.SetName(button, sub == null ? label : label + ", " + sub);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>WPF :1042: spaced capitals in a double frame, tilted -11 deg, thudding down from big.</summary>
    public static Border Stamp(string word, bool grey)
    {
        var c = new SolidColorBrush(grey ? Print : StampRed);
        var st = new Border
        {
            BorderBrush = c, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(6), Padding = new Thickness(1.5),
            Child = new Border { BorderBrush = c, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4),
                Padding = new Thickness(14, 0, 14, 1), Child = Caps(word, 34, c, FontWeight.Bold, 0.12) },
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 14), IsHitTestVisible = false, Opacity = grey ? 0.6 : 0.86, Tag = "dares-stamp",
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(-11) } },
        };
        AutomationProperties.SetName(st, word);
        var k = Amount;
        if (k > 0)
        {
            var from = 1 + 1.2 * k;
            Run(st, StampLandMs, new BackEaseOut(), (ScaleTransform.ScaleXProperty, from, 1.0), (ScaleTransform.ScaleYProperty, from, 1.0),
                (Visual.OpacityProperty, 0.0, st.Opacity));
        }
        return st;
    }

    /// <summary>WPF :1089: how long the stamp takes to land (0 with motion off).</summary>
    public static int StampLandMs => Amount > 0 ? GentleVisuals.RiseMs(280) : 0;

    private static double Amount => AmbientFxCanvas.Env.Level switch { MotionLevel.Off => 0, MotionLevel.Reduced => 0.5, _ => 1 };

    /// <summary>WPF :1154: a card comes up a little and in.</summary>
    public static void CardIn(Control card)
    {
        var k = Amount;
        card.RenderTransform ??= new TranslateTransform();
        if (k > 0) Run(card, GentleVisuals.RiseMs(220), new BackEaseOut(), (TranslateTransform.YProperty, 12 * k, 0.0), (Visual.OpacityProperty, 0.0, 1.0));
    }

    /// <summary>WPF :1168: the window fades, then <paramref name="then"/>.</summary>
    public static void FadeOutThen(Window w, Action then)
    {
        if (Amount <= 0) { then(); return; }
        var ms = GentleVisuals.FadeMs(160);
        Run(w, ms, new LinearEasing(), (Visual.OpacityProperty, w.Opacity, 0.0));
        DispatcherTimer.RunOnce(then, TimeSpan.FromMilliseconds(ms));
    }

    private static void Run(Animatable target, int ms, Easing easing, params (AvaloniaProperty P, double From, double To)[] props)
    {
        var a = new Animation { Duration = TimeSpan.FromMilliseconds(ms), Easing = easing, FillMode = FillMode.Forward };
        var start = new KeyFrame { Cue = new Cue(0) };
        var end = new KeyFrame { Cue = new Cue(1) };
        foreach (var (p, from, to) in props) { start.Setters.Add(new Setter(p, from)); end.Setters.Add(new Setter(p, to)); }
        a.Children.Add(start);
        a.Children.Add(end);
        _ = a.RunAsync(target);
    }

    // WPF :1197-1229: borderless, the violet desk with its top glow, sized to content, dragged by its desk.
    public static StackPanel Shell(Window w, double width, string tag)
    {
        w.Title = "CC Labs";
        w.Width = width;
        w.SizeToContent = SizeToContent.Height;
        w.WindowDecorations = WindowDecorations.None;
        w.Background = Brushes.Transparent;
        w.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        w.ShowInTaskbar = false;
        w.CanResize = false;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.PointerPressed += (_, e) =>
        {
            if (e.Source is Control c && c.FindAncestorOfType<Button>(includeSelf: true) == null && e.GetCurrentPoint(w).Properties.IsLeftButtonPressed)
                try { w.BeginMoveDrag(e); } catch (InvalidOperationException) { }
        };
        var body = new StackPanel();
        var desk = new RadialGradientBrush { GradientOrigin = new RelativePoint(0.3, 0, RelativeUnit.Relative), Center = new RelativePoint(0.3, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(1.0, RelativeUnit.Relative), RadiusY = new RelativeScalar(1.15, RelativeUnit.Relative) };
        desk.GradientStops.Add(new GradientStop(Color.FromRgb(0x2C, 0x1A, 0x4F), 0));
        desk.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x10, 0x30), 0.55));
        desk.GradientStops.Add(new GradientStop(Color.FromRgb(0x14, 0x0B, 0x26), 1));
        var card = new Border
        {
            Background = desk, BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x29, 0x6B)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Margin = new Thickness(14),
            Padding = new Thickness(22, 20), Tag = tag, Child = body, RenderTransform = new TranslateTransform(),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 24, Color = Color.FromArgb(0x8C, 0, 0, 0) }),
        };
        TextElement.SetFontFamily(card, PrintFont);
        TextElement.SetForeground(card, ChalkBrush);
        w.Content = card;
        CardIn(card);
        return body;
    }
}
