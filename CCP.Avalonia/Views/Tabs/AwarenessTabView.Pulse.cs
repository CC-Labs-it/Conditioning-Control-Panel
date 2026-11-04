using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AwarenessTabView
{
    internal KeywordAudioRuntime PulseEngine { get; set; } = KeywordTriggerHead.Engine;

    private void AttachPulse()
    {
        PulseEngine.TriggerFired += OnPulseFire;
        RefreshPulse();
    }

    private void DetachPulse() => PulseEngine.TriggerFired -= OnPulseFire;
    private void OnPulseFire(KeywordTrigger _) => Dispatcher.UIThread.Post(RefreshPulse);

    internal void RefreshPulse()
    {
        var fires = PulseEngine.Fires.Recent();
        AwarenessPulseFeed.Children.Clear();
        if (fires.Count == 0)
        {
            AwarenessPulseFeed.Children.Add(TxtAwarenessPulseEmpty);
            TxtAwarenessFireCount.Text = "";
            return;
        }
        foreach (var fire in fires.Take(5))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 6) };
            row.Children.Add(new TextBlock { Text = $"\"{fire.Keyword}\"", FontWeight = FontWeight.SemiBold });
            foreach (var key in fire.ActionKeys)
            {
                var icon = PulseIcon(key);
                if (icon.Length == 0) continue;
                var chip = new TextBlock { Text = icon };
                ToolTip.SetTip(chip, key.Replace(':', ' '));
                row.Children.Add(chip);
            }
            row.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#2A2A4A")), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), Child = new TextBlock { Text = fire.Source.ToUpperInvariant(), FontSize = 9 } });
            var delta = DateTime.Now - fire.FiredAt;
            var time = delta.TotalSeconds < 10 ? "just now" : delta.TotalSeconds < 60 ? $"{(int)delta.TotalSeconds}s ago" : delta.TotalMinutes < 60 ? $"{(int)delta.TotalMinutes}m ago" : delta.TotalHours < 24 ? $"{(int)delta.TotalHours}h ago" : fire.FiredAt.ToString("HH:mm");
            row.Children.Add(new TextBlock { Text = time, FontSize = 11 });
            AwarenessPulseFeed.Children.Add(row);
        }
        TxtAwarenessFireCount.Text = fires.Count == 1 ? "1 fire today" : $"{fires.Count} fires today";
    }

    private static string PulseIcon(string key) => key.Split(':')[0] switch
    {
        "PlayAudio" => "🔊", "Highlight" => "👁", "Haptic" => "💥", "AvatarComment" => "💬",
        "ExtendSession" => "⏱", "ChasterAddTime" => "🔒",
        "VisualEffect" => key.Split(':').Last() switch
        {
            "ExactSubliminal" => "🔤", "ImageFlash" => "⚡", "OverlayPulse" => "🌫", "MindWipe" => "🧠", "Bubbles" => "🫧", _ => "✨"
        },
        _ => ""
    };
}
