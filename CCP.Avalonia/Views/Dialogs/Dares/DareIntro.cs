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

// WPF 1468acc21b Dialogs/Dares/DareIntro.cs:16-95.
internal static class DareIntro
{
    internal static string MarkerPath => Path.Combine(CorePaths.UserData, "dares_intro_seen.flag");
    internal static bool Seen() { try { return File.Exists(MarkerPath); } catch { return true; } }
    internal static void MarkSeen() { try { File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("o")); } catch { } }

    internal static Control Build(Action done)
    {
        var body = new StackPanel { Tag = "dares-intro", Spacing = 12 };
        body.Children.Add(new TextBlock { Text = Loc.Get("dares_intro_title"), FontSize = 22, FontWeight = FontWeight.Bold });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*,Auto,*,Auto,*") };
        var tones = new[] { DareTone.Dare, DareTone.Duel, DareTone.Minutes, DareTone.Sp };
        for (int i = 0; i < tones.Length; i++)
        {
            var color = DareLook.ToneColor(tones[i]);
            var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4 };
            cell.Children.Add(new Border
            {
                Width = 58, Height = 58, CornerRadius = new CornerRadius(29),
                Background = new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color), BorderThickness = new Thickness(2),
                Child = new TextBlock { Text = DareLook.ToneGlyph(tones[i]), FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 24, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center }
            });
            cell.Children.Add(new TextBlock { Text = (i + 1).ToString(), HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(new TextBlock { Text = Loc.Get("dares_intro_" + (i + 1)), MaxWidth = 90,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            Grid.SetColumn(cell, i * 2); row.Children.Add(cell);
            if (i < 3) { var arrow = new TextBlock { Text = "→", VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(arrow, i * 2 + 1); row.Children.Add(arrow); }
        }
        body.Children.Add(row);
        var go = DareLook.Choice(Loc.Get("dares_intro_go"), DareLook.ToneColor(DareTone.Duel), null, done, "dares-intro-go");
        go.HorizontalAlignment = HorizontalAlignment.Center;
        body.Children.Add(go);
        return body;
    }
}
