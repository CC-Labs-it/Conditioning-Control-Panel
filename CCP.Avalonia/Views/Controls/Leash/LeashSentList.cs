// PORTED from WPF ConditioningControlPanel/Controls/Leash/LeashHolderCard.cs:131-225 (SentSection,
// StepBrush, ItemIcon, SentRow): the holder's receipts. The last few things sent to one leashed friend,
// each with its steps (sent, arrived, seen, then done / skipped / refused), its step word and how long
// ago. The rules (rows kept, lit dots, words, times, tooltip timeline) are Core LeashUiRules / LeashSentLog.
// Hosted by LeashHolderCard on Social > Leash.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashSentList : StackPanel
{
    /// <summary>WPF SentSection: null until the server speaks receipts, or when nothing was sent, so an
    /// older server draws exactly what it drew before.</summary>
    internal static LeashSentList? For(ILeashService? svc, string leashedId)
    {
        IReadOnlyList<LeashSentItem> items;
        try
        {
            if (svc?.ReceiptsSupported != true) return null;
            items = svc.SentTo(leashedId);
        }
        catch { return null; }
        return items.Count == 0 ? null : new LeashSentList(items, DateTimeOffset.UtcNow);
    }

    /// <summary>The render-all sample: a seen punishment, an arrived task, a refused tug.</summary>
    public LeashSentList() : this(Sample(DateTimeOffset.UtcNow), DateTimeOffset.UtcNow) { }

    internal LeashSentList(IReadOnlyList<LeashSentItem> items, DateTimeOffset now)
    {
        Margin = new Thickness(2, 10, 0, 0);
        Tag = "leash-sent";
        var head = F.Label(Loc.Get("leash_sent_title").ToUpperInvariant(), 9.5, F.Dim, F.Mono, FontWeight.SemiBold);
        (head.Margin, head.HorizontalAlignment) = (new Thickness(0, 0, 0, 3), HorizontalAlignment.Left);
        Children.Add(head);
        foreach (var i in items.Take(LeashUiRules.SentRows)) Children.Add(Row(i, now));
    }

    private static IReadOnlyList<LeashSentItem> Sample(DateTimeOffset now) => new[]
    {
        new LeashSentItem("l1", LeashItemKind.Punish, "vex", "0123456789abcdef", LeashStep.Seen, now.AddMinutes(-9), now.AddMinutes(-2))
            { Punish = PunishKind.Lines, Size = 5, ArrivedAt = now.AddMinutes(-8), SeenAt = now.AddMinutes(-2) },
        new LeashSentItem("l2", LeashItemKind.Assign, "vex", "0123456789abcde0", LeashStep.Arrived, now.AddMinutes(-30), now.AddMinutes(-29))
            { Assign = AssignKind.Minutes, Size = 20, ArrivedAt = now.AddMinutes(-29) },
        new LeashSentItem("l3", LeashItemKind.Tug, "vex", null, LeashStep.Refused, now.AddHours(-2), now.AddHours(-2))
            { Refusal = LeashSendStatus.Dnd },
    };

    internal static IBrush StepBrush(LeashStep s) => s switch
    {
        LeashStep.Sent => F.Dim,
        LeashStep.Arrived or LeashStep.Skipped => F.Lilac,
        LeashStep.Seen => F.Gold,
        LeashStep.Done or LeashStep.Accepted => F.Mint,
        _ => F.Red,
    };

    private static IconKind ItemIcon(LeashSentItem i) => i.Kind switch
    {
        LeashItemKind.Punish => i.Punish switch
        {
            PunishKind.Lines => IconKind.LockClosed,
            PunishKind.Pink => IconKind.Eye,
            PunishKind.Bubbles => IconKind.BubbleMultiple,
            PunishKind.Detention => IconKind.Clock,
            PunishKind.Video => IconKind.Play,
            _ => IconKind.Flash,
        },
        LeashItemKind.Assign => IconKind.TaskList,
        LeashItemKind.Reward => IconKind.Star,
        LeashItemKind.Tug => IconKind.HandRight,
        _ => IconKind.Link,
    };

    private static Control Row(LeashSentItem i, DateTimeOffset now)
    {
        var accent = StepBrush(i.Step);
        var g = new Grid
        {
            Margin = new Thickness(0, 2, 0, 2), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Background = Brushes.Transparent,
            Tag = $"leash-sent-row:{i.Kind.ToString().ToLowerInvariant()}:{i.Step.ToString().ToLowerInvariant()}",
        };
        ToolTip.SetTip(g, LeashUiRules.Timeline(i, now, Loc.Get));

        var ic = new IconGlyph { Foreground = F.Muted, Kind = ItemIcon(i), Size = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(ic);

        var what = F.Label(LeashUiRules.ItemText(i, Loc.Get), 11.5, F.Text);
        Grid.SetColumn(what, 1);
        g.Children.Add(what);

        var lit = LeashUiRules.Lit(i);
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-steps" };
        for (int k = 0; k < lit.Length; k++)
        {
            dots.Children.Add(new Ellipse
            {
                Width = 6, Height = 6, Margin = new Thickness(k == 0 ? 0 : 3, 0, 0, 0), StrokeThickness = 1,
                Fill = lit[k] ? accent : Brushes.Transparent, Stroke = lit[k] ? accent : F.Line2,
                Tag = $"leash-step-dot:{k}:{(lit[k] ? "on" : "off")}",
            });
        }
        var word = F.Label(Loc.Get(LeashUiRules.StepKey(i.Step)), 10.5, accent, F.Mono, FontWeight.SemiBold);
        (word.Margin, word.Tag) = (new Thickness(6, 0, 0, 0), "leash-step-word");
        var when = F.Label(LeashUiRules.StepTime(i.StepAt, now), 10, F.Dim, F.Mono);
        when.Margin = new Thickness(5, 0, 0, 0);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Children = { dots, word, when } };
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }
}
