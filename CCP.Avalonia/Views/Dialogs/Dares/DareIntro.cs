using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;

/// <summary>WPF Dialogs/Dares/DareIntro.cs: the first-time card, four picture steps and one button, shown
/// once inside the first Dares window that opens, remembered as a marker file beside the settings.</summary>
internal static class DareIntro
{
    private static string MarkerPath => Path.Combine(CorePaths.UserData, "dares_intro_seen.flag");

    internal static Func<bool> Seen { get; set; } = () =>
    {
        try { return File.Exists(MarkerPath); } catch { return true; }
    };

    internal static Action MarkSeen { get; set; } = () =>
    {
        try { File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("o")); } catch { }
    };

    /// <summary>WPF :30-36: a slip, they accept, CCP counts, the loser pays.</summary>
    internal static readonly (DareTone Tone, string Key)[] Steps =
    {
        (DareTone.Dare, "dares_intro_1"), (DareTone.Duel, "dares_intro_2"), (DareTone.Minutes, "dares_intro_3"), (DareTone.Sp, "dares_intro_4"),
    };

    public static Control Build(Action onDone)
    {
        var sp = new StackPanel { Tag = "dares-intro" };
        var row = new Grid { Margin = new Thickness(0, 6) };
        for (int i = 0; i < Steps.Length * 2 - 1; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition(i % 2 == 0 ? GridLength.Star : GridLength.Auto));
        for (int i = 0; i < Steps.Length; i++)
        {
            var (tone, key) = Steps[i];
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var disc = DareLook.Disc(tone);
            disc.HorizontalAlignment = HorizontalAlignment.Center;
            cell.Children.Add(disc);
            var n = DareLook.Hand((i + 1).ToString(), 24, DareLook.InkBrush);
            n.Margin = new Thickness(0, 6, 0, 2);
            cell.Children.Add(n);
            var w = DareLook.Words(Loc.Get(key), 19, DareLook.PrintBrush);
            w.TextAlignment = TextAlignment.Center;
            w.MaxWidth = 110;
            w.Margin = default;
            cell.Children.Add(w);
            Grid.SetColumn(cell, i * 2);
            row.Children.Add(cell);
            DareLook.CardIn(cell);
            if (i < Steps.Length - 1)
            {
                var arrow = DareLook.Icon(IconKind.ArrowRight, 22, DareLook.PrintBrush);
                arrow.Opacity = 0.55;
                arrow.VerticalAlignment = VerticalAlignment.Top;
                arrow.Margin = new Thickness(2, 12, 2, 0);
                Grid.SetColumn(arrow, i * 2 + 1);
                row.Children.Add(arrow);
            }
        }
        sp.Children.Add(DareLook.Slip(DareLook.SlipHead(Loc.Get("dares_intro_title"), null, null), row, out _));
        var go = DareLook.Act(Loc.Get("dares_intro_go"), null, true, () => { MarkSeen(); onDone(); }, "dares-intro-go");
        go.Margin = new Thickness(0, 18, 0, 0);
        sp.Children.Add(go);
        return sp;
    }
}
