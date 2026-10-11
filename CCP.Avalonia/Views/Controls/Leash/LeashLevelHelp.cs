// What Soft, Standard and Strict allow, right under the level switch (players asked, 2026-10-10).
// Always visible: one line per level (the leashed card) or the picked level in a few words (the
// ask card, whose chips already list the punishments). One click away: the table, the limits and
// what holds at every level. Every row and figure comes from Core LeashLevelGuide, which reads the
// grammar, so the words cannot drift from what a level really allows. Explains only.
// Flat like the rest of the leash look: drawer palette, no Effect, no animation.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashLevelHelp : StackPanel
{
    /// <summary>Whether the table is unfolded. Kept for the run: picking a level redraws the card,
    /// and a table someone opened should not fold under their pointer.</summary>
    internal static bool Open { get; set; }

    private readonly Control _table;
    private readonly Button _toggle;

    /// <param name="picked">The level the switch shows.</param>
    /// <param name="lines">True: one "what it allows" line per level. False: the picked level's tagline only.</param>
    /// <param name="maxTable">Caps the unfolded table's height (the ask card sits in a fixed overlay).</param>
    public LeashLevelHelp(LeashIntensity picked, bool lines, double maxTable = double.PositiveInfinity)
    {
        Margin = new Thickness(0, 6, 0, 0);
        Tag = "leash-guide";
        if (lines)
        {
            foreach (var level in LeashLevelGuide.Levels) Children.Add(Line(level, level == picked));
        }
        else
        {
            var tagline = LeashLook.Wrap(FriendsDrawer.Label(Loc.Get(LeashLevelGuide.TaglineKey(picked)), 11.5, FriendsDrawer.Muted));
            tagline.Margin = new Thickness(2, 0, 0, 0);
            tagline.Tag = "leash-guide-tagline";
            Children.Add(tagline);
        }

        _toggle = FriendsDrawer.Pill(
            new TextBlock { Text = Loc.Get("leash_guide_toggle"), FontFamily = FriendsDrawer.Display, FontSize = 11.5, TextDecorations = TextDecorations.Underline },
            Brushes.Transparent, FriendsDrawer.Lilac, "leash-guide-toggle", Brushes.Transparent);
        _toggle.Padding = new Thickness(2, 3, 2, 3);
        _toggle.Margin = new Thickness(0, 2, 0, 0);
        _toggle.HorizontalAlignment = HorizontalAlignment.Left;
        _toggle.Cursor = FriendsDrawer.Hand();
        _toggle.Click += (_, _) => Toggle();
        Children.Add(_toggle);

        var box = new Border
        {
            Background = FriendsDrawer.Raised,
            BorderBrush = FriendsDrawer.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Child = Table(),
        };
        _table = double.IsInfinity(maxTable)
            ? box
            : new ScrollViewer { Content = box, MaxHeight = maxTable, VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        _table.Margin = new Thickness(0, 4, 0, 0);
        _table.Tag = "leash-guide-table";
        _table.IsVisible = Open;
        Children.Add(_table);
    }

    internal bool TableShown => _table.IsVisible;

    internal void Toggle()
    {
        Open = !Open;
        _table.IsVisible = Open;
    }

    /// <summary>"Standard   + Bubble quota, Detention, Video": the picked level reads brighter.</summary>
    private static Control Line(LeashIntensity level, bool on)
    {
        var g = new Grid { Margin = new Thickness(2, 1, 0, 1), Tag = "leash-guide-line:" + level.ToString().ToLowerInvariant() + (on ? ":on" : "") };
        g.ColumnDefinitions.Add(new ColumnDefinition(72, GridUnitType.Pixel));
        g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        var name = FriendsDrawer.Label(Loc.Get(LeashLevelGuide.LevelKey(level)), 11.5, on ? FriendsDrawer.Lilac : FriendsDrawer.Muted, FriendsDrawer.Display, FontWeight.SemiBold);
        var what = LeashLook.Wrap(FriendsDrawer.Label(LeashLevelGuide.Summary(level, Loc.Get), 11.5, on ? FriendsDrawer.Text : FriendsDrawer.Dim));
        ToolTip.SetTip(g, Loc.Get(LeashLevelGuide.TaglineKey(level)));
        Grid.SetColumn(what, 1);
        g.Children.Add(name);
        g.Children.Add(what);
        return g;
    }

    private static Control Table()
    {
        var sp = new StackPanel();
        sp.Children.Add(Small(Loc.Get("leash_guide_keeps"), FriendsDrawer.Muted, "leash-guide-keeps"));
        foreach (var level in LeashLevelGuide.Levels)
        {
            var head = LeashLook.Wrap(FriendsDrawer.Label(
                Loc.Get(LeashLevelGuide.LevelKey(level)) + "  ·  " + Loc.Get(LeashLevelGuide.TaglineKey(level)),
                12, FriendsDrawer.Lilac, FriendsDrawer.Display, FontWeight.SemiBold));
            head.Margin = new Thickness(0, 8, 0, 2);
            head.Tag = "leash-guide-level:" + level.ToString().ToLowerInvariant();
            sp.Children.Add(head);
            foreach (var row in LeashLevelGuide.Rows(level))
            {
                var line = new StackPanel { Margin = new Thickness(0, 1, 0, 3), Tag = "leash-guide-row:" + row.Kind.ToString().ToLowerInvariant() };
                line.Children.Add(FriendsDrawer.Label(Loc.Get(row.NameKey), 11.5, FriendsDrawer.Text, null, FontWeight.SemiBold));
                line.Children.Add(Small(LeashLevelGuide.Detail(row, Loc.Get), FriendsDrawer.Muted, null));
                sp.Children.Add(line);
            }
        }
        var limits = Small(Loc.GetF(LeashLevelGuide.LimitsKey, LeashLevelGuide.PunishmentsPerDay, LeashLevelGuide.ExpiresAfterHours), FriendsDrawer.Text, "leash-guide-limits");
        limits.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(limits);

        var always = LeashLook.Caption(Loc.Get("leash_guide_always"), FriendsDrawer.Gold);
        always.Margin = new Thickness(0, 10, 0, 2);
        sp.Children.Add(always);
        foreach (var key in LeashLevelGuide.AlwaysKeys)
        {
            var rule = Small(Loc.Get(key), FriendsDrawer.Muted, "leash-guide-rule:" + key);
            rule.Margin = new Thickness(0, 1, 0, 3);
            sp.Children.Add(rule);
        }
        return sp;
    }

    private static TextBlock Small(string text, IBrush fg, string? tag)
    {
        var t = LeashLook.Wrap(FriendsDrawer.Label(text, 11, fg));
        t.Tag = tag;
        return t;
    }
}
