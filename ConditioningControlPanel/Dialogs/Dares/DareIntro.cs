using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Dialogs.Dares;

/// <summary>
/// The first-time card: four picture steps, a word under each, one button (DARES-HANDOFF "an intro
/// infographic card the first time someone opens Dares"). Shown inside whichever Dares window opens
/// first (the slip or an offer), once, remembered as a marker file beside the settings. Drawn on the
/// same pink slip as the composer, the steps as the tray's coloured discs.
/// </summary>
internal static class DareIntro
{
    internal static Func<bool> Seen { get; set; } = () =>
    {
        try { return File.Exists(MarkerPath); } catch { return true; }
    };

    internal static Action MarkSeen { get; set; } = () =>
    {
        try { File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("o")); } catch { }
    };

    private static string MarkerPath => Path.Combine(App.UserDataPath, "dares_intro_seen.flag");

    /// <summary>The steps as (tone, word key). A slip, they accept, CCP counts, the loser pays.</summary>
    internal static readonly (DareTone Tone, string Key)[] Steps =
    {
        (DareTone.Dare, "dares_intro_1"),
        (DareTone.Duel, "dares_intro_2"),
        (DareTone.Minutes, "dares_intro_3"),
        (DareTone.Sp, "dares_intro_4"),
    };

    public static FrameworkElement Build(Action onDone)
    {
        var sp = new StackPanel { Tag = "dares-intro" };
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        for (int i = 0; i < Steps.Length * 2 - 1; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        for (int i = 0; i < Steps.Length; i++)
        {
            var (tone, key) = Steps[i];
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var disc = DareLook.Disc(tone);
            disc.HorizontalAlignment = HorizontalAlignment.Center;
            cell.Children.Add(disc);
            var n = DareLook.Hand((i + 1).ToString(), 24, DareLook.InkBrush, center: true);
            n.Margin = new Thickness(0, 6, 0, 2);
            cell.Children.Add(n);
            var w = DareLook.Words(Loc.Get(key), 19, DareLook.PrintBrush, wrap: true);
            w.TextAlignment = TextAlignment.Center;
            w.HorizontalAlignment = HorizontalAlignment.Center;
            w.MaxWidth = 110;
            cell.Children.Add(w);
            Grid.SetColumn(cell, i * 2);
            row.Children.Add(cell);
            DareLook.CardIn(cell);
            if (i < Steps.Length - 1)
            {
                var arrow = DareLook.Icon("arrow", 22, DareLook.PrintBrush);
                arrow.Opacity = 0.55;
                arrow.VerticalAlignment = VerticalAlignment.Top;
                arrow.Margin = new Thickness(2, 12, 2, 0);
                Grid.SetColumn(arrow, i * 2 + 1);
                row.Children.Add(arrow);
            }
        }
        sp.Children.Add(DareLook.Slip(DareLook.SlipHead(Loc.Get("dares_intro_title"), null, null), row, out _));
        var go = DareLook.Big(Loc.Get("dares_intro_go"), DareBig.Go, () => { MarkSeen(); onDone(); }, "dares-intro-go");
        go.Margin = new Thickness(0, 18, 0, 0);
        sp.Children.Add(go);
        return sp;
    }
}
