using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
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
using Avalonia.Media.Transformation;
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

    /// <summary>WPF e0065f6b76 DareLook.cs:783: a compact choice's word size before any shrink to fit.</summary>
    public const double CompactWord = 15;

    // WPF 8d480c7d3f Controls/Dares/DareLook.cs:774-790: choices have a flat base edge.
    public static Button Choice(string label, Color color, string? tip, Action onClick, string tag, bool compact = false, bool on = false)
    {
        if (compact) return WordStamp(label, color, tip, onClick, tag, on);
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

    // ---- the friend card (WPF 67fadbf70f DareLook.cs:784-905): one family of light tiles, no ovals ----

    /// <summary>WPF Choice(compact): a word stamp, 15 px bold print on a light face with an <paramref name="edge"/>
    /// base, pressing onto it; <paramref name="on"/> sits pressed in, white, outlined in ink. The word shrinks to fit.</summary>
    private static Button WordStamp(string label, Color edge, string? tip, Action onClick, string tag, bool on)
    {
        var t = new TextBlock { Text = label, FontFamily = PrintFont, FontSize = CompactWord, FontWeight = FontWeight.Bold, Foreground = CardInkBrush };
        var face = new Border
        {
            Background = on ? Brushes.White : new SolidColorBrush(TileFace), BorderBrush = on ? InkBrush : new SolidColorBrush(edge),
            BorderThickness = on ? new Thickness(1.5) : new Thickness(1.5, 1.5, 1.5, 4), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 3, 9, 3), Margin = on ? new Thickness(0, 0, 0, 2.5) : default,
            Child = new Viewbox { Child = t, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
        };
        var b = Pressable(face, face, tag, label, on ? 2.5 : 0, 2.5);
        b.Margin = new Thickness(0, 0, 8, 8);
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>WPF :826: a rounded square in a hue with a white icon, the friend card's lead mark.</summary>
    public static Control Block(IconKind icon, Color hue, double size = 26)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { Background = new SolidColorBrush(hue), CornerRadius = new CornerRadius(5) });
        g.Children.Add(Icon(icon, size * 0.68, Brushes.White));
        return g;
    }

    /// <summary>WPF :843: a block, then bold print that wraps: a tile's content.</summary>
    public static Control Labelled(Control block, string text, double size = 16)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        g.Children.Add(block);
        var w = new TextBlock { Text = text, FontFamily = PrintFont, FontSize = size, FontWeight = FontWeight.Bold, Foreground = CardInkBrush,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 1) };
        Grid.SetColumn(w, 1);
        g.Children.Add(w);
        return g;
    }

    private const double Ledge = 4;

    /// <summary>WPF :862: a light face on a ledge of <paramref name="edge"/>, pressing down onto it;
    /// <paramref name="on"/> sits pressed in, white, outlined in ink. Hover whitens the face.</summary>
    public static Button PressTile(Control content, Color edge, bool on, string tag, string name, Thickness? padding = null)
    {
        var face = new Border
        {
            Background = on ? Brushes.White : new SolidColorBrush(TileFace), BorderBrush = on ? InkBrush : new SolidColorBrush(Color.FromRgb(0xC9, 0xAF, 0xCC)),
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(7), Padding = padding ?? new Thickness(6, 5, 7, 5),
            Margin = new Thickness(0, 0, 0, Ledge), Child = content,
        };
        var g = new Grid();
        g.Children.Add(new Border { Background = new SolidColorBrush(edge), CornerRadius = new CornerRadius(7), Margin = new Thickness(0, Ledge, 0, 0) });
        g.Children.Add(face);
        return Pressable(g, face, tag, name, on ? Ledge - 1 : 0, Ledge - 1);
    }

    // WPF PressTo (:896): the face rests at <rest>, goes down to <down> while pressed, whitens on hover.
    private static Button Pressable(Control content, Border face, string tag, string name, double rest, double down)
    {
        var restBrush = face.Background;
        var b = Bare(content, tag);
        var press = new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Easing = new QuadraticEaseOut() };
        face.Transitions = new Transitions { press };
        face.RenderTransform = TransformOperations.Parse($"translateY({rest}px)");
        b.PropertyChanged += (_, e) =>
        {
            if (e.Property != Button.IsPressedProperty && e.Property != InputElement.IsPointerOverProperty) return;
            press.Duration = TimeSpan.FromMilliseconds(Amount > 0 ? GentleVisuals.RiseMs(70) : 0);
            face.RenderTransform = TransformOperations.Parse($"translateY({(b.IsPressed && b.IsPointerOver ? down : rest)}px)");
            face.Background = b.IsPointerOver && b.IsEffectivelyEnabled ? Brushes.White : restBrush;
        };
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(b, name);
        return b;
    }

    /// <summary>WPF :905: the darker inset under the tiles with a small spaced-capitals label.</summary>
    public static Border Tray(string label, Control body, string tag)
    {
        var head = Caps(label, 13, MutedBrush, FontWeight.Medium, 0.16);
        head.Margin = new Thickness(0, 0, 0, 8);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0, 0, 0)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xA9, 0x93, 0xCF)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 9, 10, 10), Tag = tag,
            Child = new StackPanel { Children = { head, body } },
        };
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

    /// <summary>WPF :139: a line icon, here the Fluent one (IconGlyph).</summary>
    public static Control Icon(IconKind kind, double size, IBrush fg) =>
        new IconGlyph { Kind = kind, Size = size, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>WPF :183 <see cref="ToneIcon"/> as a Fluent icon ("ldn" is a lock, as IconMap's lockdown).</summary>
    public static IconKind ToneKind(DareTone tone) => ToneIcon(tone) switch
    {
        "picture" => IconKind.Image, "clock" => IconKind.Clock, "video" => IconKind.Video, "play" => IconKind.Play,
        "bubble" => IconKind.BubbleMultiple, "star" => IconKind.Star, "lock" or "ldn" => IconKind.LockClosed,
        "eye" => IconKind.Eye, "eyeoff" => IconKind.EyeOff, "cross" => IconKind.Dismiss, "versus" => IconKind.ArrowSwap,
        _ => IconKind.Mail,
    };

    // WPF :200-212: a friend's own hue, stable by name.
    private static readonly Color[] FriendHues =
    {
        Color.FromRgb(0x1F, 0x8A, 0x8A), Color.FromRgb(0xCC, 0x3D, 0x7A), Color.FromRgb(0xE0, 0x66, 0x3A),
        Color.FromRgb(0x2F, 0x86, 0xC9), Color.FromRgb(0x8A, 0x3F, 0x8F), Color.FromRgb(0x2C, 0x8A, 0x57),
    };

    public static Color FriendColor(string? name)
    {
        int h = 0;
        foreach (var ch in name ?? "") h = unchecked(h * 31 + ch);
        return FriendHues[(h & 0x7FFFFFFF) % FriendHues.Length];
    }

    private static string Initial(string? name)
    {
        var i = ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.Initials(name ?? "");
        return string.IsNullOrEmpty(i) ? "?" : i.Substring(0, 1);
    }

    /// <summary>WPF :221: a round initial disc in the friend's hue.</summary>
    private static Control InitialDisc(string? name, double size, Color hue)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Fill = new SolidColorBrush(hue) });
        g.Children.Add(Hand(Initial(name), size * 0.8, Brushes.White));
        return g;
    }

    /// <summary>WPF :474-500, read only: sentence lines on the slip's 44 px line grid, print words and fills.</summary>
    public static StackPanel Sentence(System.Collections.Generic.IReadOnlyList<DareLine> lines, string? friendName, double size = 23, double lineHeight = 44)
    {
        var host = new StackPanel { Tag = "dares-sentence" };
        foreach (var line in lines)
        {
            var row = new WrapPanel { ItemHeight = lineHeight };
            if (line.Mark != DareMark.None) row.Children.Add(Mark(line.Mark, friendName));
            foreach (var seg in line.Segs)
                if (seg.Kind == DareSegKind.Text) AddWords(row, seg.Text, size, PrintBrush);
                else row.Children.Add(seg.Kind == DareSegKind.Chip ? Fill(seg) : Blank(seg));
            host.Children.Add(row);
        }
        return host;
    }

    /// <summary>WPF :506: one line of words with a fill at its end (the debt card's "You owe ___").</summary>
    public static WrapPanel Line(string text, Control fill, double size, IBrush fg, double lineHeight)
    {
        var row = new WrapPanel { ItemHeight = lineHeight };
        AddWords(row, text, size, fg);
        row.Children.Add(fill);
        return row;
    }

    // WPF :280: words split at spaces, so a sentence wraps word by word.
    private static void AddWords(Panel row, string text, double size, IBrush fg)
    {
        var parts = text.Split(' ');
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0)
                row.Children.Add(new TextBlock { Text = parts[i], FontFamily = PrintFont, FontWeight = FontWeight.Medium, FontSize = size,
                    Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, i < parts.Length - 1 ? size * 0.24 : 0, 0) });
    }

    /// <summary>WPF :336-388, read only: a white handwritten box, its kind's colour on the border and the icon;
    /// the friend shows a round initial; a Lockdown is ink inverted. <paramref name="small"/> = the drawer strip's size, no shadow.</summary>
    public static Control Fill(DareSeg seg, bool small = false)
    {
        bool inverted = seg.Tone == DareTone.Lockdown, who = seg.Tone == DareTone.Who;
        var c = who ? FriendColor(seg.Text) : ToneColor(seg.Tone);
        IBrush line = inverted ? Brushes.White : new SolidColorBrush(c);
        double iconSize = small ? 17 : 24;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (who) sp.Children.Add(InitialDisc(seg.Text, iconSize, c));
        else
        {
            var icon = new Grid { VerticalAlignment = VerticalAlignment.Center };
            // WPF :346: Sparkles' star is filled gold under its outline.
            if (seg.Tone == DareTone.Sp) icon.Children.Add(new IconGlyph { Kind = IconKind.Star, Variant = IconVariant.Filled, Size = iconSize, Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD4, 0x5E)) });
            icon.Children.Add(Icon(ToneKind(seg.Tone), iconSize, line));
            sp.Children.Add(icon);
        }
        var words = Hand(seg.Text, small ? 20 : 29, inverted ? Brushes.White : InkBrush);
        words.Margin = new Thickness(small ? 4 : 6, 0, 0, small ? 1 : 2);
        words.VerticalAlignment = VerticalAlignment.Center;
        if (small) words.TextTrimming = TextTrimming.CharacterEllipsis;
        sp.Children.Add(words);
        var g = new Grid { Margin = small ? new Thickness(0) : new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Tag = "dares-chip:" + seg.Step };
        if (!small) g.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x22, 0, 0, 0)), CornerRadius = new CornerRadius(8), Margin = new Thickness(1, 2, -1, -2) });
        g.Children.Add(new Border
        {
            Child = sp, Background = inverted ? InkBrush : new SolidColorBrush(Color.FromRgb(0xFF, 0xFA, 0xF8)),
            // WPF :358: small sits on the drawer's dark violet, so an inverted fill keeps a lilac edge.
            BorderBrush = inverted ? (small ? new SolidColorBrush(Color.FromRgb(0x8F, 0x6F, 0xE0)) : InkBrush) : line,
            BorderThickness = new Thickness(seg.Tone == DareTone.When || small ? 1.5 : 2),
            CornerRadius = new CornerRadius(small ? 6 : 8), Padding = small ? new Thickness(4, 1, 7, 1) : new Thickness(7, 3, 10, 3),
        });
        if (seg.TipKey != null) ToolTip.SetTip(g, Loc.Get(seg.TipKey));
        AutomationProperties.SetName(g, seg.Text);
        return g;
    }

    /// <summary>WPF :413, read only: a blank not yet filled, a dashed underline with its small hint.</summary>
    private static Control Blank(DareSeg seg)
    {
        var b = new Border
        {
            Child = Caps(seg.Text, 14, PrintBrush, FontWeight.Medium, 0.12), MinWidth = 92, Height = 28, Padding = new Thickness(6, 0),
            Margin = new Thickness(4, 0), BorderThickness = new Thickness(0, 0, 0, 2), Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = PrintBrush, Tag = "dares-blank:" + seg.Step,
        };
        AutomationProperties.SetName(b, seg.Text);
        return b;
    }

    /// <summary>WPF :453: the round mark at the head of an outcome line: tick, cross, or an ink disc with an initial.</summary>
    private static Control Mark(DareMark m, string? themName)
    {
        var disc = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        disc.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Fill = new SolidColorBrush(m switch
            { DareMark.Yes => Color.FromRgb(0x1F, 0xA8, 0x77), DareMark.No => StampRed, _ => Ink }) });
        if (m is DareMark.Yes or DareMark.No)
            disc.Children.Add(Icon(m == DareMark.Yes ? IconKind.Checkmark : IconKind.Dismiss, 17, Brushes.White));
        else
        {
            var letter = m == DareMark.Me ? Loc.Get("dares_mark_me") : Initial(themName);
            var t = Hand(letter, letter.Length > 1 ? 15 : 20, Brushes.White);
            t.VerticalAlignment = VerticalAlignment.Center;
            disc.Children.Add(t);
        }
        AutomationProperties.SetName(disc, m == DareMark.Me ? Loc.Get("dares_mark_me") : m == DareMark.Them ? themName ?? "" : "");
        return disc;
    }

    /// <summary>WPF :690: a kind's 46 px disc with its white icon.</summary>
    public static Control Disc(DareTone tone)
    {
        var g = new Grid { Width = 46, Height = 46 };
        g.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Fill = new SolidColorBrush(ToneColor(tone)) });
        g.Children.Add(Icon(ToneKind(tone), 28, Brushes.White));
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

    // WPF :311-338: no chrome, the content is the look. An empty theme keeps Fluent's hover box and press scale off.
    private static readonly ControlTheme BareTheme = new(typeof(Button));

    private static Button Bare(Control content, string tag)
    {
        var b = new Button
        {
            Content = content, Tag = tag, Theme = BareTheme, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Padding = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            // WPF's ContentPresenter root hit-tests its whole box; Avalonia's only where something paints.
            Template = new FuncControlTemplate<Button>((btn, _) => new Border { Background = Brushes.Transparent, Child = new ContentPresenter { Content = btn.Content } }),
        };
        b.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsEffectivelyEnabledProperty) b.Opacity = b.IsEffectivelyEnabled ? 1 : 0.45; };
        return b;
    }

    /// <summary>WPF :577: the slip's head, the title in spaced capitals over a rule, its (?) and close cross.</summary>
    public static Control SlipHead(string title, string? helpKey, Action? onClose)
    {
        var dock = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        if (helpKey != null) right.Children.Add(Help(helpKey, PrintBrush));
        if (onClose != null)
        {
            var x = Bare(Icon(IconKind.Dismiss, 18, PrintBrush), "dares-close");
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
                Padding = new Thickness(30, 9, 30, 10), Margin = new Thickness(0, 0, 0, 6) }
            : new Border { Child = words, BorderBrush = onCard ? new SolidColorBrush(Color.FromRgb(0xC9, 0xAF, 0xCC)) : MutedBrush,
                BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), Padding = new Thickness(20, 8), Background = Brushes.Transparent };
        Button button;
        if (big)
        {
            var g = new Grid();
            g.Children.Add(new Border { Background = new SolidColorBrush(Color.FromRgb(0x17, 0x80, 0x5A)), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 6, 0, 0) });
            g.Children.Add(face);
            button = Bare(g, tag);
            // WPF :940-942,1146: the face presses 4 px onto its ledge (70 ms ease-out); a Transition on the face, no per-frame effect.
            var press = new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Easing = new QuadraticEaseOut() };
            face.Transitions = new Transitions { press };
            button.PropertyChanged += (_, e) =>
            {
                if (e.Property != Button.IsPressedProperty && e.Property != InputElement.IsPointerOverProperty) return;
                press.Duration = TimeSpan.FromMilliseconds(Amount > 0 ? GentleVisuals.RiseMs(70) : 0);
                face.RenderTransform = TransformOperations.Parse(button.IsPressed && button.IsPointerOver ? "translateY(4px)" : "translateY(0px)");
            };
        }
        else
        {
            button = Bare(face, tag);
            // WPF :976-978: the outline tints on hover.
            IBrush hover = onCard ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
            button.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsPointerOverProperty) face.Background = button.IsPointerOver ? hover : Brushes.Transparent; };
        }
        button.VerticalAlignment = VerticalAlignment.Top;
        button.HorizontalAlignment = HorizontalAlignment.Left;
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
