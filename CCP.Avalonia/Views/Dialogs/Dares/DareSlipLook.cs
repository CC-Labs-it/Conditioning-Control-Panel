using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;

// WPF 1468acc21b Controls/Dares/DareLook.cs:40-89, 314-450, 551-622, 690-823, 919-947, 1042-1166: the pink
// slip, handwritten fills, tiles (pressed = white with an ink outline, class "on"), light chips, the big button, the
// motion (hover lift, pop-in, card-in), and a greyed choice that still shows its reason (tooltip on disabled + help text).
internal static class DareSlipLook
{
    internal static readonly Color SlipPaper = Color.FromRgb(0xFF, 0xD3, 0xE6);
    internal static readonly IBrush Print = Brush(0x4B1F45), Ink = Brush(0x3A1D78), FillPaper = Brush(0xFFFAF8), TileFace = Brush(0xF7EEF8), TileEdge = Brush(0xDCC6DD),
        CardInk = Brush(0x2E183A), Chalk = Brush(0xEFE6FF), Muted = Brush(0xA993CF), Warn = Brush(0xFFCF6B), StampRed = Brush(0xD2265F), White = Brushes.White;
    internal static readonly FontFamily Hand = new(AppFonts.Caveat), PrintFont = new(AppFonts.BarlowCondensed);

    private static IBrush Brush(int rgb) => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    private static bool Motion => AmbientFxCanvas.Env.AllowTransitions;
    internal static T Also<T>(this T value, Action<T> act) { act(value); return value; }

    internal static TextBlock Words(string text, double size, IBrush fg, bool wrap = false, FontWeight? weight = null, bool hand = false) => new() { Text = text, FontFamily = hand ? Hand : PrintFont,
        FontSize = size, Foreground = fg, FontWeight = weight ?? (hand ? FontWeight.Bold : FontWeight.Medium), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center };
    internal static TextBlock Caps(string text, double size, IBrush fg, FontWeight weight) => new() { Text = text.ToUpperInvariant(), FontFamily = PrintFont, FontSize = size, Foreground = fg, FontWeight = weight, LetterSpacing = size * 0.16 };
    internal static IconGlyph Icon(IconKind kind, double size, IBrush fg) => new() { Kind = kind, Size = size, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    internal static Border Slip(Control head, Control body, out Panel paper) => new()
    {
        Tag = "dares-slip", Background = new SolidColorBrush(SlipPaper), CornerRadius = new CornerRadius(2), Padding = new Thickness(22, 26, 22, 22),
        Margin = new Thickness(2, 4, 2, 8), RenderTransform = new RotateTransform(-0.6), Child = paper = new Grid { Children = { new StackPanel { Children = { head, body } } } },
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 12, Blur = 26, Color = Color.FromArgb(0x99, 0, 0, 0) }),
    };
    internal static Control SlipHead(string title, Action? onClose, string? helpKey = null)
    {
        var head = new DockPanel();
        if (onClose != null)
        {
            head.Children.Add(Bare(Icon(IconKind.Dismiss, 18, Print), "dares-close", Loc.Get("dares_close"), onClose)
                .Also(c => { c.Margin = new Thickness(6, 0, -6, 0); ToolTip.SetTip(c, Loc.Get("dares_close")); DockPanel.SetDock(c, Dock.Right); }));
        }
        if (helpKey != null)
        {
            var text = Loc.Get(helpKey);
            var pinned = false;
            var face = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderBrush = Print,
                BorderThickness = new Thickness(1.5), Child = Words("?", 15, Print, weight: FontWeight.Bold).Also(q => q.HorizontalAlignment = HorizontalAlignment.Center) };
            Button? help = null;
            help = Bare(face, "dares-help:" + helpKey, text, () => { pinned = !pinned; ToolTip.SetIsOpen(help!, pinned); });
            help.Focusable = true;
            help.Margin = new Thickness(8, 0, 0, 0);
            ToolTip.SetTip(help, new TextBlock { Text = text, MaxWidth = 320, TextWrapping = TextWrapping.Wrap });
            ToolTip.SetPlacement(help, PlacementMode.Bottom);
            help.PointerExited += (_, _) => { pinned = false; ToolTip.SetIsOpen(help, false); };
            DockPanel.SetDock(help, Dock.Right);
            head.Children.Add(help);
        }
        head.Children.Add(Caps(title, 19, Print, FontWeight.Bold));
        return new Border { Child = head, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(0, 0, 0, 5), BorderBrush = Print, BorderThickness = new Thickness(0, 0, 0, 2) };
    }

    internal static Button Bare(Control content, string tag, string name, Action onClick)
    {
        var b = new Button { Content = content, Tag = tag, Focusable = false, Cursor = new Cursor(StandardCursorType.Hand), Template = new FuncControlTemplate<Button>((_, _) =>
            new ContentPresenter { Name = "PART_ContentPresenter", Background = Brushes.Transparent, [!ContentPresenter.ContentProperty] = new TemplateBinding(ContentControl.ContentProperty) }) };
        AutomationProperties.SetName(b, name);
        b.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsEnabledProperty) b.Opacity = b.IsEnabled ? 1.0 : 0.45; };
        b.Click += (_, _) => onClick();
        return b;
    }

    internal static Button Fill(string text, DareTone tone, bool current, Action onClick, string tag)
    {
        var inverted = tone == DareTone.Lockdown;
        IBrush rest = inverted ? Ink : FillPaper, color = new SolidColorBrush(DareLook.ToneColor(tone));
        var face = new Border { Background = rest, BorderBrush = inverted ? Ink : color, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 3, 10, 3),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Icon(DareLook.ToneIcon(tone), 24, inverted ? White : color), Words(text, 29, inverted ? White : Ink, hand: true) } } };
        Control body = current ? new Border { Child = face, BorderBrush = Ink, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(11), Padding = new Thickness(1) } : face;
        return Hover(Bare(body, tag, text, onClick).Also(b => b.Margin = new Thickness(4, 0, 4, 0)), face, rest, inverted ? Brush(0x4A2A90) : White);
    }

    // WPF DareLook.cs:453-469: outcome discs precede the line and identify duel sides.
    internal static Grid Mark(DareMark mark, string? friendName)
    {
        var disc = new Grid { Width = 26, Height = 26, Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center, Tag = "dares-mark:" + mark };
        disc.Children.Add(new Ellipse { Fill = mark == DareMark.Yes ? Brush(0x1FA877) : mark == DareMark.No ? StampRed : Ink });
        if (mark is DareMark.Yes or DareMark.No)
            disc.Children.Add(Icon(mark == DareMark.Yes ? IconKind.Checkmark : IconKind.Dismiss, 17, White));
        else
        {
            var initials = FriendsDrawerRules.Initials(friendName);
            var letter = mark == DareMark.Me ? Loc.Get("dares_mark_me") : string.IsNullOrEmpty(initials) ? "?" : initials[..1];
            disc.Children.Add(Words(letter, letter.Length > 1 ? 15 : 20, White, hand: true).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center));
            AutomationProperties.SetName(disc, mark == DareMark.Me ? Loc.Get("dares_mark_me") : friendName ?? "");
        }
        return disc;
    }

    internal static Control Blank(string text, bool current) => new Border { Child = Words(text, 26, current ? Ink : Print, hand: true), Opacity = current ? 1 : 0.6,
        Margin = new Thickness(4, 0, 4, 0), BorderBrush = current ? Ink : Print, BorderThickness = new Thickness(0, 0, 0, current ? 3 : 2) };
    internal static Control Disc(DareTone tone, bool locked = false)
    {
        var disc = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = new SolidColorBrush(DareLook.ToneColor(tone)), Opacity = locked ? 0.55 : 1,
            Child = Icon(DareLook.ToneIcon(tone), 28, White) };
        return !locked ? disc : new Grid { Children = { disc, new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = White, BorderBrush = Ink,
            BorderThickness = new Thickness(1.2), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -3, -3),
            Child = Icon(IconKind.LockClosed, 13, Ink) } } };
    }

    internal static Control Initial(string name) => new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = new SolidColorBrush(DareLook.ToneColor(DareTone.Who)),
        Child = Words(name.Length > 0 ? name[..1].ToUpperInvariant() : "?", 23, White, weight: FontWeight.Bold).Also(t => t.HorizontalAlignment = HorizontalAlignment.Center) };
    internal static Button Tile(Control top, string label, Color accent, string? tip, string? shut, bool pressed, Action onClick, string tag)
    {
        top.HorizontalAlignment = HorizontalAlignment.Center;
        var rest = pressed ? White : TileFace;
        var face = new Border { Background = rest, BorderBrush = TileEdge, BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(8, 8, 3, 3),
            Padding = new Thickness(8, 10, 8, 8), Margin = new Thickness(0, 0, 0, 4), Child = new StackPanel { Spacing = 4, Children = { top,
                Words(label, 16, CardInk, wrap: true).Also(w => { w.TextAlignment = TextAlignment.Center; w.MaxWidth = 112; w.HorizontalAlignment = HorizontalAlignment.Center; }) } } };
        var g = new Grid { MinWidth = 92, Children = { new Border { Background = new SolidColorBrush(accent), CornerRadius = new CornerRadius(8) }, face } };
        if (pressed) g.Children.Add(new Border { BorderBrush = Ink, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(8), IsHitTestVisible = false });
        var b = Bare(g, tag, label, onClick).Also(t => { t.Margin = new Thickness(0, 0, 10, 10); t.VerticalAlignment = VerticalAlignment.Top; t.Classes.Set("on", pressed); });
        Explain(b, shut ?? tip, shut != null);
        return Hover(b, face, rest, White, lift: true);
    }

    internal static Button Chip(string label, string? tip, Action onClick, string tag)
    {
        var face = new Border { Background = TileFace, BorderBrush = TileEdge, BorderThickness = new Thickness(1, 1, 1, 4), CornerRadius = new CornerRadius(7),
            Padding = new Thickness(14, 6, 14, 5), Child = Words(label, 19, CardInk, weight: FontWeight.Bold) };
        var b = Bare(face, tag, label, onClick).Also(c => { c.Margin = new Thickness(0, 0, 8, 8); c.HorizontalAlignment = HorizontalAlignment.Left; });
        Explain(b, tip, false);
        return Hover(b, face, TileFace, White);
    }

    internal static void Explain(Button b, string? why, bool shut)
    { ToolTip.SetTip(b, why); if (shut) { b.IsEnabled = false; ToolTip.SetShowOnDisabled(b, true); AutomationProperties.SetHelpText(b, why); } }

    internal static Button Big(string text, Action onClick, string tag) => Bare(new Border { Background = Brush(0xFF4FA3), BorderBrush = Brush(0xA3175F),
        BorderThickness = new Thickness(0, 0, 0, 5), CornerRadius = new CornerRadius(12), Padding = new Thickness(28, 10, 28, 8), Child = Caps(text, 22, White, FontWeight.Bold) }, tag, text, onClick);
    internal static Border Stamp(string word) => new() { BorderBrush = StampRed, BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 2, 12, 2), Opacity = 0.85, Tag = "dares-stamp", IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, RenderTransform = new RotateTransform(-12), Child = Caps(word, 30, StampRed, FontWeight.Bold) };

    private static Button Hover(Button b, Border face, IBrush rest, IBrush hover, bool lift = false)
    {
        var up = new TranslateTransform { Transitions = Motion ? new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(120) } } : null };
        if (lift) b.RenderTransform = up;
        b.PointerEntered += (_, _) => { if (b.IsEnabled) { face.Background = hover; up.Y = lift && Motion ? -3 : 0; } };
        b.PointerExited += (_, _) => { face.Background = rest; up.Y = 0; };
        return b;
    }

    internal static void PopIn(Control el) => Run(el, 380, false, (0, 0.7), (0.4, 1.12), (1, 1.0));
    internal static void CardIn(Control el) => Run(el, 240, true, (0, 0.96), (1, 1.0));

    // WPF DareLook.FadeOutThen (Controls/Dares/DareLook.cs:1168-1174): 160 ms fade, then close; motion off closes at once.
    internal static async Task FadeOutThen(Window w, Action then)
    {
        if (Motion)
            await new Animation { Duration = TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(160)), FillMode = FillMode.Forward,
                Children = { new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, w.Opacity) } },
                             new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0) } } } }.RunAsync(w);
        then();
    }

    private static void Run(Control el, int ms, bool fade, params (double Cue, double Scale)[] keys)
    {
        if (!Motion) return;
        el.RenderTransformOrigin = RelativePoint.Center;
        el.RenderTransform = new ScaleTransform();
        var anim = new Animation { Duration = TimeSpan.FromMilliseconds(ms) };
        foreach (var (cue, scale) in keys) // opacity only on a fade: a pop never lifts a dimmed (disabled) choice
            anim.Children.Add(new KeyFrame { Cue = new Cue(cue), Setters = { new Setter(ScaleTransform.ScaleXProperty, scale), new Setter(ScaleTransform.ScaleYProperty, scale) } }
                .Also(f => { if (fade) f.Setters.Add(new Setter(Visual.OpacityProperty, cue)); }));
        _ = anim.RunAsync(el);
    }
}
