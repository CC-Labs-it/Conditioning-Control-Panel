using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls.Dares;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;

/// <summary>WPF Dialogs/Dares/DareIntro.cs: the first-time card, shown once (a marker file beside the settings).</summary>
internal static class DareIntro
{
    private static string MarkerPath => System.IO.Path.Combine(CorePaths.UserData, "dares_intro_seen.flag");

    internal static Func<bool> Seen { get; set; } = () => { try { return File.Exists(MarkerPath); } catch { return true; } };
    internal static Action MarkSeen { get; set; } = () => { try { File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("o")); } catch { } };

    // WPF :30-36: a slip, they accept, CCP counts, the loser pays.
    internal static readonly (DareTone Tone, string Key)[] Steps =
        [(DareTone.Dare, "dares_intro_1"), (DareTone.Duel, "dares_intro_2"), (DareTone.Minutes, "dares_intro_3"), (DareTone.Sp, "dares_intro_4")];

    // shortcut: numbered tone discs on the plain card shell, not WPF's slip with icons; s19-dares-cards-look ports that.
    public static Control Build(Action onDone)
    {
        var sp = new StackPanel { Tag = "dares-intro" };
        sp.Children.Add(DareLook.Words(Loc.Get("dares_intro_title"), 22));
        var row = new UniformGrid { Columns = Steps.Length, Margin = new Thickness(0, 10, 0, 18) };
        for (int i = 0; i < Steps.Length; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(4, 0) };
            var disc = new Grid { Width = 46, Height = 46, HorizontalAlignment = HorizontalAlignment.Center };
            disc.Children.Add(new Ellipse { Fill = new SolidColorBrush(DareLook.ToneColor(Steps[i].Tone)) });
            disc.Children.Add(new TextBlock { Text = (i + 1).ToString(), FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            cell.Children.Add(disc);
            cell.Children.Add(new TextBlock { Text = Loc.Get(Steps[i].Key), FontSize = 16, Foreground = DareLook.ChalkBrush,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
            row.Children.Add(cell);
        }
        sp.Children.Add(row);
        sp.Children.Add(DareLook.Act(Loc.Get("dares_intro_go"), null, true, () => { MarkSeen(); onDone(); }, "dares-intro-go"));
        return sp;
    }
}
