using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
        foreach (var fire in fires.Take(5))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(new TextBlock { Text = $"\"{fire.Keyword}\"", FontWeight = FontWeight.SemiBold, Foreground = Brushes.White });
            foreach (var key in fire.ActionKeys)
            {
                IconKind? icon = key.Split(':')[0] switch
                {
                    "PlayAudio" => IconKind.Speaker2, "Highlight" => IconKind.Eye, "Haptic" => IconKind.PhoneVibrate,
                    "AvatarComment" => IconKind.Chat, "ExtendSession" => IconKind.Timer, "ChasterAddTime" => IconKind.Key,
                    "VisualEffect" when Enum.TryParse<KeywordVisualEffect>(key.Split(':').Last(), out var effect)
                        => AwarenessPresetDetailDialog.IconForVisualEffect(effect),
                    _ => null,
                };
                if (icon is not { } kind) continue;
                var chip = new IconGlyph { Kind = kind, Size = 13 };
                ToolTip.SetTip(chip, key); ToolTip.SetShowDelay(chip, 200);
                row.Children.Add(chip);
            }
            row.Children.Add(new TextBlock { Text = fire.Source.ToUpperInvariant(), FontSize = 9 });
            var age = DateTime.Now - fire.FiredAt;
            row.Children.Add(new TextBlock { Text = age.TotalSeconds < 10 ? "just now" : age.TotalSeconds < 60 ? $"{(int)age.TotalSeconds}s ago" :
                age.TotalMinutes < 60 ? $"{(int)age.TotalMinutes}m ago" : age.TotalHours < 24 ? $"{(int)age.TotalHours}h ago" : fire.FiredAt.ToString("HH:mm"), FontSize = 11 });
            AwarenessPulseFeed.Children.Add(new Border { Child = row, Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#2A2A40")),
                Background = this.FindResource("DarkerBgBrush") as IBrush });
        }
    }
}
