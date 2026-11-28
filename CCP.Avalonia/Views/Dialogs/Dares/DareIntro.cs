using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;

// WPF 1468acc21b Dialogs/Dares/DareIntro.cs:16-95: four discs on the pink slip, the step number in
// handwriting, a word under each, an arrow between, one button.
internal static class DareIntro
{
    internal static string MarkerPath => Path.Combine(CorePaths.UserData, "dares_intro_seen.flag");
    internal static bool Seen() { try { return File.Exists(MarkerPath); } catch { return true; } }
    internal static void MarkSeen() { try { File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("o")); } catch { } }

    private static readonly (DareTone Tone, string Key)[] Steps =
        { (DareTone.Dare, "dares_intro_1"), (DareTone.Duel, "dares_intro_2"), (DareTone.Minutes, "dares_intro_3"), (DareTone.Sp, "dares_intro_4") };

    internal static Control Build(Action done)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto,*,Auto,*"), Margin = new Thickness(0, 6, 0, 6) };
        for (int i = 0; i < Steps.Length; i++)
        {
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Children = {
                DareSlipLook.Disc(Steps[i].Tone).Also(d => d.HorizontalAlignment = HorizontalAlignment.Center),
                DareSlipLook.Words((i + 1).ToString(), 24, DareSlipLook.Ink, hand: true).Also(n => { n.HorizontalAlignment = HorizontalAlignment.Center; n.Margin = new Thickness(0, 6, 0, 2); }),
                DareSlipLook.Words(Loc.Get(Steps[i].Key), 19, DareSlipLook.Print, wrap: true).Also(w => { w.TextAlignment = TextAlignment.Center; w.HorizontalAlignment = HorizontalAlignment.Center; w.MaxWidth = 110; }) } };
            Grid.SetColumn(cell, i * 2);
            row.Children.Add(cell);
            DareSlipLook.CardIn(cell);
            if (i == Steps.Length - 1) continue;
            var arrow = new IconGlyph { Kind = IconKind.ArrowRight, Size = 22, Foreground = DareSlipLook.Print, Opacity = 0.55,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 12, 2, 0) };
            Grid.SetColumn(arrow, i * 2 + 1);
            row.Children.Add(arrow);
        }
        var go = DareSlipLook.Big(Loc.Get("dares_intro_go"), done, "dares-intro-go").Also(g => { g.Margin = new Thickness(0, 18, 0, 0); g.HorizontalAlignment = HorizontalAlignment.Center; });
        return new StackPanel { Tag = "dares-intro", Children = { DareSlipLook.Slip(DareSlipLook.SlipHead(Loc.Get("dares_intro_title"), null), row, out _), go } };
    }
}
