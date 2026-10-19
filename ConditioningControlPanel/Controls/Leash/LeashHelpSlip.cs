using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// A "?" slip (owner-approved "ink slips", 2026-10-17): a paper card that says what the other
/// player gets when the holder presses a button. A top band in the accent of the button or row
/// it explains, the title, a level stamp, "YOU [button] -> THEM [pictogram]", a caption and a
/// small limits line. Code-built; the pictograms are plain ink drawings on a 130x84 canvas of
/// the surface the other player actually sees, highlighted in the same accent.
/// </summary>
public sealed class LeashHelpSlip : Border
{
    internal static readonly Color PaperColor = Color.FromRgb(0xF3, 0xEE, 0xE4);
    internal static readonly Brush Paper = FriendsLook.Frozen(PaperColor);
    internal static readonly Brush Ink = FriendsLook.Frozen(Color.FromRgb(0x1E, 0x1B, 0x22));
    internal static readonly Brush Muted = FriendsLook.Frozen(Color.FromRgb(0x5A, 0x54, 0x62));
    internal static readonly Brush Faint = FriendsLook.Frozen(Color.FromRgb(0xB4, 0xAC, 0xA0));
    internal static readonly Brush PinkFill = FriendsLook.Frozen(Color.FromRgb(0xF2, 0xB6, 0xCF));
    private static readonly FontFamily Sans = new("Segoe UI");

    /// <summary>One accent: <see cref="Bright"/> is the colour the card draws the button or row
    /// icon in (band, fills); <see cref="Deep"/> is the same hue dark enough for text on paper.</summary>
    internal readonly record struct SlipAccent(Color Bright, Color Deep)
    {
        public Brush BrightBrush => FriendsLook.Frozen(Bright);
        public Brush DeepBrush => FriendsLook.Frozen(Deep);

        /// <summary>The bright colour washed into the paper, for filled shapes under ink lines.</summary>
        public Brush TintBrush => FriendsLook.Frozen(Mix(Bright, PaperColor, 0.42));
    }

    // Bright = the brush the holder card uses (LeashLook.Tone / SizeRow accent); Deep = 4.5:1 on paper.
    internal static readonly SlipAccent GoldAccent = new(FriendsLook.Gold, Color.FromRgb(0x7A, 0x53, 0x00));
    internal static readonly SlipAccent MintAccent = new(FriendsLook.Mint, Color.FromRgb(0x0E, 0x66, 0x50));
    internal static readonly SlipAccent RedAccent = new(FriendsLook.Red, Color.FromRgb(0xB0, 0x24, 0x3F));
    internal static readonly SlipAccent PinkAccent = new(FriendsLook.Pink, Color.FromRgb(0xA3, 0x17, 0x5E));
    internal static readonly SlipAccent LilacAccent = new(FriendsLook.Lilac, Color.FromRgb(0x5A, 0x3D, 0xB0));

    /// <summary>The accent of the button or row a slip explains, as the holder card paints it:
    /// Assign gold, Reward mint, Punish and every punish row red (the card draws all punish row
    /// icons in <c>FriendsLook.RedBrush</c>), Tug (the ghost button, lilac ink) lilac, Praise
    /// pink, Pardon lilac, Chaster credit mint, the sticker shelf gold (its first stickers).</summary>
    internal static SlipAccent AccentFor(string id) => id switch
    {
        "assign" => GoldAccent,
        "reward" or "credit" => MintAccent,
        "praise" => PinkAccent,
        "tug" or "letgo" or "pardon" => LilacAccent,
        "sticker" => GoldAccent,
        "punish" or "lines" or "pink" or "bubbles" or "detention" or "video" or "chaster" => RedAccent,
        _ => LilacAccent,
    };

    /// <summary>The shelf's sticker colours, in shelf order (LeashLook.StickerDisc).</summary>
    internal static readonly Color[] StickerColors = { FriendsLook.Gold, FriendsLook.Gold, FriendsLook.Lilac, FriendsLook.Pink, FriendsLook.Mint };

    public LeashHelpEntry Entry { get; }
    internal SlipAccent Accent { get; }
    private readonly TextBlock _caption;

    public LeashHelpSlip(LeashHelpEntry entry, string theirName)
    {
        Entry = entry;
        Accent = AccentFor(entry.Id);
        Width = 300;
        Background = Paper;
        CornerRadius = new CornerRadius(6);
        BorderBrush = FriendsLook.Frozen(Color.FromRgb(0xD8, 0xCF, 0xBF));
        BorderThickness = new Thickness(0, 0, 0, 1);
        Tag = "leash-slip:" + entry.Id;
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 6, Direction = 270, Opacity = 0.25, Color = Color.FromRgb(0x1E, 0x1B, 0x22) };

        var root = new StackPanel();
        root.Children.Add(Band(entry.Id));
        var sp = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        root.Children.Add(sp);

        // Title and stamp.
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock
        {
            Text = Loc.Get(entry.TitleKey),
            FontFamily = Sans,
            FontWeight = FontWeights.ExtraBold,
            FontSize = 18,
            Foreground = Ink,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var stamp = new Border
        {
            BorderBrush = Accent.DeepBrush,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(-4),
            Tag = "leash-slip-stamp",
            Child = new TextBlock
            {
                Text = LeashHelpCatalog.StampText(entry.Stamp, Loc.Get),
                FontFamily = FriendsLook.Mono,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = Accent.DeepBrush,
            },
        };
        Grid.SetColumn(stamp, 1);
        head.Children.Add(stamp);
        sp.Children.Add(head);

        // YOU [button] -> THEM [pictogram].
        var pic = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var you = Side(Loc.Get("leash_slip_you"), new Border
        {
            MinWidth = 60,
            Height = 30,
            Padding = new Thickness(7, 0, 7, 0),
            BorderBrush = Ink,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = LeashHelpCatalog.YouLabel(entry, Loc.Get),
                FontFamily = Sans,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = Ink,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        you.VerticalAlignment = VerticalAlignment.Center;
        pic.Children.Add(you);
        var arrow = new Canvas { Width = 24, Height = 16, Margin = new Thickness(4, 14, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        arrow.Children.Add(P("M2 8 H19 M13 3 L20 8 L13 13", null, Ink, 2.2));
        pic.Children.Add(arrow);
        pic.Children.Add(Side(Loc.Get("leash_slip_them"), Pictogram(entry.Pictogram, Accent)));
        sp.Children.Add(pic);

        // Caption and limits.
        var caption = Loc.Get(entry.CaptionKey);
        if (entry.CaptionNamesThem) caption = string.Format(CultureInfo.InvariantCulture, caption, theirName);
        _caption = new TextBlock
        {
            Text = caption,
            FontFamily = Sans,
            FontSize = 13.5,
            LineHeight = 19,
            Foreground = Ink,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            Tag = "leash-slip-caption",
        };
        sp.Children.Add(_caption);
        sp.Children.Add(new TextBlock
        {
            Text = Loc.Get(entry.LimitsKey),
            FontFamily = FriendsLook.Mono,
            FontSize = 11.5,
            Foreground = Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
            Tag = "leash-slip-limits",
        });
        Child = root;
    }

    internal string CaptionText => _caption.Text;

    /// <summary>The coloured band across the top: the accent, or the sticker colours in a row.</summary>
    private FrameworkElement Band(string id)
    {
        var band = new Border { Height = 6, CornerRadius = new CornerRadius(6, 6, 0, 0), Tag = "leash-slip-band", Background = Accent.BrightBrush };
        if (id == LeashHelpCatalog.StickerId)
        {
            var stops = new GradientStopCollection();
            for (int i = 0; i < StickerColors.Length; i++)
            {
                // Hard stops: five flat blocks, not a gradient.
                stops.Add(new GradientStop(StickerColors[i], (double)i / StickerColors.Length));
                stops.Add(new GradientStop(StickerColors[i], (double)(i + 1) / StickerColors.Length));
            }
            var b = new LinearGradientBrush(stops, 0);
            b.Freeze();
            band.Background = b;
        }
        return band;
    }

    private static StackPanel Side(string label, FrameworkElement body)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        s.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = FriendsLook.Mono,
            FontWeight = FontWeights.Bold,
            FontSize = 10,
            Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        });
        body.HorizontalAlignment = HorizontalAlignment.Center;
        s.Children.Add(body);
        return s;
    }

    internal static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R * t + b.R * (1 - t)),
        (byte)Math.Round(a.G * t + b.G * (1 - t)),
        (byte)Math.Round(a.B * t + b.B * (1 - t)));

    /// <summary>WCAG contrast ratio between two colours.</summary>
    internal static double Contrast(Color a, Color b)
    {
        static double Lin(byte c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        static double L(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        double la = L(a), lb = L(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // ---- pictograms: what the other player sees, 130x84 ---------------------------------

    /// <summary>The pictogram for <paramref name="id"/> in its own accent.</summary>
    internal static Canvas Pictogram(string id) => Pictogram(id, AccentFor(id));

    /// <summary>The pictogram for <paramref name="id"/>, highlighted in <paramref name="accent"/>;
    /// an unknown id draws an empty frame.</summary>
    internal static Canvas Pictogram(string id, SlipAccent accent)
    {
        var c = new Canvas { Width = 130, Height = 84, Tag = "leash-slip-pic:" + id, ClipToBounds = false };
        void Add(UIElement e) => c.Children.Add(e);
        Brush a = accent.BrightBrush, d = accent.DeepBrush, t = accent.TintBrush;
        switch (id)
        {
            case "assign":
                // A task card on their side: band, the task ticked off, a Done button.
                Add(R(12, 3, 106, 78, 7, Paper, Ink, 2.4));
                Add(R(13.2, 4.2, 103.6, 18, 6, a, null, 0));
                Add(P("M13.2 16 H116.8 V22.2 H13.2 Z", a, null, 0));
                Add(C(28, 13, 6, Paper, Ink, 2));
                Add(P("M28 9.5 V13 L30.5 14.5", null, Ink, 1.8));
                Add(R(40, 10, 50, 6, 3, Ink, null, 0));
                Add(R(22, 31, 14, 14, 3, t, Ink, 2.2));
                Add(P("M25 38 L28.5 41.5 L34 33.5", null, d, 2.6));
                Add(R(42, 35, 52, 6, 3, Ink, null, 0));
                Add(R(22, 52, 14, 14, 3, Paper, Ink, 2.2));
                Add(R(42, 56, 34, 6, 3, Faint, null, 0));
                Add(R(82, 60, 30, 16, 5, d, null, 0));
                Add(P("M90 68 L94 72 L104 63.5", null, Paper, 2.4));
                break;
            case "tug":
                // Their window wobbles; the chain jingles.
                var g = new Canvas { Width = 130, Height = 84, RenderTransform = new RotateTransform(-5, 65, 40) };
                g.Children.Add(R(22, 6, 86, 60, 7, Paper, Ink, 2.4));
                g.Children.Add(P("M23.2 18 V12.5 a5.3 5.3 0 0 1 5.3 -5.3 H101.5 a5.3 5.3 0 0 1 5.3 5.3 V18 Z", Ink, null, 0));
                g.Children.Add(R(32, 28, 66, 28, 5, t, Ink, 2));
                g.Children.Add(R(40, 36, 44, 5, 2.5, Ink, null, 0));
                g.Children.Add(R(40, 45, 30, 5, 2.5, Muted, null, 0));
                Add(g);
                Add(P("M6 22 Q12 32 6 42 M13 26 Q17 32 13 38", null, d, 2.8));
                Add(P("M124 30 Q118 40 124 50 M117 34 Q113 40 117 46", null, d, 2.8));
                Add(E(46, 76, 8, 5, null, d, 2.8));
                Add(R(55, 74, 14, 4, 2, d, null, 0));
                Add(E(78, 76, 8, 5, null, d, 2.8));
                break;
            case "letgo":
                // The chain breaks; panic switches on, the lock opens.
                Add(E(18, 20, 11, 7, null, Ink, 2.8));
                Add(E(36, 20, 11, 7, null, Ink, 2.8));
                Add(P("M54 10 L59 17 M62 7 V16 M70 10 L65 17 M54 30 L59 23 M70 30 L65 23", null, d, 2.4));
                Add(E(88, 20, 11, 7, null, Ink, 2.8));
                Add(E(106, 20, 11, 7, null, Ink, 2.8));
                Add(R(14, 48, 48, 26, 13, a, Ink, 2.4));
                Add(C(48, 61, 9, Paper, Ink, 2.2));
                Add(R(84, 54, 30, 24, 5, t, Ink, 2.4));
                Add(P("M90 54 V44 a8 8 0 0 1 16 0 V46", null, Ink, 2.8));
                Add(C(99, 64, 3, Ink, null, 0));
                break;
            case "lines":
                // A gate card over their panel: a lock-card phrase, the box they type it in, 3 of 5.
                Add(R(4, 3, 122, 78, 7, Faint, Ink, 2.4));
                Add(R(16, 9, 98, 66, 7, Paper, Ink, 2.4));
                Add(R(16.8, 9.8, 96.4, 12, 6, t, null, 0));
                Add(P("M16.8 16 H113.2 V22 H16.8 Z", t, null, 0));
                Add(R(24, 15, 11, 9, 2, d, null, 0));
                Add(P("M26 15 V12.5 a3.5 3.5 0 0 1 7 0 V15", null, d, 2));
                Add(R(42, 16, 54, 5, 2.5, Ink, null, 0));
                Add(R(24, 30, 82, 16, 4, Paper, Ink, 2));
                Add(R(29, 36, 42, 5, 2.5, Ink, null, 0));
                Add(P("M75 34 V43", null, d, 2.2));
                Add(C(28, 60, 4.5, d, null, 0));
                Add(C(42, 60, 4.5, d, null, 0));
                Add(C(56, 60, 4.5, d, null, 0));
                Add(C(70, 60, 4.5, Paper, Ink, 2));
                Add(C(84, 60, 4.5, Paper, Ink, 2));
                Add(T(92, 65, 12, Ink, "3/5"));
                break;
            case "pink":
                // Their screen under a pink filter: their own flashes, a session timer.
                Add(R(4, 3, 122, 78, 7, PinkFill, Ink, 2.4));
                Add(R(14, 14, 40, 30, 3, Paper, Ink, 2.2));
                Add(P("M17 41 L28 27 L36 36 L42 30 L51 41 Z", d, null, 0));
                Add(C(44, 21, 3.5, d, null, 0));
                Add(R(66, 42, 46, 30, 3, Paper, Ink, 2.2));
                Add(P("M69 69 L80 55 L88 64 L94 58 L109 69 Z", d, null, 0));
                Add(R(70, 11, 46, 18, 9, Ink, null, 0));
                Add(T(77, 24.5, 11, Paper, "15:00"));
                Add(R(14, 56, 38, 6, 3, d, null, 0));
                break;
            case "bubbles":
                // Bubbles float over their screen with the pop counter.
                Add(R(4, 3, 122, 78, 7, Paper, Ink, 2.4));
                Bubble(c, 32, 30, 14, t, d);
                Bubble(c, 68, 46, 17, t, d);
                Bubble(c, 102, 24, 11, t, d);
                Bubble(c, 104, 60, 8, t, d);
                Add(R(10, 62, 50, 15, 7.5, Ink, null, 0));
                Add(T(16, 73.5, 10.5, Paper, "12/100"));
                break;
            case "detention":
                // Their own flashes, subliminals and bubbles, against the clock.
                Add(R(4, 3, 122, 78, 7, Paper, Ink, 2.4));
                Add(R(12, 11, 34, 24, 3, Ink, null, 0));
                Add(P("M15 32 L24 22 L30 28 L34 24 L43 32 Z", Faint, null, 0));
                Add(R(12, 42, 44, 10, 2, d, null, 0));
                Add(R(16, 45.5, 30, 3, 1.5, Paper, null, 0));
                Bubble(c, 26, 66, 8, t, d);
                Bubble(c, 46, 64, 5, t, d);
                Add(C(92, 42, 25, Paper, Ink, 2.6));
                Add(P("M92 17 A25 25 0 0 1 115.8 49.7", null, d, 5));
                Add(P("M92 27 V42 L102 48", null, Ink, 3));
                Add(C(92, 42, 2.8, Ink, null, 0));
                break;
            case "video":
                // A video over most of the screen: no pause, no close, a running bar.
                Add(R(4, 3, 122, 78, 7, Ink, Ink, 2.4));
                Add(C(65, 36, 17, null, Paper, 2.4));
                Add(P("M59 27 L75 36 L59 45 Z", Paper, null, 0));
                Add(R(12, 66, 106, 6, 3, Muted, null, 0));
                Add(R(12, 66, 64, 6, 3, a, null, 0));
                Add(C(76, 69, 4.5, Paper, null, 0));
                Add(P("M104 10 L114 20 M114 10 L104 20", null, Muted, 2.2));
                Add(P("M100 24 L118 6", null, a, 3));
                break;
            case "chaster":
            case "credit":
                // Their Chaster padlock: time goes on (+) or comes off (-).
                bool credit = id == "credit";
                Add(credit
                    ? P("M24 40 V24 a13 13 0 0 1 26 0 V27", null, Ink, 3.6)
                    : P("M24 40 V27 a13 13 0 0 1 26 0 V40", null, Ink, 3.6));
                Add(R(14, 38, 46, 40, 7, t, Ink, 2.6));
                Add(C(37, 54, 5, Ink, null, 0));
                Add(R(35, 55, 4, 12, 2, Ink, null, 0));
                Add(R(68, 22, 58, 32, 8, d, null, 0));
                Add(T(80, 45, 19, Paper, credit ? "-30" : "+30"));
                Add(T(86, 70, 12, Ink, "min"));
                break;
            case "sticker":
                // A new sticker lands on their shelf, in the shelf's own colours.
                Add(P("M6 72 H124", null, Ink, 3));
                Add(P("M22 72 V80 M108 72 V80", null, Ink, 2.6));
                Sticker(c, 22, 54, 15, StickerColors[0], P("M22 44 L25 51 L32.5 51.5 L27 56.5 L28.5 64 L22 60 L15.5 64 L17 56.5 L11.5 51.5 L19 51 Z", Ink, null, 0));
                Sticker(c, 56, 54, 15, StickerColors[3], P("M56 63 C50 59 47 56 47 52.5 A4.5 4.5 0 0 1 56 50.5 A4.5 4.5 0 0 1 65 52.5 C65 56 62 59 56 63 Z", Ink, null, 0));
                Sticker(c, 90, 54, 15, StickerColors[4], T(81, 57.5, 10, Ink, "WOW"));
                Sticker(c, 112, 24, 13, StickerColors[2], T(104, 27.5, 9, Ink, "PET"));
                Add(P("M92 14 L96 20 M88 26 L94 27 M100 6 V13", null, accent.DeepBrush, 2.4));
                break;
            case "praise":
                // A sound and a note on their screen.
                Add(R(4, 3, 122, 78, 7, Paper, Ink, 2.4));
                Add(P("M12 20 H17 L25 13 V35 L17 28 H12 Z", Ink, null, 0));
                Add(P("M30 17 Q35 24 30 31 M36 12 Q45 24 36 36", null, d, 2.6));
                Add(R(34, 44, 84, 28, 7, Ink, null, 0));
                Add(P("M51 66 C44.5 61.5 42 58 42 54.5 A4.6 4.6 0 0 1 51 52.5 A4.6 4.6 0 0 1 60 54.5 C60 58 57.5 61.5 51 66 Z", a, null, 0));
                Add(R(66, 52, 44, 5, 2.5, Paper, null, 0));
                Add(R(66, 61, 28, 5, 2.5, Faint, null, 0));
                break;
            case "pardon":
                // A ticket they spend on the gate; they can hold three.
                Add(P("M8 14 H122 V34 a8 8 0 0 0 0 16 V70 H8 V50 a8 8 0 0 0 0 -16 Z", t, Ink, 2.6));
                var perf = P("M94 18 V66", null, Ink, 2);
                perf.StrokeDashArray = new DoubleCollection { 1.5, 2.5 };
                Add(perf);
                Add(C(28, 32, 6.5, Paper, d, 2.8));
                Add(C(28, 54, 6.5, Paper, d, 2.8));
                Add(P("M33.5 35.5 L62 54 M33.5 50.5 L62 32", null, d, 2.8));
                Add(R(66, 36, 20, 5, 2.5, Ink, null, 0));
                Add(R(66, 46, 14, 5, 2.5, Muted, null, 0));
                Add(C(108, 28, 4.5, d, null, 0));
                Add(C(108, 42, 4.5, d, null, 0));
                Add(C(108, 56, 4.5, Paper, Ink, 2));
                break;
            default:
                Add(R(4, 3, 122, 78, 7, null, Ink, 2.4));
                break;
        }
        return c;
    }

    private static void Bubble(Canvas c, double cx, double cy, double r, Brush fill, Brush stroke)
    {
        c.Children.Add(C(cx, cy, r, fill, stroke, 2.6));
        c.Children.Add(C(cx - r * 0.38, cy - r * 0.38, Math.Max(1.6, r * 0.22), Paper, null, 0));
    }

    private static void Sticker(Canvas c, double cx, double cy, double r, Color color, UIElement face)
    {
        c.Children.Add(C(cx, cy, r, FriendsLook.Frozen(color), Ink, 2));
        c.Children.Add(face);
    }

    private static Rectangle R(double x, double y, double w, double h, double rx, Brush? fill, Brush? stroke, double sw)
    {
        var r = new Rectangle { Width = w, Height = h, RadiusX = rx, RadiusY = rx, Fill = fill, Stroke = stroke, StrokeThickness = sw };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    private static Ellipse C(double cx, double cy, double r, Brush? fill, Brush? stroke, double sw) => E(cx, cy, r, r, fill, stroke, sw);

    private static Ellipse E(double cx, double cy, double rx, double ry, Brush? fill, Brush? stroke, double sw)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill, Stroke = stroke, StrokeThickness = sw };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e, cy - ry);
        return e;
    }

    private static Path P(string d, Brush? fill, Brush? stroke, double sw) => new()
    {
        Data = Geometry.Parse(d),
        Fill = fill,
        Stroke = stroke,
        StrokeThickness = sw,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeDashCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    /// <summary>SVG text at baseline (x, y): the TextBlock's top sits about one cap height up.</summary>
    private static TextBlock T(double x, double y, double size, Brush fg, string text)
    {
        var t = new TextBlock { Text = text, FontFamily = FriendsLook.Mono, FontWeight = FontWeights.Bold, FontSize = size, Foreground = fg };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y - size * 0.95);
        return t;
    }
}

/// <summary>
/// The small "?" that opens a <see cref="LeashHelpSlip"/>. Hover opens, a click pins (and a second
/// click unpins), the slip closes when the pointer leaves an unpinned slip or on any press
/// elsewhere. Never bound to Escape (the panic key). The popup is StaysOpen with no capture, and
/// drops its topmost flag on open (AGENTS.md popup rules). The "?" handles its own click, so the
/// command next to it never fires.
/// </summary>
public static class LeashHelpButton
{
    public static Button Create(LeashHelpEntry entry, Func<string> theirName, double size = 16)
        => Create(() => new LeashHelpSlip(entry, theirName()), "leash-slip-help:" + entry.Id, size);

    /// <summary>The same "?" for any paper slip (the online media meter's, 2026-10-19):
    /// <paramref name="slip"/> builds the card each time it opens.</summary>
    public static Button Create(Func<UIElement> slip, string tag, double size = 16)
    {
        var t = new TextBlock
        {
            Text = "?",
            FontFamily = FriendsLook.Display,
            FontWeight = FontWeights.Bold,
            FontSize = size * 0.62,
            Foreground = FriendsLook.LilacBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Opaque, so a "?" that sits on a button's corner reads as a badge, not a hole.
        var b = FriendsLook.Pill(t, FriendsLook.Frozen(FriendsLook.Rgb(0x24, 0x18, 0x38)), FriendsLook.LilacBrush,
            FriendsLook.Frozen(Color.FromArgb(0x66, 0xB9, 0x9C, 0xFF)), size / 2, new Thickness(0));
        b.Width = size;
        b.Height = size;
        b.Focusable = false;
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Tag = tag;
        var ctl = new Controller(b, slip);
        b.Click += (_, e) => { e.Handled = true; ctl.TogglePin(); };
        return b;
    }

    /// <summary>A sheet row's icon that is its own "?" (owner, 2026-10-17): the icon with a
    /// small "?" badge on its top-right corner. The badge hangs outside the icon's box, so the
    /// row's name keeps every pixel it had. Hover opens the slip, a click pins it.</summary>
    public static Button Badged(FrameworkElement face, LeashHelpEntry entry, Func<string> theirName, double badge = 11)
    {
        var cell = new Grid { Background = Brushes.Transparent };
        cell.Children.Add(face);
        cell.Children.Add(new Border
        {
            Width = badge,
            Height = badge,
            CornerRadius = new CornerRadius(badge / 2),
            Background = FriendsLook.Frozen(FriendsLook.Rgb(0x24, 0x18, 0x38)),
            BorderBrush = FriendsLook.Frozen(Color.FromArgb(0xAA, 0xB9, 0x9C, 0xFF)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -badge * 0.6, -badge * 0.3, 0),
            IsHitTestVisible = false,
            Tag = "leash-slip-badge",
            Child = new TextBlock
            {
                Text = "?",
                FontFamily = FriendsLook.Display,
                FontWeight = FontWeights.Bold,
                FontSize = badge * 0.72,
                Foreground = FriendsLook.LilacBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -1, 0, 0),
            },
        });
        var template = new ControlTemplate(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
        template.Seal();
        var b = new Button
        {
            Content = cell,
            Template = template,
            Focusable = false,
            Cursor = Cursors.Help,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "leash-slip-help:" + entry.Id,
        };
        var ctl = new Controller(b, () => new LeashHelpSlip(entry, theirName()));
        b.Click += (_, e) => { e.Handled = true; ctl.TogglePin(); };
        return b;
    }

    /// <summary>The slip as hover content for something that cannot host a button (the Let go
    /// menu row): a ToolTip with the paper card, no chrome.</summary>
    public static ToolTip AsToolTip(LeashHelpEntry entry, string theirName) => new()
    {
        Content = new LeashHelpSlip(entry, theirName),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0, 0, 8, 10),
        HasDropShadow = false,
        Placement = PlacementMode.Right,
    };

    private sealed class Controller
    {
        private readonly Button _b;
        private readonly Func<UIElement> _slip;
        private Popup? _popup;
        private bool _pinned;
        private UIElement? _root;
        private readonly DispatcherTimer _leave;

        public Controller(Button b, Func<UIElement> slip)
        {
            _b = b;
            _slip = slip;
            _leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _leave.Tick += (_, _) => { _leave.Stop(); if (!_pinned && !Over()) Close(); };
            b.MouseEnter += (_, _) => { _leave.Stop(); Open(); };
            b.MouseLeave += (_, _) => _leave.Start();
            b.IsVisibleChanged += (_, e) => { if (e.NewValue is false) Close(); };
            b.Unloaded += (_, _) => Close();
        }

        private bool Over() => _b.IsMouseOver || _popup?.Child is UIElement c && c.IsMouseOver;

        public void TogglePin()
        {
            if (_pinned) { _pinned = false; Close(); return; }
            _pinned = true;
            Open();
        }

        private void Open()
        {
            if (_popup is { IsOpen: true }) return;
            try
            {
                var slip = _slip();
                var holder = new Border { Padding = new Thickness(0, 4, 8, 12), Background = Brushes.Transparent, Child = slip };
                holder.MouseEnter += (_, _) => _leave.Stop();
                holder.MouseLeave += (_, _) => _leave.Start();
                holder.PreviewMouseDown += (_, e) => { _pinned = true; };
                _popup = new Popup
                {
                    Child = holder,
                    AllowsTransparency = true,
                    StaysOpen = true,
                    Placement = PlacementMode.Bottom,
                    PlacementTarget = _b,
                    PopupAnimation = PopupAnimation.None,
                    Focusable = false,
                };
                _popup.Opened += (_, _) => { DropTopmost(); HookRoot(); };
                _popup.Closed += (_, _) => UnhookRoot();
                _popup.IsOpen = true;
            }
            catch (Exception ex) { App.Logger?.Debug("[Leash] help slip open failed: {E}", ex.Message); }
        }

        private void Close()
        {
            _leave.Stop();
            _pinned = false;
            if (_popup != null) _popup.IsOpen = false;
            _popup = null;
        }

        /// <summary>A press anywhere else in the window that hosts the "?" folds the slip.</summary>
        private void HookRoot()
        {
            UnhookRoot();
            _root = PresentationSource.FromVisual(_b)?.RootVisual as UIElement;
            if (_root != null) _root.PreviewMouseDown += OnRootDown;
        }

        private void UnhookRoot()
        {
            if (_root != null) _root.PreviewMouseDown -= OnRootDown;
            _root = null;
        }

        private void OnRootDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && IsOn(d)) return;
            Close();
        }

        private bool IsOn(DependencyObject d)
        {
            for (var cur = d; cur != null; cur = cur is Visual || cur is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
                if (ReferenceEquals(cur, _b)) return true;
            return false;
        }

        private void DropTopmost()
        {
            try
            {
                if (_popup?.Child is { } c && PresentationSource.FromVisual(c) is HwndSource src && src.Handle != IntPtr.Zero)
                    SetWindowPos(src.Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
            catch (Exception ex) { App.Logger?.Debug("[Leash] help slip topmost drop failed: {E}", ex.Message); }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
