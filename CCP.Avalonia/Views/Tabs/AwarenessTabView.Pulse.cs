using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AwarenessTabView
{
    private void InitializePulseFeed()
    {
        AttachedToVisualTree += (_, _) => KeywordTriggerHead.Engine.TriggerFired += OnKeywordFired;
        DetachedFromVisualTree += (_, _) => KeywordTriggerHead.Engine.TriggerFired -= OnKeywordFired;
    }

    private void OnKeywordFired(KeywordTrigger _) { if (IsVisible) RefreshPulseFeed(); }

    internal void RefreshPulseFeed()
    {
        var fires = KeywordTriggerHead.Engine.RecentFires;
        AwarenessPulseFeed.Children.Clear();
        TxtAwarenessFireCount.Text = fires.Count == 0 ? "" : fires.Count == 1 ? "1 fire today" : $"{fires.Count} fires today";
        if (fires.Count == 0) { AwarenessPulseFeed.Children.Add(TxtAwarenessPulseEmpty); return; }
        foreach (var fire in fires.Take(5)) AwarenessPulseFeed.Children.Add(BuildPulseRow(fire));
    }

    /// <summary>WPF MainWindow.Awareness.cs:187 BuildPulseRow: accent bar, keyword, chips, filler, source chip, time.</summary>
    private Border BuildPulseRow(TriggerFireRecord fire)
    {
        IBrush? Res(string key) => this.FindResource(key) as IBrush;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("6,Auto,Auto,*,Auto,Auto") };
        void Add(Control c, int column) { Grid.SetColumn(c, column); grid.Children.Add(c); }
        Add(new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Background = Res("PinkBrush"),
            VerticalAlignment = VerticalAlignment.Center }, 0);
        Add(new TextBlock { Text = $"\"{fire.Keyword}\"", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }, 1);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        foreach (var key in fire.ActionKeys)
        {
            if (ActionChip(key) is not { } display) continue;
            var chip = new IconGlyph { Kind = display.Icon, Size = 13, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(chip, display.Tip); ToolTip.SetShowDelay(chip, 200);
            AutomationProperties.SetName(chip, display.Tip);
            chips.Children.Add(chip);
        }
        Add(chips, 2);
        Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#2A2A4A")), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2),
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = fire.Source.ToUpperInvariant(), Foreground = Res("TextDimBrush"), FontSize = 9, FontWeight = FontWeight.Bold },
        }, 4);
        var age = DateTime.Now - fire.FiredAt;
        Add(new TextBlock { Text = age.TotalSeconds < 10 ? "just now" : age.TotalSeconds < 60 ? $"{(int)age.TotalSeconds}s ago" :
            age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m ago" : age.TotalHours < 24 ? $"{(int)age.TotalHours}h ago" : fire.FiredAt.ToString("HH:mm"),
            Foreground = Res("TextMutedBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }, 5);
        return new Border { Child = grid, Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#2A2A40")),
            Background = Res("DarkerBgBrush") };
    }

    /// <summary>WPF MainWindow.Awareness.cs:323 GetActionChipDisplay (icon + English tooltip); null skips the key.</summary>
    internal static (IconKind Icon, string Tip)? ActionChip(string key)
    {
        var colon = key.IndexOf(':');
        var type = colon < 0 ? key : key[..colon];
        var arg = colon < 0 ? "" : key[(colon + 1)..];
        return type switch
        {
            "PlayAudio" => (IconKind.Speaker2, "Plays an audio clip when the word is detected"),
            "Highlight" => (IconKind.Eye, "Draws a glowing box around the matched word on screen (OCR matches only)"),
            "Haptic" => (IconKind.PhoneVibrate, "Fires a haptic vibration pattern on connected devices"),
            "AvatarComment" => (IconKind.Chat, "Makes the avatar comment on the matched word (AI + canned fallback)"),
            "ExtendSession" => (IconKind.Timer, arg == "" ? "Extends the current session" : $"Extends the current session by {arg} minutes"),
            "ChasterAddTime" => (IconKind.LockClosed, arg == "" ? "Adds time to the Chaster lock" : $"Adds {arg} minutes to the Chaster lock"),
            "VisualEffect" => (Enum.TryParse<KeywordVisualEffect>(arg, out var effect) ? AwarenessPresetDetailDialog.IconForVisualEffect(effect) : IconKind.Sparkle,
                arg switch
                {
                    "SubliminalFlash" => "Flashes a random word from your subliminal pool",
                    "ExactSubliminal" => "Flashes the matched keyword itself as subliminal text",
                    "ImageFlash" => "Fires a flash burst image when the word is detected",
                    "OverlayPulse" => "Briefly intensifies the screen overlay",
                    "MindWipe" => "Triggers the MindWipe effect",
                    "Bubbles" => "Spawns bubbles on screen",
                    _ => $"Fires visual effect: {arg}",
                }),
            _ => null,
        };
    }
}
