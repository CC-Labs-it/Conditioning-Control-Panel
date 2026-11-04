using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AwarenessTabView
{
    internal KeywordAudioRuntime PulseEngine { get; set; } = KeywordTriggerHead.Engine;

    private void AttachPulse()
    {
        PulseEngine.TriggerFired += OnPulseFire;
        LocalizationManager.Instance.LanguageChanged += OnPulseLanguageChanged;
        RefreshPulse();
    }

    private void DetachPulse()
    {
        PulseEngine.TriggerFired -= OnPulseFire;
        LocalizationManager.Instance.LanguageChanged -= OnPulseLanguageChanged;
    }
    private void OnPulseLanguageChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshPulse);
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
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("6,Auto,Auto,*,Auto,Auto") };
            var accent = new Rectangle { Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5,
                Fill = this.FindResource("PinkBrush") as IBrush, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(accent);
            var keyword = new TextBlock { Text = $"\"{fire.Keyword}\"", Foreground = Brushes.White,
                FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(keyword, 1); grid.Children.Add(keyword);
            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0) };
            foreach (var key in fire.ActionKeys)
            {
                var (icon, tip) = PulseChip(key);
                if (icon.Length == 0) continue;
                var chip = new TextBlock { Text = icon, FontSize = 13, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0) };
                ToolTip.SetTip(chip, tip);
                ToolTip.SetShowDelay(chip, 200);
                ToolTip.SetBetweenShowDelay(chip, 0);
                chips.Children.Add(chip);
            }
            Grid.SetColumn(chips, 2); grid.Children.Add(chips);
            var source = new Border { Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 2), Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = fire.Source.ToUpperInvariant(), FontSize = 9, FontWeight = FontWeight.Bold,
                    Foreground = this.FindResource("TextDimBrush") as IBrush } };
            Grid.SetColumn(source, 4); grid.Children.Add(source);
            var time = FormatPulseTime(fire.FiredAt, DateTime.Now);
            var timeBlock = new TextBlock { Text = time, FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0), Foreground = this.FindResource("TextMutedBrush") as IBrush };
            Grid.SetColumn(timeBlock, 5); grid.Children.Add(timeBlock);
            AwarenessPulseFeed.Children.Add(new Border { Background = this.FindResource("DarkerBgBrush") as IBrush,
                BorderBrush = new SolidColorBrush(Color.Parse("#2A2A40")), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 0, 6), Child = grid });
        }
        TxtAwarenessFireCount.Text = FormatPulseCount(fires.Count);
    }

    internal static string FormatPulseCount(int count) => count == 1
        ? Loc.Get("pulse_count_one") : Loc.GetF("pulse_count_many", count);

    internal static string FormatPulseTime(DateTime firedAt, DateTime now)
    {
        var delta = now - firedAt;
        if (delta.TotalSeconds < 10) return Loc.Get("pulse_time_now");
        if (delta.TotalSeconds < 60) return Loc.GetF("pulse_time_seconds", (int)delta.TotalSeconds);
        if (delta.TotalMinutes < 60) return Loc.GetF("pulse_time_minutes", (int)delta.TotalMinutes);
        if (delta.TotalHours < 24) return Loc.GetF("pulse_time_hours", (int)delta.TotalHours);
        return firedAt.ToString("HH:mm");
    }

    internal static (string icon, string tooltip) PulseChip(string key)
    {
        var colon = key.IndexOf(':');
        var type = colon < 0 ? key : key[..colon];
        var arg = colon < 0 ? "" : key[(colon + 1)..];
        return type switch
        {
            "PlayAudio" => ("🔊", Loc.Get("pulse_tip_audio")),
            "Highlight" => ("👁", Loc.Get("pulse_tip_highlight")),
            "Haptic" => ("💥", Loc.Get("pulse_tip_haptic")),
            "AvatarComment" => ("💬", Loc.Get("pulse_tip_avatar")),
            "ExtendSession" => ("⏱", arg.Length == 0 ? Loc.Get("pulse_tip_extend") : Loc.GetF("pulse_tip_extend_minutes", arg)),
            "ChasterAddTime" => ("🔒", arg.Length == 0 ? Loc.Get("pulse_tip_chaster") : Loc.GetF("pulse_tip_chaster_minutes", arg)),
            "VisualEffect" => arg switch
            {
                "SubliminalFlash" => ("✨", Loc.Get("pulse_tip_subliminal")),
                "ExactSubliminal" => ("🔤", Loc.Get("pulse_tip_exact")),
                "ImageFlash" => ("⚡", Loc.Get("pulse_tip_image")),
                "OverlayPulse" => ("🌫", Loc.Get("pulse_tip_overlay")),
                "MindWipe" => ("🧠", Loc.Get("pulse_tip_mindwipe")),
                "Bubbles" => ("🫧", Loc.Get("pulse_tip_bubbles")),
                _ => ("✨", Loc.GetF("pulse_tip_effect", arg)),
            },
            _ => ("", "")
        };
    }
}
