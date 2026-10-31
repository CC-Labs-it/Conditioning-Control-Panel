// PORTED from WPF ConditioningControlPanel/Controls/Leash/LeashLevelHelp.cs (main a12bb6c53).
// What Soft, Standard and Strict allow, right under the level switch: one line per level (the leashed
// card) or the picked level's tagline (the ask card), and the full table one click away. Every row and
// figure comes from Core LeashLevelGuide, which reads the grammar. Flat: no Effect, no animation.
// The self card hosts this guide; the ask card is still missing on this head.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashLevelHelp : StackPanel
{
    /// <summary>Whether the table is unfolded. Kept for the run: picking a level redraws the card,
    /// and a table someone opened should not fold under their pointer.</summary>
    internal static bool Open { get; set; }

    private readonly Control _table;

    /// <summary>The render constructor --render-all discovers: the leashed card's look at Standard.</summary>
    internal LeashLevelHelp() : this(LeashIntensity.Standard, lines: true) { }

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
            var tagline = Wrap(F.Label(Loc.Get(LeashLevelGuide.TaglineKey(picked)), 11.5, F.Muted));
            tagline.Margin = new Thickness(2, 0, 0, 0);
            tagline.Tag = "leash-guide-tagline";
            Children.Add(tagline);
        }

        var toggle = F.Pill(
            new TextBlock { Text = Loc.Get("leash_guide_toggle"), FontFamily = F.Display, FontSize = 11.5, TextDecorations = TextDecorations.Underline },
            Brushes.Transparent, F.Lilac, "leash-guide-toggle");
        toggle.Padding = new Thickness(2, 3, 2, 3);
        toggle.Margin = new Thickness(0, 2, 0, 0);
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Click += (_, _) => Toggle();
        Children.Add(toggle);

        var box = new Border
        {
            Background = F.Raised,
            BorderBrush = F.Line2,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8, 10, 8),
            Child = Table(),
        };
        _table = double.IsInfinity(maxTable)
            ? box
            : new ScrollViewer { Content = box, MaxHeight = maxTable, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
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
        var g = new Grid
        {
            Margin = new Thickness(2, 1, 0, 1),
            Tag = "leash-guide-line:" + level.ToString().ToLowerInvariant() + (on ? ":on" : ""),
            ColumnDefinitions = new ColumnDefinitions("72,*"),
        };
        ToolTip.SetTip(g, Loc.Get(LeashLevelGuide.TaglineKey(level)));
        var name = F.Label(Loc.Get(LeashLevelGuide.LevelKey(level)), 11.5, on ? F.Lilac : F.Muted, F.Display, FontWeight.SemiBold);
        name.VerticalAlignment = VerticalAlignment.Top;
        var what = Wrap(F.Label(LeashLevelGuide.Summary(level, Loc.Get), 11.5, on ? F.Text : F.Dim));
        what.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(what, 1);
        g.Children.Add(name);
        g.Children.Add(what);
        return g;
    }

    private static Control Table()
    {
        var sp = new StackPanel();
        sp.Children.Add(Small(Loc.Get("leash_guide_keeps"), F.Muted, "leash-guide-keeps"));
        foreach (var level in LeashLevelGuide.Levels)
        {
            var head = Wrap(F.Label(
                Loc.Get(LeashLevelGuide.LevelKey(level)) + "  ·  " + Loc.Get(LeashLevelGuide.TaglineKey(level)),
                12, F.Lilac, F.Display, FontWeight.SemiBold));
            head.Margin = new Thickness(0, 8, 0, 2);
            head.Tag = "leash-guide-level:" + level.ToString().ToLowerInvariant();
            sp.Children.Add(head);
            foreach (var row in LeashLevelGuide.Rows(level))
            {
                var line = new StackPanel { Margin = new Thickness(0, 1, 0, 3), Tag = "leash-guide-row:" + row.Kind.ToString().ToLowerInvariant() };
                line.Children.Add(F.Label(Loc.Get(row.NameKey), 11.5, F.Text, null, FontWeight.SemiBold));
                line.Children.Add(Small(LeashLevelGuide.Detail(row, Loc.Get), F.Muted, null));
                sp.Children.Add(line);
            }
        }
        var limits = Small(Loc.GetF(LeashLevelGuide.LimitsKey, LeashLevelGuide.PunishmentsPerDay, LeashLevelGuide.ExpiresAfterHours), F.Text, "leash-guide-limits");
        limits.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(limits);

        // WPF LeashLook.Caption: the mono "section" voice, upper case.
        var always = F.Label(Loc.Get("leash_guide_always").ToUpperInvariant(), 9.5, F.Gold, F.Mono, FontWeight.SemiBold);
        always.Margin = new Thickness(0, 10, 0, 2);
        sp.Children.Add(always);
        foreach (var key in LeashLevelGuide.AlwaysKeys)
        {
            var rule = Small(Loc.Get(key), F.Muted, "leash-guide-rule:" + key);
            rule.Margin = new Thickness(0, 1, 0, 3);
            sp.Children.Add(rule);
        }
        return sp;
    }

    private static TextBlock Small(string text, IBrush fg, string? tag)
    {
        var t = Wrap(F.Label(text, 11, fg));
        t.Tag = tag;
        return t;
    }

    private static TextBlock Wrap(TextBlock t) { t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None; return t; }
}
