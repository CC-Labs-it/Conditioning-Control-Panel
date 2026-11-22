using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Controls.Friends;

/// <summary>
/// The folds (owner 2026-11-22): a busy section sits under one header line that opens and folds
/// it (<see cref="FriendsFold"/> holds the rules and the memory). "What happened" is the first.
/// </summary>
public sealed partial class FriendsDrawer
{
    /// <summary>The header just pressed: its chevron turns instead of snapping.</summary>
    private string? _foldTurned;


    /// <summary>One header line: chevron, title, an optional summary and bubble, something on the
    /// right (a count), and an optional extra button beside it. The whole line toggles.</summary>
    private FrameworkElement FoldHead(string name, string title, string titleTag, bool open, Action toggle,
        string? summary = null, int badge = 0, FrameworkElement? right = null, FrameworkElement? extra = null)
    {
        var g = new Grid { Margin = new Thickness(2, 6, 2, 2), Tag = "friends-fold:" + name };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var inner = new Grid();
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var turn = new RotateTransform(open ? 90 : 0);
        var chevron = new Path
        {
            Data = Geometry.Parse("M0,0 L4,4 L0,8"),
            Stroke = FriendsLook.DimBrush,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 6,
            Height = 9,
            Margin = new Thickness(2, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.35, 0.5),
            RenderTransform = turn,
            Tag = "friends-fold-chevron",
        };
        inner.Children.Add(chevron);

        var t = FriendsLook.Label(title, 10, FriendsLook.DimBrush, FriendsLook.Mono);
        t.Tag = titleTag;
        t.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(t, 1);
        inner.Children.Add(t);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            var s = FriendsLook.Label(summary!, 11.5, FriendsLook.LilacBrush, FriendsLook.Display);
            s.Margin = new Thickness(8, 0, 4, 0);
            s.VerticalAlignment = VerticalAlignment.Center;
            s.TextTrimming = TextTrimming.CharacterEllipsis;
            s.Tag = "friends-fold-summary";
            Grid.SetColumn(s, 2);
            inner.Children.Add(s);
        }

        if (badge > 0)
        {
            var b = new Border
            {
                Background = FriendsLook.PinkBrush,
                CornerRadius = new CornerRadius(999),
                MinWidth = 16,
                Height = 16,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = FriendsLook.Glow(FriendsLook.Pink, 8, 0.7),
                Tag = "friends-fold-badge",
                Child = new TextBlock
                {
                    Text = Services.Friends.FriendsFeedRules.BadgeText(badge),
                    FontFamily = FriendsLook.Display,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 10.5,
                    Foreground = FriendsLook.Frozen(FriendsLook.Rgb(0x2A, 0x06, 0x1A)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(b, 3);
            inner.Children.Add(b);
            if (_foldTurned == null && _isOpen) PopBadge(b);
        }

        if (right != null)
        {
            right.VerticalAlignment = VerticalAlignment.Center;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(right, 4);
            inner.Children.Add(right);
        }

        var btn = FriendsLook.Pill(inner, Brushes.Transparent, FriendsLook.TextBrush, Brushes.Transparent, 8,
            new Thickness(6, 3, 6, 3), FriendsLook.HoverBrush);
        btn.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        btn.Tag = "friends-fold-toggle:" + name;
        btn.ToolTip = Loc.Get(open ? "friends_fold_close" : "friends_fold_open");
        AutomationProperties.SetName(btn, title);
        btn.Click += (_, _) =>
        {
            FriendsSfx.Click();
            _foldTurned = name;
            toggle();
        };
        g.Children.Add(btn);

        if (extra != null)
        {
            extra.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(extra, 1);
            g.Children.Add(extra);
        }

        // In and out: the chevron turns both ways when its own header was the one pressed.
        if (_foldTurned == name)
        {
            _foldTurned = null;
            if (MotionFx.AllowTransitions)
                turn.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open ? 0 : 90, open ? 90 : 0,
                    TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        return g;
    }

    /// <summary>A small round x: dismiss a dare, hide the feed preview.</summary>
    private static Button XButton(string tag, string tip)
    {
        var x = new Path
        {
            Data = Geometry.Parse("M0,0 L7,7 M7,0 L0,7"),
            Stroke = FriendsLook.MutedBrush,
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 7,
            Height = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var b = FriendsLook.Pill(x, Brushes.Transparent, FriendsLook.MutedBrush, Brushes.Transparent, 999,
            new Thickness(6), FriendsLook.HoverBrush);
        b.Width = 22;
        b.Height = 22;
        b.Tag = tag;
        b.ToolTip = tip;
        AutomationProperties.SetName(b, tip);
        return b;
    }

    /// <summary>The out of a dismiss or a clear: the rows fade and slide right, then
    /// <paramref name="then"/> redraws. At once under Motion Off.</summary>
    private static void FadeAway(IReadOnlyList<FrameworkElement> rows, Action then)
    {
        var k = Amount;
        if (k <= 0 || rows.Count == 0) { then(); return; }
        var dur = TimeSpan.FromMilliseconds(170);
        var left = rows.Count;
        foreach (var r in rows)
        {
            r.IsHitTestVisible = false;
            var slide = new TranslateTransform();
            r.RenderTransform = slide;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, 14 * k, dur));
            var fade = new DoubleAnimation(r.Opacity, 0, dur);
            fade.Completed += (_, _) => { if (--left == 0) then(); };
            r.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>The bubble pops in when it shows in an open drawer.</summary>
    private static void PopBadge(FrameworkElement b)
    {
        var k = Amount;
        if (k <= 0) return;
        var s = new ScaleTransform(0.4, 0.4);
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        b.RenderTransform = s;
        var a = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { Amplitude = 0.6 * k, EasingMode = EasingMode.EaseOut } };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        s.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Freshly shown rows rise in one after another (the in of an open).</summary>
    private static void RiseIn(IEnumerable<FrameworkElement> rows)
    {
        try { MotionFx.StaggerIn(rows.ToList()); }
        catch (Exception ex) { App.Logger?.Debug("[Friends] fold rise failed: {E}", ex.Message); }
    }
}
