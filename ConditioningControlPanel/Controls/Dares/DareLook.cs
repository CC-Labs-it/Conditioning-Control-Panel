using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Controls.Dares;

/// <summary>Which big button: the mint go (Accept, Pay, Start it) or the hot pink send.</summary>
internal enum DareBig { Go, Hot }

/// <summary>
/// The Dares paint box: the owner's dare-slip mockup (handoff-1118 mockup/dare-slip.html) in CCP
/// colours. A pink paper slip (ruled lines, a scalloped top, a slight tilt) on a deep violet desk;
/// the sentence in a condensed print face, every filled blank a white handwritten fill box with its
/// kind's colour and icon; the tray under the slip with big tiles, amount chips, and pressable
/// buttons. Icons are the mockup's own SVG paths drawn as WPF geometry. Every move reads
/// <see cref="MotionFx.Level"/> and asks <see cref="GentleVisuals"/> for its rise, and has an IN
/// and an OUT. The only effects are static drop shadows on empty, bitmap-cached siblings.
/// </summary>
internal static class DareLook
{
    // ---- tokens ------------------------------------------------------------------------

    public static readonly Color Desk = Rgb(0x1A, 0x10, 0x30);
    public static readonly Color DeskTop = Rgb(0x2C, 0x1A, 0x4F);
    public static readonly Color DeskLow = Rgb(0x14, 0x0B, 0x26);
    public static readonly Color Chalk = Rgb(0xEF, 0xE6, 0xFF);
    public static readonly Color Muted = Rgb(0xA9, 0x93, 0xCF);
    public static readonly Color SlipPaper = Rgb(0xFF, 0xD3, 0xE6);
    public static readonly Color SlipLine = Rgb(0xF3, 0xA9, 0xC9);
    public static readonly Color Print = Rgb(0x4B, 0x1F, 0x45);
    public static readonly Color Ink = Rgb(0x3A, 0x1D, 0x78);
    public static readonly Color FillPaper = Rgb(0xFF, 0xFA, 0xF8);
    public static readonly Color StampRed = Rgb(0xD2, 0x26, 0x5F);
    public static readonly Color Go = Rgb(0x2F, 0xD3, 0x9A);
    public static readonly Color GoShadow = Rgb(0x17, 0x80, 0x5A);
    public static readonly Color GoInk = Rgb(0x05, 0x33, 0x24);
    public static readonly Color Tick = Rgb(0x1F, 0xA8, 0x77);
    public static readonly Color Hot = Rgb(0xFF, 0x4F, 0xA3);
    public static readonly Color HotShadow = Rgb(0xA3, 0x17, 0x5F);
    public static readonly Color TileFace = Rgb(0xF7, 0xEE, 0xF8);
    public static readonly Color TileEdge = Rgb(0xDC, 0xC6, 0xDD);
    public static readonly Color CardInk = Rgb(0x2E, 0x18, 0x3A);
    public static readonly Color Gold = Rgb(0xB0, 0x7A, 0x00);
    public static readonly Color GoldFill = Rgb(0xFF, 0xD4, 0x5E);
    public static readonly Color WarnOnDesk = Rgb(0xFF, 0xCF, 0x6B);

    public static readonly Brush ChalkBrush = B(Chalk);
    public static readonly Brush MutedBrush = B(Muted);
    public static readonly Brush PrintBrush = B(Print);
    public static readonly Brush InkBrush = B(Ink);
    public static readonly Brush CardInkBrush = B(CardInk);
    public static readonly Brush TileBrush = B(TileFace);
    public static readonly Brush TileEdgeBrush = B(TileEdge);
    public static readonly Brush SlipBrush = B(SlipPaper);
    public static readonly Brush FillBrush = B(FillPaper);
    public static readonly Brush GoldFillBrush = B(GoldFill);
    public static readonly Brush WhiteBrush = B(Colors.White);
    public static readonly Brush WarnBrush = B(WarnOnDesk);
    public static readonly Brush GoodOnCardBrush = B(GoShadow);
    public static readonly Brush WarnOnCardBrush = B(Rgb(0xA8, 0x6A, 0x00));

    public static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    public static Brush B(Color c) => FriendsLook.Frozen(c);
    public static Brush Tint(Color c, byte alpha) => FriendsLook.Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));

    // ---- fonts -------------------------------------------------------------------------

    /// <summary>Barlow Condensed (Medium + Bold ship in Fonts/Dares), Segoe UI behind it.</summary>
    public static readonly FontFamily PrintFont = LoadFont("./Fonts/Dares/#Barlow Condensed", "Arial Narrow, Segoe UI");

    /// <summary>Caveat Bold, the handwriting in every fill.</summary>
    public static readonly FontFamily HandFont = LoadFont("./Fonts/Dares/#Caveat", "Segoe Print, Segoe UI");

    private static FontFamily LoadFont(string relative, string fallback)
    {
        try { return new FontFamily(new Uri("pack://application:,,,/"), relative + ", " + fallback); }
        catch (Exception) { return new FontFamily(fallback); } // swallow: no pack scheme (a test host); the fallback draws
    }

    // ---- icons (the mockup's I table, viewBox 24) ---------------------------------------

    private static readonly Dictionary<string, Geometry> IconTable = BuildIcons();

    private static Dictionary<string, Geometry> BuildIcons()
    {
        static Geometry R(double x, double y, double w, double h, double r) => new RectangleGeometry(new Rect(x, y, w, h), r, r);
        static Geometry C(double cx, double cy, double r) => new EllipseGeometry(new Point(cx, cy), r, r);
        static Geometry P(string d) => Geometry.Parse(d);
        static Geometry G(params Geometry[] parts)
        {
            var g = new GeometryGroup();
            foreach (var p in parts) g.Children.Add(p);
            g.Freeze();
            return g;
        }
        const string Eye = "M2.5 12 C2.5 12 6 5.5 12 5.5 C18 5.5 21.5 12 21.5 12 C21.5 12 18 18.5 12 18.5 C6 18.5 2.5 12 2.5 12 Z";
        return new Dictionary<string, Geometry>
        {
            ["picture"] = G(R(3, 5, 18, 14, 2), C(9, 10, 1.6), P("M4 17 L9 13 L13 16 L16 14 L20 17")),
            ["clock"] = G(C(12, 12, 8.5), P("M12 7.5 V12 L15 14")),
            ["play"] = G(C(12, 12, 8.5), P("M10 8.5 L15.5 12 L10 15.5 Z")),
            ["lock"] = G(R(5, 11, 14, 9, 2), P("M8 11 V8 A4 4 0 0 1 16 8 V11")),
            ["ldn"] = G(R(3, 4, 18, 13, 1.5), P("M9 21 H15 M12 17 V21"), R(9.5, 10, 5, 4, 0.8), P("M10.5 10 V9 A1.5 1.5 0 0 1 13.5 9 V10")),
            ["envelope"] = G(R(3, 6, 18, 13, 2), P("M3.5 7.5 L12 13.5 L20.5 7.5")),
            ["star"] = G(P("M12 3.5 L14.6 8.9 L20.5 9.7 L16.2 13.8 L17.2 19.6 L12 16.9 L6.8 19.6 L7.8 13.8 L3.5 9.7 L9.4 8.9 Z")),
            ["bubble"] = G(C(12, 12, 8), P("M8.5 9.5 A4 4 0 0 1 11.5 7.5")),
            ["video"] = G(R(3, 5, 18, 14, 2), P("M10 9 L15 12 L10 15 Z")),
            ["pc"] = G(R(3, 4, 18, 13, 1.5), P("M9 21 H15 M12 17 V21")),
            ["versus"] = G(P("M4 8 H17 L14 5 M20 16 H7 L10 19")),
            ["eye"] = G(P(Eye), C(12, 12, 2.8)),
            ["eyeoff"] = G(P(Eye), P("M4 4 L20 20")),
            ["check"] = G(P("M5 12.5 L9.5 17 L19 7.5")),
            ["cross"] = G(P("M6 6 L18 18 M18 6 L6 18")),
            ["arrow"] = G(P("M5 12 H18 M13 7 L18 12 L13 17")),
        };
    }

    /// <summary>One of the mockup's icons: a stroked path in a 24 box, scaled to <paramref name="size"/>.</summary>
    public static FrameworkElement Icon(string name, double size, Brush stroke, Brush? fill = null, double thickness = 1.7)
    {
        var path = new Path
        {
            Data = IconTable.TryGetValue(name, out var g) ? g : IconTable["envelope"],
            Stroke = stroke,
            Fill = fill,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox
        {
            Width = size,
            Height = size,
            Child = canvas,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
    }

    // ---- kinds -------------------------------------------------------------------------

    public static Color ToneColor(DareTone t) => t switch
    {
        DareTone.Cant => Rgb(0xC2, 0x2B, 0x2B),
        DareTone.Duel => Rgb(0x2C, 0x8A, 0x57),
        DareTone.Pics => Rgb(0xE0, 0x66, 0x3A),
        DareTone.Minutes => Rgb(0x1F, 0x8A, 0x8A),
        DareTone.Video => Rgb(0xCC, 0x3D, 0x7A),
        DareTone.Session => Rgb(0x8A, 0x3F, 0x8F),
        DareTone.Bubbles => Rgb(0x2F, 0x86, 0xC9),
        DareTone.Dare => Rgb(0xC2, 0x2B, 0x2B),
        DareTone.Sp => Gold,
        DareTone.Lock => Rgb(0x33, 0x61, 0x8F),
        DareTone.CameraOn => Rgb(0x2C, 0x8A, 0x57),
        DareTone.CameraOff => Rgb(0x7A, 0x6A, 0x8F),
        _ => Ink,   // Lockdown (inverted ink), When, Who
    };

    public static string ToneIcon(DareTone t) => t switch
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

    private static readonly Color[] FriendHues =
    {
        Rgb(0x1F, 0x8A, 0x8A), Rgb(0xCC, 0x3D, 0x7A), Rgb(0xE0, 0x66, 0x3A),
        Rgb(0x2F, 0x86, 0xC9), Rgb(0x8A, 0x3F, 0x8F), Rgb(0x2C, 0x8A, 0x57),
    };

    /// <summary>A friend's own hue, stable by name (the friend fill's border and initial disc).</summary>
    public static Color FriendColor(string? name)
    {
        int h = 0;
        foreach (var ch in name ?? "") h = unchecked(h * 31 + ch);
        return FriendHues[(h & 0x7FFFFFFF) % FriendHues.Length];
    }

    private static string Initial(string? name)
    {
        var i = FriendsDrawerRules.Initials(name ?? "");
        return string.IsNullOrEmpty(i) ? "?" : i.Substring(0, 1);
    }

    /// <summary>A round initial disc in the friend's hue, a handwritten letter in it.</summary>
    public static FrameworkElement InitialDisc(string? name, double size, Color? hue = null)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Ellipse { Fill = B(hue ?? FriendColor(name)) });
        g.Children.Add(Hand(Initial(name), size * 0.8, WhiteBrush, center: true));
        return g;
    }

    /// <summary>A friend's face for a tile: the real avatar when there is one, else the initial disc.</summary>
    public static FrameworkElement Face(string name, string? url, double size)
        => string.IsNullOrEmpty(url) ? InitialDisc(name, size) : FriendsLook.Avatar(name, url, size);

    // ---- text --------------------------------------------------------------------------

    public static TextBlock Words(string text, double size, Brush fg, bool wrap = false, FontWeight? weight = null) => new()
    {
        Text = text,
        FontFamily = PrintFont,
        FontSize = size,
        Foreground = fg,
        FontWeight = weight ?? FontWeights.Medium,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Handwriting (Caveat Bold) with the mockup's line-height 1.</summary>
    public static TextBlock Hand(string text, double size, Brush fg, bool center = false)
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = HandFont,
            FontWeight = FontWeights.Bold,
            FontSize = size,
            Foreground = fg,
            LineHeight = size,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (center) t.HorizontalAlignment = HorizontalAlignment.Center;
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    /// <summary>Uppercase print with letter spacing (<paramref name="trackingEm"/>, as the mockup's CSS).</summary>
    public static FrameworkElement Caps(string text, double size, Brush fg, FontWeight weight, double trackingEm)
        => new DareCaps
        {
            Text = (text ?? "").ToUpper(CultureInfo.CurrentCulture),
            Font = PrintFont,
            Size = size,
            Weight = weight,
            Ink = fg,
            TrackingEm = trackingEm,
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>Plain words split at spaces, so a sentence wraps word by word in its WrapPanel.</summary>
    public static void AddWords(Panel row, string text, double size, Brush fg)
    {
        var parts = text.Split(' ');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            var w = Words(parts[i], size, fg);
            w.TextTrimming = TextTrimming.None;
            w.Margin = new Thickness(0, 0, i < parts.Length - 1 ? size * 0.24 : 0, 0);
            row.Children.Add(w);
        }
    }

    private static string? Tip(string? key, bool changeable)
    {
        if (key == null) return changeable ? Loc.Get("dares_tip_change") : null;
        var t = Loc.Get(key);
        return changeable ? t + " " + Loc.Get("dares_tip_change") : t;
    }

    // ---- the plain button every piece here is built on ---------------------------------

    private static ControlTemplate? _bare;

    private static ControlTemplate BareTemplate()
    {
        if (_bare != null) return _bare;
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        var t = new ControlTemplate(typeof(Button)) { VisualTree = cp };
        t.Seal();
        return _bare = t;
    }

    /// <summary>A button with no chrome: the content IS the look. Named for UIA by the caller.</summary>
    public static Button Bare(object content, string name)
    {
        var b = new Button
        {
            Content = content,
            Template = BareTemplate(),
            Cursor = Cursors.Hand,
            Focusable = false,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        AutomationProperties.SetName(b, name);
        b.IsEnabledChanged += (_, _) => b.Opacity = b.IsEnabled ? 1.0 : 0.45;
        return b;
    }

    // ---- the fill (a filled blank) ------------------------------------------------------

    /// <summary>A filled blank: a white handwritten box, its kind's colour on the border and the
    /// icon; the friend shows a round initial; a Lockdown is ink inverted. <paramref name="onClick"/>
    /// null = read only. <paramref name="small"/> = the drawer strip's size.</summary>
    public static FrameworkElement Fill(DareSeg seg, Action? onClick, bool current = false, bool small = false)
    {
        bool inverted = seg.Tone == DareTone.Lockdown;
        bool who = seg.Tone == DareTone.Who;
        var c = who ? FriendColor(seg.Text) : ToneColor(seg.Tone);
        double icon = small ? 17 : 24, text = small ? 20 : 29;
        var fg = inverted ? WhiteBrush : InkBrush;

        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (who) sp.Children.Add(InitialDisc(seg.Text, icon));
        else sp.Children.Add(Icon(ToneIcon(seg.Tone), icon, inverted ? WhiteBrush : B(c), seg.Tone == DareTone.Sp ? GoldFillBrush : null));
        var words = Hand(seg.Text, text, fg);
        words.Margin = new Thickness(small ? 4 : 6, 0, 0, small ? 1 : 2);
        if (small) words.TextTrimming = TextTrimming.CharacterEllipsis;
        sp.Children.Add(words);

        var rest = inverted ? InkBrush : FillBrush;
        var face = new Border
        {
            Child = sp,
            Background = rest,
            // Small = on the drawer's dark violet: an inverted fill keeps a lilac edge so it reads.
            BorderBrush = inverted ? (small ? B(Rgb(0x8F, 0x6F, 0xE0)) : InkBrush) : B(c),
            BorderThickness = new Thickness(seg.Tone == DareTone.When ? 1.5 : small ? 1.5 : 2),
            CornerRadius = new CornerRadius(small ? 6 : 8),
            Padding = small ? new Thickness(4, 1, 7, 1) : new Thickness(7, 3, 10, 3),
        };
        FrameworkElement body = face;
        if (!small)
        {
            var g = new Grid();
            g.Children.Add(new Border { Background = Tint(Colors.Black, 0x22), CornerRadius = new CornerRadius(8), Margin = new Thickness(1, 2, -1, -2) });
            g.Children.Add(face);
            body = g;
        }
        if (current)
            body = new Border { Child = body, BorderBrush = InkBrush, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(11), Padding = new Thickness(1) };

        var tip = Tip(seg.TipKey, onClick != null);
        FrameworkElement result;
        if (onClick == null)
        {
            body.ToolTip = tip;
            AutomationProperties.SetName(body, seg.Text);
            result = body;
        }
        else
        {
            var b = Bare(body, seg.Text);
            b.ToolTip = tip;
            b.Click += (_, _) => onClick();
            var hover = inverted ? B(Rgb(0x4A, 0x2A, 0x90)) : WhiteBrush;
            b.MouseEnter += (_, _) => face.Background = hover;
            b.MouseLeave += (_, _) => face.Background = rest;
            result = b;
        }
        result.Margin = small ? new Thickness(0) : new Thickness(4, 0, 4, 0);
        result.VerticalAlignment = VerticalAlignment.Center;
        result.HorizontalAlignment = HorizontalAlignment.Left;
        result.Tag = "dares-chip:" + seg.Step;
        return result;
    }

    private static Brush DashBrush(Color c)
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 9, 2))));
        g.Children.Add(new GeometryDrawing(B(c), null, new RectangleGeometry(new Rect(0, 0, 5, 2))));
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 9, 2), ViewportUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    private static readonly Brush PrintDash = DashBrush(Print);

    /// <summary>A blank not yet filled: a dashed underline with its small uppercase hint; the one in
    /// hand turns solid ink and nudges a few times.</summary>
    public static FrameworkElement Blank(DareSeg seg, bool current)
    {
        var t = Caps(seg.Text, 14, current ? InkBrush : PrintBrush, FontWeights.Medium, 0.12);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        var line = new Border
        {
            Child = t,
            MinWidth = 92,
            Height = 28,
            Padding = new Thickness(6, 0, 6, 0),
            Margin = new Thickness(4, 0, 4, 0),
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = current ? InkBrush : PrintDash,
            Background = Brushes.Transparent,
            Opacity = current ? 1 : 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "dares-blank:" + seg.Step,
        };
        AutomationProperties.SetName(line, seg.Text);
        if (current) { PopIn(line); Nudge(line); }
        return line;
    }

    /// <summary>The blank in hand glows paper-white three times and settles clear (IN and OUT in one
    /// reversing pulse; never a Forever loop, and none while Gentle visuals is on).</summary>
    private static void Nudge(Border line)
    {
        if (Amount <= 0 || GentleVisuals.On) return;
        var brush = new SolidColorBrush(Color.FromArgb(0, 0xFF, 0xFF, 0xFF));
        line.Background = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF), TimeSpan.FromMilliseconds(550))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }

    /// <summary>The round mark at the head of an outcome line: tick (green), cross (red), or an ink
    /// disc with a handwritten initial for a duel side.</summary>
    public static FrameworkElement Mark(DareMark m, string? themName)
    {
        var disc = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        disc.Children.Add(new Ellipse { Fill = B(m switch { DareMark.Yes => Tick, DareMark.No => StampRed, _ => Ink }) });
        if (m is DareMark.Yes or DareMark.No)
        {
            var i = Icon(m == DareMark.Yes ? "check" : "cross", 17, WhiteBrush, null, 3);
            i.HorizontalAlignment = HorizontalAlignment.Center;
            disc.Children.Add(i);
        }
        else
        {
            var letter = m == DareMark.Me ? Loc.Get("dares_mark_me") : Initial(themName);
            disc.Children.Add(Hand(letter, letter.Length > 1 ? 15 : 20, WhiteBrush, center: true));
        }
        AutomationProperties.SetName(disc, m == DareMark.Me ? Loc.Get("dares_mark_me") : m == DareMark.Them ? themName ?? "" : "");
        return disc;
    }

    /// <summary>Draws sentence lines on the ruled paper: print words, fills and blanks flowing on a
    /// 44 px line grid. <paramref name="onChip"/> null = read only.</summary>
    public static StackPanel Sentence(IReadOnlyList<DareLine> lines, string? friendName, DareStep? current,
        Action<DareStep>? onChip, double size = 23, double lineHeight = 44, Brush? textBrush = null)
    {
        var fg = textBrush ?? PrintBrush;
        var host = new StackPanel { Tag = "dares-sentence" };
        foreach (var line in lines)
        {
            var row = new WrapPanel { ItemHeight = lineHeight };
            if (line.Mark != DareMark.None) row.Children.Add(Mark(line.Mark, friendName));
            foreach (var seg in line.Segs)
            {
                switch (seg.Kind)
                {
                    case DareSegKind.Text:
                        AddWords(row, seg.Text, size, fg);
                        break;
                    case DareSegKind.Blank:
                        row.Children.Add(Blank(seg, seg.Step == current));
                        break;
                    default:
                        var step = seg.Step ?? DareStep.Who;
                        row.Children.Add(Fill(seg, onChip == null ? null : () => onChip(step), seg.Step == current));
                        break;
                }
            }
            host.Children.Add(row);
        }
        return host;
    }

    /// <summary>One line of words with an optional fill at its end, on a line grid (the debt card's
    /// "You lost. You owe ___").</summary>
    public static WrapPanel Line(string text, FrameworkElement? fill, double size, Brush fg, double lineHeight)
    {
        var row = new WrapPanel { ItemHeight = lineHeight };
        AddWords(row, text, size, fg);
        if (fill != null) row.Children.Add(fill);
        return row;
    }

    // ---- the slip ----------------------------------------------------------------------

    private static readonly Brush Ruled = MakeRuled();

    private static Brush MakeRuled()
    {
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 4, 44))));
        g.Children.Add(new GeometryDrawing(B(SlipLine), null, new RectangleGeometry(new Rect(0, 43, 4, 1))));
        // The mockup starts its rule tile 70 px down: 26 padding + 32 head + 12 gap, so every
        // sentence line sits on a rule.
        var b = new DrawingBrush(g) { TileMode = TileMode.Tile, Viewport = new Rect(0, 26, 4, 44), ViewportUnits = BrushMappingMode.Absolute };
        b.Freeze();
        return b;
    }

    private static Geometry Scallops(Size s)
    {
        var holes = new GeometryGroup();
        for (double x = 9; x < s.Width + 9; x += 18) holes.Children.Add(new EllipseGeometry(new Point(x, -1), 5.3, 5.3));
        var g = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(s)), holes);
        g.Freeze();
        return g;
    }

    private static Border ShadowUnder(Brush fill, CornerRadius r, Thickness margin, double blur, double depth, double opacity) => new()
    {
        Background = fill,
        CornerRadius = r,
        Margin = margin,
        IsHitTestVisible = false,
        Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity },
        CacheMode = new BitmapCache(),
    };

    /// <summary>The pink betting slip: perforated top, ruled lines, a static -0.6 deg tilt and a
    /// drop shadow under it. <paramref name="paper"/> is where a stamp lands.</summary>
    public static Grid Slip(FrameworkElement head, FrameworkElement body, out Grid paper)
    {
        var content = new StackPanel { Margin = new Thickness(22, 26, 22, 22) };
        content.Children.Add(head);
        content.Children.Add(body);
        var p = new Grid { Background = SlipBrush, Tag = "dares-slip-paper" };
        p.Children.Add(new Rectangle { Fill = Ruled, IsHitTestVisible = false });
        p.Children.Add(new Border { BorderBrush = Tint(Colors.White, 0x66), BorderThickness = new Thickness(0, 1, 0, 0), IsHitTestVisible = false });
        p.Children.Add(content);
        p.SizeChanged += (_, e) => p.Clip = Scallops(e.NewSize);
        paper = p;

        var root = new Grid
        {
            Margin = new Thickness(2, 4, 2, 8),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-0.6),
            Tag = "dares-slip",
        };
        root.Children.Add(ShadowUnder(SlipBrush, new CornerRadius(2), new Thickness(10, 16, 10, 0), 26, 12, 0.6));
        root.Children.Add(p);
        return root;
    }

    /// <summary>The slip's header row: the title in spaced capitals, a rule under it, the (?) and the
    /// close cross at the right.</summary>
    public static FrameworkElement SlipHead(string title, string? helpKey, Action? onClose)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = Caps(title, 19, PrintBrush, FontWeights.Bold, 0.16);
        t.VerticalAlignment = VerticalAlignment.Bottom;
        t.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(t);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        if (helpKey != null) right.Children.Add(Help(helpKey, PrintBrush));
        if (onClose != null) right.Children.Add(Close(onClose, PrintBrush));
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return new Border
        {
            Child = g,
            Height = 32,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(0, 0, 0, 5),
            BorderBrush = PrintBrush,
            BorderThickness = new Thickness(0, 0, 0, 2),
            Tag = "dares-head",
        };
    }

    /// <summary>A heading on the desk (the debt card): chalk spaced capitals and an optional (?).</summary>
    public static FrameworkElement DeskHead(string title, string? helpKey)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14), Tag = "dares-head" };
        sp.Children.Add(Caps(title, 19, ChalkBrush, FontWeights.Bold, 0.16));
        if (helpKey != null) sp.Children.Add(Help(helpKey, ChalkBrush));
        return sp;
    }

    /// <summary>The tray heading for the blank in hand: muted spaced capitals, an optional (?).</summary>
    public static FrameworkElement Ask(string text, string? helpKey, Brush? fg = null)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        sp.Children.Add(Caps(text, 15, fg ?? MutedBrush, FontWeights.Medium, 0.16));
        if (helpKey != null) sp.Children.Add(Help(helpKey, fg ?? MutedBrush));
        return sp;
    }

    /// <summary>The light card the debt card and its consent list sit on (the mockup's lockcard).
    /// The returned grid is where a stamp lands.</summary>
    public static Grid LockCard(out StackPanel inner)
    {
        inner = new StackPanel { Margin = new Thickness(18) };
        var g = new Grid { Tag = "dares-lockcard" };
        g.Children.Add(ShadowUnder(TileBrush, new CornerRadius(10), new Thickness(10, 16, 10, 0), 26, 12, 0.6));
        g.Children.Add(new Border { Background = TileBrush, CornerRadius = new CornerRadius(10), Child = inner });
        return g;
    }

    /// <summary>The "how the camera works" box: dashed, a little darker than the desk.</summary>
    public static FrameworkElement HowBox(params FrameworkElement[] rows)
    {
        var g = new Grid { MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Tag = "dares-camera-how" };
        g.Children.Add(new Rectangle
        {
            Fill = Tint(Colors.Black, 0x33),
            Stroke = MutedBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            RadiusX = 10,
            RadiusY = 10,
        });
        var sp = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i].Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 0);
            sp.Children.Add(rows[i]);
        }
        g.Children.Add(sp);
        return g;
    }

    public static FrameworkElement HowRow(FrameworkElement lead, string text)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lead.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(lead);
        var t = Words(text, 18, ChalkBrush, wrap: true);
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        return g;
    }

    /// <summary>The camera tick strip: 30 looks, a few missed.</summary>
    public static FrameworkElement Ticks(int count, ICollection<int> missed)
    {
        var u = new UniformGrid { Rows = 1, Columns = count, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < count; i++)
        {
            bool miss = missed.Contains(i);
            u.Children.Add(new Border
            {
                Margin = new Thickness(1, 0, 1, 0),
                CornerRadius = new CornerRadius(2),
                Background = miss ? Brushes.Transparent : B(Go),
                BorderBrush = MutedBrush,
                BorderThickness = new Thickness(miss ? 1 : 0),
            });
        }
        return u;
    }

    // ---- the tray: tiles, chips, buttons ----------------------------------------------

    /// <summary>A kind's 46 px disc with its 28 px white icon (a padlock badge when it is an ad).</summary>
    public static FrameworkElement Disc(DareTone tone, bool locked = false)
    {
        var g = new Grid { Width = 46, Height = 46 };
        var disc = new Grid { Opacity = locked ? 0.55 : 1 };
        disc.Children.Add(new Ellipse { Fill = B(ToneColor(tone)) });
        var i = Icon(ToneIcon(tone), 28, WhiteBrush);
        i.HorizontalAlignment = HorizontalAlignment.Center;
        disc.Children.Add(i);
        g.Children.Add(disc);
        if (locked)
        {
            var badge = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -3) };
            badge.Children.Add(new Ellipse { Fill = WhiteBrush, Stroke = InkBrush, StrokeThickness = 1.2 });
            var l = Icon("lock", 13, InkBrush, null, 2.2);
            l.HorizontalAlignment = HorizontalAlignment.Center;
            badge.Children.Add(l);
            g.Children.Add(badge);
        }
        return g;
    }

    /// <summary>An option tile: light card, the kind's disc and icon, the word under it, the kind's
    /// colour along the bottom; selected = an ink outline. Shut tiles say why in the tooltip; an
    /// upsell tile (locked stays visible, owner) wears the padlock and the Basic plate.</summary>
    public static Button Tile(DareTone tone, string label, string? tipKey, string? shutKey, bool pressed, Action onClick, string tag,
        bool upsell = false)
        => TileOf(Disc(tone, upsell), label, ToneColor(tone), tipKey == null ? null : Loc.Get(tipKey), shutKey, pressed, onClick,
            upsell ? tag + ":locked" : tag, upsell);

    /// <summary>A friend tile: their face over their name.</summary>
    public static Button FaceTile(string name, string? avatarUrl, bool pressed, Action onClick, string tag)
        => TileOf(Face(name, avatarUrl, 46), name, null, name, null, pressed, onClick, tag, false);

    private static Button TileOf(FrameworkElement top, string label, Color? accent, string? tip, string? shutKey, bool pressed,
        Action onClick, string tag, bool upsell)
    {
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        top.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(top);
        var w = Words(label, 16, B(CardInk), wrap: true);
        w.TextAlignment = TextAlignment.Center;
        w.HorizontalAlignment = HorizontalAlignment.Center;
        w.MaxWidth = 112;
        w.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(w);
        if (upsell && FriendsLook.TierPlate(1, 13) is { } plate)
        {
            plate.Margin = new Thickness(0, 4, 0, 0);
            plate.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(plate);
        }
        var rest = pressed ? WhiteBrush : TileBrush;
        var face = new Border
        {
            Background = rest,
            BorderBrush = TileEdgeBrush,
            BorderThickness = new Thickness(1, 1, 1, 0),
            CornerRadius = new CornerRadius(8, 8, 3, 3),
            Padding = new Thickness(8, 10, 8, 8),
            Margin = new Thickness(0, 0, 0, 4),
            Child = sp,
        };
        var g = new Grid { MinWidth = 92 };
        g.Children.Add(ShadowUnder(TileBrush, new CornerRadius(8), new Thickness(4, 8, 4, 0), 10, 5, 0.6));
        g.Children.Add(new Border { Background = accent is { } a ? B(a) : TileEdgeBrush, CornerRadius = new CornerRadius(8) });
        g.Children.Add(face);
        if (pressed) g.Children.Add(new Border { BorderBrush = InkBrush, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(8), IsHitTestVisible = false });

        var b = Bare(g, label);
        b.Margin = new Thickness(0, 0, 10, 10);
        b.VerticalAlignment = VerticalAlignment.Top;
        b.Tag = tag;
        if (shutKey != null)
        {
            b.IsEnabled = false;
            ToolTipService.SetShowOnDisabled(b, true);
            b.ToolTip = Loc.Get(shutKey);
            AutomationProperties.SetHelpText(b, Loc.Get(shutKey));
        }
        else if (tip != null) b.ToolTip = tip;
        b.Click += (_, _) => onClick();
        b.MouseEnter += (_, _) => { if (b.IsEnabled) face.Background = WhiteBrush; };
        b.MouseLeave += (_, _) => face.Background = rest;
        HoverTilt(b);
        return b;
    }

    /// <summary>An amount or deadline chip: a light pill, bold print, tabular figures.</summary>
    public static Button Choice(string label, string? tip, Action onClick, string tag)
    {
        var t = Words(label, 19, B(CardInk), weight: FontWeights.Bold);
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        var face = new Border
        {
            Background = TileBrush,
            BorderBrush = TileEdgeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(99),
            Padding = new Thickness(16, 5, 16, 6),
            Child = t,
        };
        var g = new Grid();
        g.Children.Add(ShadowUnder(TileBrush, new CornerRadius(99), new Thickness(8, 6, 8, 1), 7, 3, 0.4));
        g.Children.Add(face);
        var b = Bare(g, label);
        b.Margin = new Thickness(0, 0, 8, 8);
        b.ToolTip = tip;
        b.Tag = tag;
        b.MouseEnter += (_, _) => face.Background = WhiteBrush;
        b.MouseLeave += (_, _) => face.Background = TileBrush;
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>Swaps a chip's words (Copy -> Copied) without losing its look.</summary>
    public static void SetChoiceText(Button chip, string text)
    {
        if (chip.Content is Grid { Children.Count: > 1 } g && g.Children[1] is Border { Child: TextBlock t }) t.Text = text;
        AutomationProperties.SetName(chip, text);
    }

    /// <summary>The big pressable button: spaced capitals on colour, a 6 px darker ledge under it
    /// that the face presses down onto.</summary>
    public static Button Big(string text, DareBig tone, Action onClick, string tag)
    {
        var (bg, ledge, fg) = tone == DareBig.Go ? (Go, GoShadow, GoInk) : (Hot, HotShadow, Colors.White);
        var move = new TranslateTransform();
        var face = new Border
        {
            Background = B(bg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(30, 9, 30, 10),
            Margin = new Thickness(0, 0, 0, 6),
            Child = Caps(text, 24, B(fg), FontWeights.Bold, 0.14),
            RenderTransform = move,
        };
        var g = new Grid();
        g.Children.Add(new Border { Background = B(ledge), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 6, 0, 0) });
        g.Children.Add(face);
        var b = Bare(g, text);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.VerticalAlignment = VerticalAlignment.Top;
        b.Margin = new Thickness(0, 0, 12, 10);
        b.Tag = tag;
        b.Click += (_, _) => onClick();
        b.PreviewMouseLeftButtonDown += (_, _) => PressTo(move, 4);
        b.PreviewMouseLeftButtonUp += (_, _) => PressTo(move, 0);
        b.MouseLeave += (_, _) => PressTo(move, 0);
        return b;
    }

    /// <summary>The quiet (ghost) button: an outline, spaced capitals, an optional small line under it.
    /// <paramref name="onCard"/> = on the light lockcard rather than the desk.</summary>
    public static Button Quiet(string text, string? sub, Action onClick, string tag, bool onCard = false)
    {
        var fg = onCard ? B(CardInk) : ChalkBrush;
        var sp = new StackPanel();
        var head = Caps(text, 19, fg, FontWeights.Medium, 0.1);
        head.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(head);
        if (sub != null)
        {
            var s = Words(sub, 13, fg);
            s.Opacity = 0.75;
            sp.Children.Add(s);
        }
        var rest = Brushes.Transparent;
        var face = new Border
        {
            BorderBrush = onCard ? B(Rgb(0xC9, 0xAF, 0xCC)) : MutedBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 8, 20, 8),
            Background = rest,
            Child = sp,
        };
        var b = Bare(face, sub == null ? text : text + ", " + sub);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.VerticalAlignment = VerticalAlignment.Top;
        b.Margin = new Thickness(0, 0, 12, 10);
        b.Tag = tag;
        var hover = onCard ? WhiteBrush : Tint(Colors.White, 0x14);
        b.MouseEnter += (_, _) => face.Background = hover;
        b.MouseLeave += (_, _) => face.Background = rest;
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>The small round (?): its rule in a tooltip, pinned open on click, named for UIA.</summary>
    public static FrameworkElement Help(string key, Brush? fg = null)
    {
        var text = Loc.Get(key);
        var brush = fg ?? MutedBrush;
        var g = new Grid { Width = 22, Height = 22, Background = Brushes.Transparent };
        g.Children.Add(new Ellipse { Stroke = brush, StrokeThickness = 1.5 });
        var q = Words("?", 15, brush, weight: FontWeights.Bold);
        q.HorizontalAlignment = HorizontalAlignment.Center;
        q.Margin = new Thickness(0, -1, 0, 0);
        g.Children.Add(q);
        var b = Bare(g, text);
        b.Margin = new Thickness(8, 0, 0, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        b.ToolTip = text;
        b.Tag = "dares-help:" + key;
        var pin = new ToolTip { Content = text, Placement = PlacementMode.Bottom, PlacementTarget = b, MaxWidth = 320 };
        b.Click += (_, _) => pin.IsOpen = !pin.IsOpen;
        b.MouseLeave += (_, _) => pin.IsOpen = false;
        return b;
    }

    /// <summary>The close cross, named "Close" for UIA.</summary>
    public static Button Close(Action onClose, Brush? fg = null)
    {
        var g = new Grid { Width = 28, Height = 26, Background = Brushes.Transparent };
        var x = Icon("cross", 18, fg ?? MutedBrush, null, 2);
        x.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(x);
        var b = Bare(g, Loc.Get("dares_close"));
        b.Margin = new Thickness(6, 0, -6, 0);
        b.ToolTip = Loc.Get("dares_close");
        b.Tag = "dares-close";
        b.Click += (_, _) => onClose();
        return b;
    }

    /// <summary>A thin progress bar in a kind's colour (the drawer strip).</summary>
    public static FrameworkElement Bar(double fraction, Color c, double height = 7)
    {
        var track = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Tint(c, 0x2A) });
        var fill = new Border
        {
            CornerRadius = new CornerRadius(height / 2),
            Background = FriendsLook.Frozen(c),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        track.Children.Add(fill);
        track.SizeChanged += (_, e) => fill.Width = Math.Max(0, e.NewSize.Width * Math.Clamp(fraction, 0, 1));
        return track;
    }

    // ---- the stamp -----------------------------------------------------------------------

    /// <summary>The stamp: spaced capitals in a double frame, tilted -11 deg. IN = it thuds down from
    /// big; OUT (<see cref="StampOut"/>) = it fades. <paramref name="grey"/> = the print colour (Passed,
    /// Renounced), else stamp red.</summary>
    public static Border Stamp(string word, bool grey = false)
    {
        var c = grey ? Print : StampRed;
        var inner = new Border
        {
            BorderBrush = B(c),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 0, 14, 1),
            Child = Caps(word, 34, B(c), FontWeights.Bold, 0.12),
        };
        double rest = grey ? 0.6 : 0.86;
        var st = new Border
        {
            BorderBrush = B(c),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(1.5),
            Child = inner,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 14),
            IsHitTestVisible = false,
            Opacity = rest,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Tag = "dares-stamp",
        };
        AutomationProperties.SetName(st, word);
        var scale = new ScaleTransform(1, 1);
        var g = new TransformGroup();
        g.Children.Add(scale);
        g.Children.Add(new RotateTransform(-11));
        st.RenderTransform = g;
        double k = Amount;
        if (k > 0)
        {
            var dur = TimeSpan.FromMilliseconds(StampLandMs);
            var ease = new BackEase { Amplitude = 0.45 * k, EasingMode = EasingMode.EaseOut };
            var a = new DoubleAnimation(1 + 1.2 * k, 1, dur) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            st.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, rest, dur));
        }
        return st;
    }

    /// <summary>How long the stamp takes to land (0 with motion off).</summary>
    public static int StampLandMs => Amount > 0 ? GentleVisuals.RiseMs(280) : 0;

    public static void StampOut(FrameworkElement stamp, Action? done = null)
    {
        if (Amount <= 0) { done?.Invoke(); return; }
        var a = new DoubleAnimation(stamp.Opacity, 0, TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(240)));
        if (done != null) a.Completed += (_, _) => done();
        stamp.BeginAnimation(UIElement.OpacityProperty, a);
    }

    // ---- juice -------------------------------------------------------------------------

    private static double Amount => MotionFx.Level switch { MotionLevel.Off => 0, MotionLevel.Reduced => 0.5, _ => 1 };

    /// <summary>IN: a fill that just landed swells a little and settles.</summary>
    public static void PopIn(FrameworkElement el)
    {
        double k = Amount;
        if (k <= 0) return;
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        var s = new ScaleTransform(1, 1);
        el.RenderTransform = s;
        var rise = GentleVisuals.RiseMs(120);
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(rise + 260) };
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1 + 0.12 * k, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(rise)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(rise + 260)),
            new ElasticEase { Oscillations = 1, Springiness = 5, EasingMode = EasingMode.EaseOut }));
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>A tile lifts 3 px and tips 1 deg on hover (IN) and settles back on leave (OUT).</summary>
    private static void HoverTilt(Button b)
    {
        var rot = new RotateTransform(0);
        var up = new TranslateTransform(0, 0);
        var g = new TransformGroup();
        g.Children.Add(rot);
        g.Children.Add(up);
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        b.RenderTransform = g;
        void To(bool on)
        {
            double k = Amount;
            if (k <= 0) return;
            var dur = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(120));
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            up.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(on ? -3 * k : 0, dur) { EasingFunction = ease });
            rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(on ? -1 * k : 0, dur) { EasingFunction = ease });
        }
        b.MouseEnter += (_, _) => { if (b.IsEnabled) To(true); };
        b.MouseLeave += (_, _) => To(false);
    }

    /// <summary>The big button's face goes down onto its ledge (IN) and back up (OUT).</summary>
    private static void PressTo(TranslateTransform move, double y)
    {
        if (Amount <= 0) { move.BeginAnimation(TranslateTransform.YProperty, null); move.Y = y; return; }
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(70)))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>IN for a card window: up a little and in. OUT: <see cref="FadeOutThen"/>.</summary>
    public static void CardIn(FrameworkElement card)
    {
        double k = Amount;
        if (k <= 0) return;
        // A window card moves with its shadow, so the shadow never shows on its own.
        if (card.Parent is Grid { Tag: "dares-host" } host) card = host;
        var t = new TranslateTransform(0, 12 * k);
        card.RenderTransform = t;
        var dur = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(220));
        t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12 * k, 0, dur)
        { EasingFunction = new BackEase { Amplitude = 0.35 * k, EasingMode = EasingMode.EaseOut } });
        card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur));
    }

    public static void FadeOutThen(Window w, Action then)
    {
        if (Amount <= 0) { then(); return; }
        var a = new DoubleAnimation(w.Opacity, 0, TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(160)));
        a.Completed += (_, _) => then();
        w.BeginAnimation(UIElement.OpacityProperty, a);
    }

    // ---- the window --------------------------------------------------------------------

    private static Brush DeskBrush()
    {
        var b = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.3, 0),
            Center = new Point(0.3, 0),
            RadiusX = 1.0,
            RadiusY = 1.15,
        };
        b.GradientStops.Add(new GradientStop(DeskTop, 0));
        b.GradientStops.Add(new GradientStop(Desk, 0.55));
        b.GradientStops.Add(new GradientStop(DeskLow, 1));
        b.Freeze();
        return b;
    }

    /// <summary>The card window shell every Dares window wears: borderless, the violet desk with its
    /// top glow, a drag anywhere that is not a button (AGENTS: draggable borderless windows skip
    /// clickables). Its shadow is a cached sibling, so nothing inside sits under an effect.</summary>
    public static Border Shell(Window w, double width)
    {
        w.Title = "CC Labs";
        w.Width = width;
        w.SizeToContent = SizeToContent.Height;
        w.WindowStyle = WindowStyle.None;
        w.AllowsTransparency = true;
        w.Background = Brushes.Transparent;
        w.ShowInTaskbar = false;
        w.ResizeMode = ResizeMode.NoResize;
        w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        w.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed || OnClickable(e.OriginalSource as DependencyObject)) return;
            try { w.DragMove(); } catch (InvalidOperationException) { } // swallow: the button was already up
        };
        var card = new Border
        {
            Background = DeskBrush(),
            BorderBrush = B(Rgb(0x3D, 0x29, 0x6B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(14),
            Padding = new Thickness(22, 20, 22, 20),
        };
        TextElement.SetFontFamily(card, PrintFont);
        TextElement.SetForeground(card, ChalkBrush);
        var host = new Grid { Tag = "dares-host" };
        host.Children.Add(ShadowUnder(B(Desk), new CornerRadius(16), new Thickness(18), 24, 6, 0.55));
        host.Children.Add(card);
        w.Content = host;
        return card;
    }

    private static bool OnClickable(DependencyObject? d)
    {
        for (; d != null; d = FriendsDrawer.ParentOf(d))
        {
            if (d is ButtonBase or TextBoxBase) return true;
            if (d is FrameworkElement fe && fe.Cursor == Cursors.Hand) return true;
            if (d is Window) return false;
        }
        return false;
    }
}

/// <summary>
/// Spaced capitals: WPF text has no letter spacing, so this draws each character with the mockup's
/// tracking (an em fraction). One line; the tracking shrinks to fit a narrow slot. It reads to UIA as
/// one text element with its whole string.
/// </summary>
internal sealed class DareCaps : FrameworkElement
{
    public string Text { get; init; } = "";
    public FontFamily Font { get; init; } = new("Segoe UI");
    public double Size { get; init; } = 14;
    public FontWeight Weight { get; init; } = FontWeights.Normal;
    public Brush Ink { get; init; } = Brushes.Black;
    public double TrackingEm { get; init; }

    private List<FormattedText>? _chars;
    private double _track;

    private List<FormattedText> Chars()
    {
        if (_chars != null) return _chars;
        double dip = 1;
        try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch (Exception) { } // swallow: not on screen yet; 1:1 measures fine
        var tf = new Typeface(Font, FontStyles.Normal, Weight, FontStretches.Normal);
        var list = new List<FormattedText>();
        var e = StringInfo.GetTextElementEnumerator(Text);
        while (e.MoveNext())
            list.Add(new FormattedText(e.GetTextElement(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, Size, Ink, dip));
        return _chars = list;
    }

    protected override Size MeasureOverride(Size available)
    {
        var cs = Chars();
        double w = cs.Sum(c => c.WidthIncludingTrailingWhitespace);
        double h = cs.Count == 0 ? Size * 1.2 : cs.Max(c => c.Height);
        int gaps = Math.Max(0, cs.Count - 1);
        _track = TrackingEm * Size;
        if (gaps > 0 && !double.IsInfinity(available.Width) && w + gaps * _track > available.Width)
            _track = Math.Max(0, (available.Width - w) / gaps);
        return new Size(w + gaps * _track, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double x = 0;
        foreach (var c in Chars())
        {
            dc.DrawText(c, new Point(x, 0));
            x += c.WidthIncludingTrailingWhitespace + _track;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _chars = null;
        InvalidateMeasure();
        InvalidateVisual();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer : FrameworkElementAutomationPeer
    {
        public Peer(DareCaps owner) : base(owner) { }
        protected override string GetNameCore() => ((DareCaps)Owner).Text;
        protected override string GetClassNameCore() => "TextBlock";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    }
}
