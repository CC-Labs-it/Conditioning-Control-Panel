// PORTED from WPF ConditioningControlPanel/Controls/Leash/LeashHelpSlip.cs (main 325115b21, the
// "ink slip" card and its "?" button). Catalog and words are Core (LeashHelpCatalog).
// Flat on this head (P82): the WPF DropShadowEffect is a BoxShadow on the card. The popup sits on the
// window's overlay layer where the platform allows it and is never Topmost (WPF DropTopmost).
// ponytail: LeashHelpButton.Badged (row icon as its own "?") and AsToolTip (Let go row) land with the
// holder card unit (sync7-leash-c, rows 179/180), which is also the first host of these slips here.
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Serilog;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

/// <summary>
/// A "?" slip: a paper card that says what the other player gets when the holder presses a button.
/// A top band in the accent of the button or row it explains, the title, a level stamp,
/// "YOU [button] -> THEM [pictogram]", a caption and a small limits line. The pictograms are ink
/// drawings on a 130x84 canvas of the surface the other player actually sees.
/// </summary>
public sealed class LeashHelpSlip : Border
{
    internal static readonly Color PaperColor = Color.FromRgb(0xF3, 0xEE, 0xE4);
    internal static readonly IBrush Paper = new ImmutableSolidColorBrush(PaperColor);
    internal static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.FromRgb(0x1E, 0x1B, 0x22));
    internal static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.FromRgb(0x5A, 0x54, 0x62));
    internal static readonly IBrush Faint = new ImmutableSolidColorBrush(Color.FromRgb(0xB4, 0xAC, 0xA0));
    internal static readonly IBrush PinkFill = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xB6, 0xCF));
    private static readonly FontFamily Sans = new("Segoe UI");

    // WPF FriendsLook colours (FriendsDrawer carries them as brushes).
    internal static readonly Color Gold = Color.FromRgb(0xFF, 0xCF, 0x6B), Mint = Color.FromRgb(0x5F, 0xFF, 0xD0),
        Red = Color.FromRgb(0xFF, 0x5F, 0x7A), Pink = Color.FromRgb(0xFF, 0x5F, 0xB4), Lilac = Color.FromRgb(0xB9, 0x9C, 0xFF);

    /// <summary>One accent: Bright is the colour the card draws the button or row icon in (band, fills);
    /// Deep is the same hue dark enough for text on paper.</summary>
    internal readonly record struct SlipAccent(Color Bright, Color Deep)
    {
        public IBrush BrightBrush => new ImmutableSolidColorBrush(Bright);
        public IBrush DeepBrush => new ImmutableSolidColorBrush(Deep);
        /// <summary>The bright colour washed into the paper, for filled shapes under ink lines.</summary>
        public IBrush TintBrush => new ImmutableSolidColorBrush(Mix(Bright, PaperColor, 0.42));
    }

    internal static readonly SlipAccent GoldAccent = new(Gold, Color.FromRgb(0x7A, 0x53, 0x00));
    internal static readonly SlipAccent MintAccent = new(Mint, Color.FromRgb(0x0E, 0x66, 0x50));
    internal static readonly SlipAccent RedAccent = new(Red, Color.FromRgb(0xB0, 0x24, 0x3F));
    internal static readonly SlipAccent PinkAccent = new(Pink, Color.FromRgb(0xA3, 0x17, 0x5E));
    internal static readonly SlipAccent LilacAccent = new(Lilac, Color.FromRgb(0x5A, 0x3D, 0xB0));

    /// <summary>The accent of the button or row a slip explains, as the holder card paints it.</summary>
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

    /// <summary>The shelf's sticker colours, in shelf order.</summary>
    internal static readonly Color[] StickerColors = { Gold, Gold, Lilac, Pink, Mint };

    public LeashHelpEntry Entry { get; }
    internal SlipAccent Accent { get; }
    private readonly TextBlock _caption;

    internal LeashHelpSlip() : this(LeashHelpCatalog.Get("tug")!, "Vex") { }   // --render-view sample

    public LeashHelpSlip(LeashHelpEntry entry, string theirName)
    {
        Entry = entry;
        Accent = AccentFor(entry.Id);
        Width = 300;
        Background = Paper;
        CornerRadius = new CornerRadius(6);
        BorderBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xD8, 0xCF, 0xBF));
        BorderThickness = new Thickness(0, 0, 0, 1);
        Tag = "leash-slip:" + entry.Id;
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 18, Color = Color.FromArgb(0x40, 0x1E, 0x1B, 0x22) });

        var root = new StackPanel();
        root.Children.Add(Band(entry.Id));
        var sp = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        root.Children.Add(sp);

        // Title and stamp.
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(new TextBlock
        {
            Text = Loc.Get(entry.TitleKey), FontFamily = Sans, FontWeight = FontWeight.ExtraBold, FontSize = 18,
            Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var stamp = new Border
        {
            BorderBrush = Accent.DeepBrush, BorderThickness = new Thickness(2), Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(-4), Tag = "leash-slip-stamp",
            Child = new TextBlock
            {
                Text = LeashHelpCatalog.StampText(entry.Stamp, Loc.Get), FontFamily = F.Mono, FontWeight = FontWeight.Bold,
                FontSize = 10, Foreground = Accent.DeepBrush,
            },
        };
        Grid.SetColumn(stamp, 1);
        head.Children.Add(stamp);
        sp.Children.Add(head);

        // YOU [button] -> THEM [pictogram].
        var pic = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var you = Side(Loc.Get("leash_slip_you"), new Border
        {
            MinWidth = 60, Height = 30, Padding = new Thickness(7, 0, 7, 0), BorderBrush = Ink, BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = LeashHelpCatalog.YouLabel(entry, Loc.Get), FontFamily = Sans, FontWeight = FontWeight.SemiBold, FontSize = 12,
                Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
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
            Text = caption, FontFamily = Sans, FontSize = 13.5, LineHeight = 19, Foreground = Ink, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0), Tag = "leash-slip-caption",
        };
        sp.Children.Add(_caption);
        sp.Children.Add(new TextBlock
        {
            Text = Loc.Get(entry.LimitsKey), FontFamily = F.Mono, FontSize = 11.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0), Tag = "leash-slip-limits",
        });
        Child = root;
    }

    internal string? CaptionText => _caption.Text;

    /// <summary>The coloured band across the top: the accent, or the sticker colours in a row.</summary>
    private Border Band(string id)
    {
        var band = new Border { Height = 6, CornerRadius = new CornerRadius(6, 6, 0, 0), Tag = "leash-slip-band", Background = Accent.BrightBrush };
        if (id == LeashHelpCatalog.StickerId)
        {
            var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            for (int i = 0; i < StickerColors.Length; i++)
            {
                // Hard stops: five flat blocks, not a gradient.
                b.GradientStops.Add(new GradientStop(StickerColors[i], (double)i / StickerColors.Length));
                b.GradientStops.Add(new GradientStop(StickerColors[i], (double)(i + 1) / StickerColors.Length));
            }
            band.Background = b;
        }
        return band;
    }

    private static StackPanel Side(string label, Control body)
    {
        var s = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        s.Children.Add(new TextBlock
        {
            Text = label, FontFamily = F.Mono, FontWeight = FontWeight.Bold, FontSize = 10, Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4),
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
        void Add(Control e) => c.Children.Add(e);
        IBrush a = accent.BrightBrush, d = accent.DeepBrush, t = accent.TintBrush;
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
                // Their window wobbles; the chain jingles. WPF's RotateTransform(-5, 65, 40) turns about
                // (65,40) from the top-left origin; Avalonia's default origin is the centre, so pin it.
                var g = new Canvas
                {
                    Width = 130, Height = 84, RenderTransformOrigin = RelativePoint.TopLeft,
                    RenderTransform = new RotateTransform { Angle = -5, CenterX = 65, CenterY = 40 },
                };
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
                Add(P("M92 14 L96 20 M88 26 L94 27 M100 6 V13", null, d, 2.4));
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
                perf.StrokeDashArray = new AvaloniaList<double> { 1.5, 2.5 };
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

    private static void Bubble(Canvas c, double cx, double cy, double r, IBrush fill, IBrush stroke)
    {
        c.Children.Add(C(cx, cy, r, fill, stroke, 2.6));
        c.Children.Add(C(cx - r * 0.38, cy - r * 0.38, Math.Max(1.6, r * 0.22), Paper, null, 0));
    }

    private static void Sticker(Canvas c, double cx, double cy, double r, Color color, Control face)
    {
        c.Children.Add(C(cx, cy, r, new ImmutableSolidColorBrush(color), Ink, 2));
        c.Children.Add(face);
    }

    private static Rectangle R(double x, double y, double w, double h, double rx, IBrush? fill, IBrush? stroke, double sw)
    {
        var r = new Rectangle { Width = w, Height = h, RadiusX = rx, RadiusY = rx, Fill = fill, Stroke = stroke, StrokeThickness = sw };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

    private static Ellipse C(double cx, double cy, double r, IBrush? fill, IBrush? stroke, double sw) => E(cx, cy, r, r, fill, stroke, sw);

    private static Ellipse E(double cx, double cy, double rx, double ry, IBrush? fill, IBrush? stroke, double sw)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill, Stroke = stroke, StrokeThickness = sw };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e, cy - ry);
        return e;
    }

    private static Path P(string d, IBrush? fill, IBrush? stroke, double sw) => new()
    {
        Data = Geometry.Parse(d), Fill = fill, Stroke = stroke, StrokeThickness = sw,
        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
    };

    /// <summary>SVG text at baseline (x, y): the TextBlock's top sits about one cap height up.</summary>
    private static TextBlock T(double x, double y, double size, IBrush fg, string text)
    {
        var t = new TextBlock { Text = text, FontFamily = F.Mono, FontWeight = FontWeight.Bold, FontSize = size, Foreground = fg };
        Canvas.SetLeft(t, x);
        Canvas.SetTop(t, y - size * 0.95);
        return t;
    }
}

/// <summary>
/// The small "?" that opens a <see cref="LeashHelpSlip"/> (or any paper slip). Hover opens, a click
/// pins (a second click unpins), the slip closes when the pointer leaves an unpinned slip or on any
/// press elsewhere in the window. Never bound to Escape (the panic key). The "?" handles its own
/// click, so the command next to it never fires.
/// </summary>
public static class LeashHelpButton
{
    private static readonly IBrush Badge = new ImmutableSolidColorBrush(Color.FromRgb(0x24, 0x18, 0x38));
    private static readonly IBrush BadgeLine = new ImmutableSolidColorBrush(Color.FromArgb(0x66, 0xB9, 0x9C, 0xFF));

    public static Button Create(LeashHelpEntry entry, Func<string> theirName, double size = 16)
        => Create(() => new LeashHelpSlip(entry, theirName()), "leash-slip-help:" + entry.Id, size);

    /// <summary>The same "?" for any paper slip: <paramref name="slip"/> builds the card each time it opens.</summary>
    public static Button Create(Func<Control> slip, string tag, double size = 16)
    {
        var t = new TextBlock
        {
            Text = "?", FontFamily = F.Display, FontWeight = FontWeight.Bold, FontSize = size * 0.62, Foreground = F.Lilac,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        // Opaque, so a "?" that sits on a button's corner reads as a badge, not a hole.
        var b = F.Pill(t, Badge, F.Lilac, tag, BadgeLine);
        b.CornerRadius = new CornerRadius(size / 2);
        b.Padding = new Thickness(0);
        b.Width = size;
        b.Height = size;
        b.MinWidth = 0;
        b.MinHeight = 0;
        b.Focusable = false;   // WPF: never takes focus, so Enter/Escape stay with the card and panic.
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        b.Cursor = new Cursor(StandardCursorType.Help);
        var ctl = new Controller(b, slip);
        Controllers.AddOrUpdate(b, ctl);
        b.Click += (_, e) => { e.Handled = true; ctl.TogglePin(); };
        return b;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Button, Controller> Controllers = new();

    /// <summary>Opens the slip pinned, and leaves it open when it already is. A "?" made elsewhere is ignored.</summary>
    public static void Pin(Button help)
    {
        if (Controllers.TryGetValue(help, out var ctl)) ctl.Pin();
    }

    internal static bool IsOpen(Button help) => Controllers.TryGetValue(help, out var c) && c.IsOpen;
    internal static bool IsPinned(Button help) => Controllers.TryGetValue(help, out var c) && c.Pinned;
    internal static Control? Slip(Button help) => Controllers.TryGetValue(help, out var c) ? c.Holder?.Child : null;

    private sealed class Controller
    {
        private readonly Button _b;
        private readonly Func<Control> _slip;
        private Popup? _popup;
        private TopLevel? _root;
        private readonly DispatcherTimer _leave;
        internal bool Pinned;
        internal Border? Holder;

        public Controller(Button b, Func<Control> slip)
        {
            _b = b;
            _slip = slip;
            _leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _leave.Tick += (_, _) => { _leave.Stop(); if (!Pinned && !Over()) Close(); };
            b.PointerEntered += (_, _) => { _leave.Stop(); Open(); };
            b.PointerExited += (_, _) => _leave.Start();
            b.PropertyChanged += (_, e) => { if (e.Property == Visual.IsVisibleProperty && e.NewValue is false) Close(); };
            b.DetachedFromVisualTree += (_, _) => Close();
        }

        internal bool IsOpen => _popup is { IsOpen: true };

        private bool Over() => _b.IsPointerOver || Holder?.IsPointerOver == true;

        public void Pin()
        {
            Pinned = true;
            Open();
        }

        public void TogglePin()
        {
            if (Pinned) { Close(); return; }
            Pinned = true;
            Open();
        }

        private void Open()
        {
            if (IsOpen || TopLevel.GetTopLevel(_b) is not { } top) return;
            try
            {
                Holder = new Border { Padding = new Thickness(0, 4, 8, 12), Background = Brushes.Transparent, Child = _slip() };
                Holder.PointerEntered += (_, _) => _leave.Stop();
                Holder.PointerExited += (_, _) => _leave.Start();
                // A press on the slip pins it (WPF holder.PreviewMouseDown) and stops there: the popup's
                // logical parent is the "?", which would otherwise take the press as its own click.
                Holder.AddHandler(InputElement.PointerPressedEvent, (_, e) => { Pinned = true; e.Handled = true; }, RoutingStrategies.Bubble, handledEventsToo: true);
                // Overlay layer where the platform allows it, never topmost (WPF drops the flag by hand).
                _popup = new Popup
                {
                    Child = Holder, Placement = PlacementMode.Bottom, PlacementTarget = _b, IsLightDismissEnabled = false,
                    Focusable = false, ShouldUseOverlayLayer = true, Topmost = false,
                };
                // A popup opens only once it is in the logical tree (HelpPopover does the same).
                ((ISetLogicalParent)_popup).SetParent(_b);
                _root = top;
                _root.AddHandler(InputElement.PointerPressedEvent, OnRootDown, RoutingStrategies.Tunnel, handledEventsToo: true);
                _popup.IsOpen = true;
            }
            catch (Exception ex) { Log.Debug("[Leash] help slip open failed: {E}", ex.Message); }
        }

        private void Close()
        {
            _leave.Stop();
            Pinned = false;
            _root?.RemoveHandler(InputElement.PointerPressedEvent, OnRootDown);
            _root = null;
            if (_popup != null)
            {
                _popup.IsOpen = false;
                ((ISetLogicalParent)_popup).SetParent(null);
            }
            _popup = null;
            Holder = null;
        }

        /// <summary>A press on the slip pins it; a press on the "?" is its own click; any other press folds it.</summary>
        private void OnRootDown(object? sender, PointerPressedEventArgs e)
        {
            if (e.Source is not Visual v) { Close(); return; }
            if (Holder != null && (ReferenceEquals(v, Holder) || Holder.IsVisualAncestorOf(v))) { Pinned = true; return; }
            if (ReferenceEquals(v, _b) || _b.IsVisualAncestorOf(v)) return;
            Close();
        }
    }
}
