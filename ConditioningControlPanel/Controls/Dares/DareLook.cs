using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Controls.Dares;

/// <summary>
/// The Dares paint box, on the friends drawer's violets (<see cref="FriendsLook"/>): one hue per kind,
/// an icon first on every chip and tile, a round (?) where a rule hides, and the two bits of juice
/// (a chip that pops in when its blank fills, a stamp that thuds on and fades off). Every move reads
/// <see cref="MotionFx.Level"/> and asks <see cref="GentleVisuals"/> for its rise.
/// </summary>
internal static class DareLook
{
    /// <summary>MDL2 first, a fallback behind it (FontFallbackTests).</summary>
    public static readonly FontFamily GlyphFont = new(FriendsDrawerRules.GlyphFont);

    /// <summary>An ordinary symbol (tick, cross): MDL2 would draw an empty box for it.</summary>
    public static readonly FontFamily SymbolFont = new("Segoe UI Symbol, Segoe UI");

    public static Color ToneColor(DareTone t) => t switch
    {
        DareTone.Cant => FriendsLook.Red,
        DareTone.Duel => FriendsLook.Mint,
        DareTone.Pics => FriendsLook.Rgb(0xFF, 0x9A, 0x5C),
        DareTone.Minutes => FriendsLook.Rgb(0x4F, 0xD8, 0xD0),
        DareTone.Video => FriendsLook.Pink,
        DareTone.Session => FriendsLook.Lilac,
        DareTone.Bubbles => FriendsLook.Rgb(0x6B, 0xB8, 0xFF),
        DareTone.Dare => FriendsLook.Rgb(0xFF, 0x6F, 0x8E),
        DareTone.Sp => FriendsLook.Gold,
        DareTone.Lock => FriendsLook.Rgb(0x86, 0xA8, 0xFF),
        DareTone.Lockdown => FriendsLook.Rgb(0x9C, 0x7B, 0xFF),
        DareTone.CameraOn => FriendsLook.Mint,
        DareTone.CameraOff => FriendsLook.Dim,
        DareTone.Who => FriendsLook.Lilac,
        _ => FriendsLook.Muted,
    };

    /// <summary>MDL2 code points: Picture, Recent, Video, Play, CircleRing, Mail, FavoriteStar, Lock,
    /// Shield, Calendar, RedEye, Hide, Contact, Cancel, Switch.</summary>
    public static string ToneGlyph(DareTone t) => t switch
    {
        DareTone.Pics => "",
        DareTone.Minutes => "",
        DareTone.Video => "",
        DareTone.Session => "",
        DareTone.Bubbles => "",
        DareTone.Dare => "",
        DareTone.Sp => "",
        DareTone.Lock => "",
        DareTone.Lockdown => "",
        DareTone.When => "",
        DareTone.CameraOn => "",
        DareTone.CameraOff => "",
        DareTone.Cant => "",
        DareTone.Duel => "",
        _ => "",
    };

    public static Brush Tint(Color c, byte alpha) => FriendsLook.Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));

    public static TextBlock Glyph(string glyph, double size, Brush fg) => new()
    {
        Text = glyph,
        FontFamily = GlyphFont,
        FontSize = size,
        Foreground = fg,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static TextBlock Words(string text, double size, Brush fg, bool wrap = false, FontWeight? weight = null)
    {
        var t = FriendsLook.Label(text, size, fg, FriendsLook.Display, weight);
        if (wrap) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; }
        return t;
    }

    /// <summary>A filled blank: icon first, the words, in its kind's colour. <paramref name="onClick"/>
    /// null = read only (a bet already sent). Every chip carries a tooltip.</summary>
    public static FrameworkElement Chip(DareSeg seg, Action? onClick, bool current = false)
    {
        var c = ToneColor(seg.Tone);
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (seg.Tone == DareTone.Who)
        {
            var av = FriendsLook.Avatar(seg.Text, null, 18);
            av.Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(av);
        }
        else
        {
            var g = Glyph(ToneGlyph(seg.Tone), 12, FriendsLook.Frozen(c));
            g.Margin = new Thickness(0, 0, 6, 0);
            sp.Children.Add(g);
        }
        sp.Children.Add(Words(seg.Text, 14, FriendsLook.TextBrush, weight: FontWeights.Medium));
        var tip = Tip(seg.TipKey, onClick != null);

        if (onClick == null)
        {
            return new Border
            {
                Child = sp,
                Background = Tint(c, 0x26),
                BorderBrush = Tint(c, 0xB0),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 3, 10, 3),
                Margin = new Thickness(2, 2, 2, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tip,
                Tag = "dares-chip:" + seg.Step,
            };
        }
        var b = FriendsLook.Pill(sp, Tint(c, 0x26), FriendsLook.TextBrush, current ? FriendsLook.Frozen(c) : Tint(c, 0xB0),
            999, new Thickness(8, 3, 10, 3), Tint(c, 0x44));
        b.BorderThickness = new Thickness(current ? 2.5 : 1.5);
        b.Margin = new Thickness(2);
        b.ToolTip = tip;
        b.Tag = "dares-chip:" + seg.Step;
        b.Click += (_, _) => onClick();
        return b;
    }

    private static string? Tip(string? key, bool changeable)
    {
        if (key == null) return changeable ? Loc.Get("dares_tip_change") : null;
        var t = Loc.Get(key);
        return changeable ? t + " " + Loc.Get("dares_tip_change") : t;
    }

    /// <summary>A blank not yet filled: its hint word over a dashed line; the one in hand is lit.</summary>
    public static FrameworkElement Blank(DareSeg seg, bool current)
    {
        var t = FriendsLook.Label(seg.Text.ToUpperInvariant(), 10.5, current ? FriendsLook.TextBrush : FriendsLook.DimBrush, FriendsLook.Mono);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        var line = new Border
        {
            Child = t,
            MinWidth = 72,
            Padding = new Thickness(6, 4, 6, 2),
            Margin = new Thickness(3, 2, 3, 2),
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush = current ? FriendsLook.LilacBrush : FriendsLook.Line2Brush,
            Background = current ? Tint(FriendsLook.Lilac, 0x1C) : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "dares-blank:" + seg.Step,
        };
        if (current) PopIn(line);
        return line;
    }

    /// <summary>The mark at the head of an outcome line.</summary>
    public static FrameworkElement Mark(DareMark m, string? themName)
    {
        var (bg, text, sym) = m switch
        {
            DareMark.Yes => (FriendsLook.Mint, "✓", true),
            DareMark.No => (FriendsLook.Red, "✕", true),
            DareMark.Me => (FriendsLook.Lilac, Loc.Get("dares_mark_me"), false),
            _ => (FriendsLook.Pink, FriendsDrawerRules.Initials(themName), false),
        };
        return new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = FriendsLook.Frozen(bg),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = sym ? SymbolFont : FriendsLook.Display,
                FontSize = sym ? 12 : 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = FriendsLook.Frozen(FriendsLook.Ground),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>Draws sentence lines: words, chips and blanks flowing in a wrap panel per line.
    /// <paramref name="onChip"/> null = read only.</summary>
    public static StackPanel Sentence(IReadOnlyList<DareLine> lines, string? friendName, DareStep? current,
        Action<DareStep>? onChip, double size = 15)
    {
        var host = new StackPanel { Tag = "dares-sentence" };
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var row = new WrapPanel { Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0) };
            if (line.Mark != DareMark.None) row.Children.Add(Mark(line.Mark, friendName));
            foreach (var seg in line.Segs)
            {
                switch (seg.Kind)
                {
                    case DareSegKind.Text:
                        var t = Words(seg.Text, size, FriendsLook.MutedBrush);
                        t.Margin = new Thickness(1, 0, 1, 0);
                        row.Children.Add(t);
                        break;
                    case DareSegKind.Blank:
                        row.Children.Add(Blank(seg, seg.Step == current));
                        break;
                    default:
                        var step = seg.Step ?? DareStep.Who;
                        row.Children.Add(Chip(seg, onChip == null ? null : () => onChip(step), seg.Step == current));
                        break;
                }
            }
            host.Children.Add(row);
        }
        return host;
    }

    /// <summary>An option tile: a coloured disc with its icon, the word under it, a tooltip. Shut
    /// tiles say why in the tooltip.</summary>
    public static Button Tile(DareTone tone, string label, string? tipKey, string? shutKey, bool pressed, Action onClick, string tag,
        bool upsell = false)
    {
        var c = ToneColor(tone);
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (upsell)
        {
            // Locked stays visible (owner): the tile is an ad, a padlock on the disc and the Basic badge under it.
            var b0 = Tile(tone, label, tipKey, null, false, onClick, tag);
            if (b0.Content is StackPanel inner && inner.Children[0] is Border disc0)
            {
                disc0.Opacity = 0.55;
                var lockMark = Glyph("", 11, FriendsLook.TextBrush);
                var badge = new Grid();
                inner.Children.RemoveAt(0);
                badge.Children.Add(disc0);
                lockMark.HorizontalAlignment = HorizontalAlignment.Right;
                lockMark.VerticalAlignment = VerticalAlignment.Bottom;
                badge.Children.Add(lockMark);
                inner.Children.Insert(0, badge);
                if (FriendsLook.TierPlate(1, 13) is { } plate)
                {
                    plate.Margin = new Thickness(0, 4, 0, 0);
                    plate.HorizontalAlignment = HorizontalAlignment.Center;
                    inner.Children.Add(plate);
                }
            }
            b0.Tag = tag + ":locked";
            return b0;
        }
        var disc = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Background = FriendsLook.Frozen(c),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock
            {
                Text = ToneGlyph(tone),
                FontFamily = GlyphFont,
                FontSize = 17,
                Foreground = FriendsLook.Frozen(FriendsLook.Ground),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        sp.Children.Add(disc);
        var w = Words(label, 12.5, FriendsLook.TextBrush);
        w.HorizontalAlignment = HorizontalAlignment.Center;
        w.Margin = new Thickness(0, 5, 0, 0);
        sp.Children.Add(w);
        var b = FriendsLook.Pill(sp, pressed ? FriendsLook.ButtonHoverBrush : FriendsLook.ButtonBrush, FriendsLook.TextBrush,
            pressed ? FriendsLook.Frozen(c) : FriendsLook.Line2Brush, 12, new Thickness(10, 9, 10, 8));
        b.MinWidth = 86;
        b.Margin = new Thickness(0, 0, 8, 8);
        b.BorderThickness = new Thickness(pressed ? 2 : 1, pressed ? 2 : 1, pressed ? 2 : 1, 3);
        if (!pressed) b.BorderBrush = Tint(c, 0x90);
        b.Tag = tag;
        if (shutKey != null)
        {
            b.IsEnabled = false;
            ToolTipService.SetShowOnDisabled(b, true);
            b.ToolTip = Loc.Get(shutKey);
        }
        else if (tipKey != null) b.ToolTip = Loc.Get(tipKey);
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>An amount or deadline chip in the tray.</summary>
    public static Button Choice(string label, Color c, string? tip, Action onClick, string tag)
    {
        var b = FriendsLook.Pill(Words(label, 14, FriendsLook.TextBrush, weight: FontWeights.SemiBold), FriendsLook.ButtonBrush,
            FriendsLook.TextBrush, Tint(c, 0xA0), 999, new Thickness(14, 5, 14, 5), Tint(c, 0x40));
        b.Margin = new Thickness(0, 0, 8, 8);
        b.ToolTip = tip;
        b.Tag = tag;
        b.Click += (_, _) => onClick();
        return b;
    }

    /// <summary>The small round (?): its rule in a tooltip, and pinned open on click.</summary>
    public static FrameworkElement Help(string key)
    {
        var text = Loc.Get(key);
        var b = FriendsLook.Pill(new TextBlock
        {
            Text = "?",
            FontFamily = FriendsLook.Display,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FriendsLook.LilacBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        }, Brushes.Transparent, FriendsLook.LilacBrush, FriendsLook.LilacBrush, 999, new Thickness(6, 0, 6, 1), FriendsLook.HoverBrush);
        b.Width = 20;
        b.Height = 20;
        b.Margin = new Thickness(6, 0, 0, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        b.ToolTip = text;
        b.Tag = "dares-help:" + key;
        var pin = new ToolTip { Content = text, Placement = PlacementMode.Bottom, PlacementTarget = b, MaxWidth = 300 };
        b.Click += (_, _) => pin.IsOpen = !pin.IsOpen;
        b.MouseLeave += (_, _) => pin.IsOpen = false;
        return b;
    }

    /// <summary>A thin progress bar in a kind's colour.</summary>
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

    // ---- juice -------------------------------------------------------------------------

    private static double Amount => MotionFx.Level switch { MotionLevel.Off => 0, MotionLevel.Reduced => 0.5, _ => 1 };

    /// <summary>IN: a chip that just filled swells a little and settles.</summary>
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

    /// <summary>The stamp: a tilted word in a double frame. IN = it thuds down from big; OUT
    /// (<see cref="StampOut"/>) = it fades away.</summary>
    public static Border Stamp(string word, Color c)
    {
        var st = new Border
        {
            BorderBrush = FriendsLook.Frozen(c),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 2, 12, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 12),
            IsHitTestVisible = false,
            Opacity = 0.9,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Child = new TextBlock
            {
                Text = word.ToUpperInvariant(),
                FontFamily = FriendsLook.Display,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = FriendsLook.Frozen(c),
            },
            Tag = "dares-stamp",
        };
        var rot = new RotateTransform(-11);
        var scale = new ScaleTransform(1, 1);
        var g = new TransformGroup();
        g.Children.Add(scale);
        g.Children.Add(rot);
        st.RenderTransform = g;
        double k = Amount;
        if (k > 0)
        {
            var dur = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(280));
            var ease = new BackEase { Amplitude = 0.5 * k, EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1 + 1.2 * k, 1, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1 + 1.2 * k, 1, dur) { EasingFunction = ease });
            st.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.9, dur));
        }
        return st;
    }

    public static void StampOut(FrameworkElement stamp, Action? done = null)
    {
        if (Amount <= 0) { done?.Invoke(); return; }
        var a = new DoubleAnimation(stamp.Opacity, 0, TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(240)));
        if (done != null) a.Completed += (_, _) => done();
        stamp.BeginAnimation(UIElement.OpacityProperty, a);
    }

    /// <summary>IN for a card window: up a little and in. OUT: <see cref="FadeOutThen"/>.</summary>
    public static void CardIn(FrameworkElement card)
    {
        double k = Amount;
        if (k <= 0) return;
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

    /// <summary>The card window shell every Dares window wears: borderless, the drawer's glass, a
    /// drag anywhere that is not a button (AGENTS: draggable borderless windows skip clickables).</summary>
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
            Background = FriendsLook.GlassBrush,
            BorderBrush = FriendsLook.Line2Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(14),
            Padding = new Thickness(18, 16, 18, 16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.5 },
        };
        w.Content = card;
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

    /// <summary>A heading row: an icon, the title, an optional (?) and a close cross.</summary>
    public static Grid Head(string title, string? helpKey, Action? onClose)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Glyph(ToneGlyph(DareTone.Dare), 16, FriendsLook.PinkBrush);
        icon.Margin = new Thickness(0, 0, 8, 0);
        left.Children.Add(icon);
        left.Children.Add(Words(title.ToUpperInvariant(), 15, FriendsLook.TextBrush, weight: FontWeights.SemiBold));
        if (helpKey != null) left.Children.Add(Help(helpKey));
        g.Children.Add(left);
        if (onClose != null)
        {
            var x = FriendsLook.Pill(Glyph("", 10, FriendsLook.MutedBrush), Brushes.Transparent, FriendsLook.MutedBrush,
                Brushes.Transparent, 8, new Thickness(7, 5, 7, 5), FriendsLook.HoverBrush);
            x.ToolTip = Loc.Get("dares_close");
            x.Tag = "dares-close";
            x.Click += (_, _) => onClose();
            Grid.SetColumn(x, 1);
            g.Children.Add(x);
        }
        return g;
    }

    /// <summary>The big action button (mint) and the quiet one (outline, with a small line under it).</summary>
    public static Button Big(string text, Color c, Action onClick, string tag)
    {
        var b = FriendsLook.Pill(Words(text, 15, FriendsLook.Frozen(FriendsLook.Ground), weight: FontWeights.SemiBold),
            FriendsLook.Frozen(c), FriendsLook.Frozen(FriendsLook.Ground), FriendsLook.Frozen(c), 10, new Thickness(18, 8, 18, 8), FriendsLook.Frozen(c));
        b.Margin = new Thickness(0, 0, 8, 8);
        b.Tag = tag;
        b.Click += (_, _) => onClick();
        return b;
    }

    public static Button Quiet(string text, string? sub, Action onClick, string tag)
    {
        var sp = new StackPanel();
        sp.Children.Add(Words(text, 13.5, FriendsLook.TextBrush, weight: FontWeights.Medium));
        if (sub != null) sp.Children.Add(Words(sub, 11, FriendsLook.MutedBrush));
        var b = FriendsLook.Pill(sp, Brushes.Transparent, FriendsLook.TextBrush, FriendsLook.Line2Brush, 10, new Thickness(14, 6, 14, 6));
        b.Margin = new Thickness(0, 0, 8, 8);
        b.Tag = tag;
        b.Click += (_, _) => onClick();
        return b;
    }
}
