using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Dialogs.Dares;

/// <summary>
/// The first-time card: four picture steps, a word under each, one button (DARES-HANDOFF "an intro
/// infographic card the first time someone opens Dares"). Shown inside whichever Dares window opens
/// first (the slip or an offer), once, remembered as a marker file beside the settings.
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
        sp.Children.Add(DareLook.Head(Loc.Get("dares_intro_title"), null, null));
        var row = new Grid { Margin = new Thickness(0, 4, 0, 14) };
        for (int i = 0; i < Steps.Length * 2 - 1; i++)
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        for (int i = 0; i < Steps.Length; i++)
        {
            var (tone, key) = Steps[i];
            var c = DareLook.ToneColor(tone);
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var disc = new Border
            {
                Width = 58,
                Height = 58,
                CornerRadius = new CornerRadius(29),
                Background = DareLook.Tint(c, 0x30),
                BorderBrush = FriendsLook.Frozen(c),
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = DareLook.ToneGlyph(tone),
                    FontFamily = DareLook.GlyphFont,
                    FontSize = 24,
                    Foreground = FriendsLook.Frozen(c),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            cell.Children.Add(disc);
            var n = DareLook.Words((i + 1).ToString(), 10, FriendsLook.DimBrush);
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.Margin = new Thickness(0, 4, 0, 0);
            cell.Children.Add(n);
            var w = DareLook.Words(Loc.Get(key), 12.5, FriendsLook.TextBrush, wrap: true);
            w.TextAlignment = TextAlignment.Center;
            w.HorizontalAlignment = HorizontalAlignment.Center;
            w.MaxWidth = 90;
            cell.Children.Add(w);
            Grid.SetColumn(cell, i * 2);
            row.Children.Add(cell);
            DareLook.CardIn(cell);
            if (i < Steps.Length - 1)
            {
                var arrow = DareLook.Glyph("", 12, FriendsLook.DimBrush);   // Forward
                arrow.Margin = new Thickness(2, 0, 2, 30);
                Grid.SetColumn(arrow, i * 2 + 1);
                row.Children.Add(arrow);
            }
        }
        sp.Children.Add(row);
        var go = DareLook.Big(Loc.Get("dares_intro_go"), FriendsLook.Mint, () => { MarkSeen(); onDone(); }, "dares-intro-go");
        go.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(go);
        return sp;
    }
}
