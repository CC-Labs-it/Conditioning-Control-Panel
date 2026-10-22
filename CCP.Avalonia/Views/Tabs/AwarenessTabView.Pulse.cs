// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs (OnAwarenessTriggerFired,
// RefreshAwarenessPulseFeed, BuildPulseRow, BuildActionChipStrip, GetActionChipDisplay, FormatTimeAgo).
// The fires come from the keyword runtime (Platform.KeywordTriggerHead.Engine) through Core KeywordFireLog.
// WPF draws one emoji per action; this head draws the matching Fluent icon (docs/avalonia-decisions.md).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AwarenessTabView
    {
        private void OnAwarenessTriggerFired(KeywordTrigger trigger)
        {
            // WPF BeginInvokes and refreshes only while the tab is showing.
            Dispatcher.UIThread.Post(() =>
            {
                if (IsEffectivelyVisible) RefreshAwarenessPulseFeed();
            });
        }

        /// <summary>WPF RefreshAwarenessPulseFeed: the five newest fires, or the empty line; count of all kept.</summary>
        internal void RefreshAwarenessPulseFeed()
        {
            var fires = Platform.KeywordTriggerHead.Engine.Fires.Recent();
            AwarenessPulseFeed.Children.Clear();

            if (fires.Count == 0)
            {
                AwarenessPulseFeed.Children.Add(TxtAwarenessPulseEmpty);
                TxtAwarenessFireCount.Text = "";
                return;
            }

            var now = Platform.KeywordTriggerHead.Engine.Now();
            for (var i = 0; i < fires.Count && i < 5; i++)
                AwarenessPulseFeed.Children.Add(BuildPulseRow(fires[i], now));

            TxtAwarenessFireCount.Text = fires.Count == 1 ? "1 fire today" : $"{fires.Count} fires today";
        }

        private Border BuildPulseRow(TriggerFireRecord f, DateTime now)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("6,Auto,Auto,*,Auto,Auto") };

            var bar = new Rectangle
            {
                Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5,
                Fill = Res("PinkBrush", Brushes.HotPink),
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(bar);

            var keyword = new TextBlock
            {
                Text = $"\"{f.Keyword}\"",
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };
            Grid.SetColumn(keyword, 1);
            grid.Children.Add(keyword);

            var chips = BuildActionChipStrip(f.ActionKeys);
            Grid.SetColumn(chips, 2);
            grid.Children.Add(chips);

            var source = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2A2A4A")),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = f.Source.ToUpperInvariant(),
                    Foreground = Res("TextDimBrush", Brushes.Gray),
                    FontSize = 9,
                    FontWeight = FontWeight.Bold,
                },
            };
            Grid.SetColumn(source, 4);
            grid.Children.Add(source);

            var time = new TextBlock
            {
                Text = FormatTimeAgo(f.FiredAt, now),
                Foreground = Res("TextMutedBrush", Brushes.Gray),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };
            Grid.SetColumn(time, 5);
            grid.Children.Add(time);

            return new Border
            {
                Background = Res("DarkerBgBrush", Brushes.Black),
                BorderBrush = new SolidColorBrush(Color.Parse("#2A2A40")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Child = grid,
            };
        }

        /// <summary>WPF BuildActionChipStrip: one icon per action, tooltip after a 200 ms hover.</summary>
        private static StackPanel BuildActionChipStrip(List<string>? actionKeys)
        {
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            };
            if (actionKeys == null) return strip;

            foreach (var key in actionKeys)
            {
                if (ActionChip(key) is not { } chip) continue;
                var glyph = IconGlyph.Of(chip.Icon, size: 13);
                glyph.Margin = new Thickness(0, 0, 4, 0);
                ToolTip.SetTip(glyph, chip.Tooltip);
                ToolTip.SetShowDelay(glyph, 200);
                AutomationProperties.SetName(glyph, chip.Tooltip);
                strip.Children.Add(glyph);
            }
            return strip;
        }

        /// <summary>
        /// WPF GetActionChipDisplay with each emoji swapped for its Fluent icon. Keys come from Core
        /// KeywordFireLog.ActionKeys ("PlayAudio", "VisualEffect:ImageFlash", "ExtendSession:5"); unknown
        /// keys return null so the strip skips them.
        /// </summary>
        internal static (IconKind Icon, string Tooltip)? ActionChip(string? key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var colon = key.IndexOf(':');
            var type = colon < 0 ? key : key.Substring(0, colon);
            var arg = colon < 0 ? "" : key.Substring(colon + 1);

            return type switch
            {
                "PlayAudio" => (IconKind.Speaker2, "Plays an audio clip when the word is detected"),
                "Highlight" => (IconKind.Eye, "Draws a glowing box around the matched word on screen (OCR matches only)"),
                "Haptic" => (IconKind.PhoneVibrate, "Fires a haptic vibration pattern on connected devices"),
                "AvatarComment" => (IconKind.Chat, "Makes the avatar comment on the matched word (AI + canned fallback)"),
                "ExtendSession" => (IconKind.Timer, string.IsNullOrEmpty(arg)
                    ? "Extends the current session"
                    : $"Extends the current session by {arg} minutes"),
                "ChasterAddTime" => (IconKind.LockClosed, string.IsNullOrEmpty(arg)
                    ? "Adds time to the Chaster lock"
                    : $"Adds {arg} minutes to the Chaster lock"),
                "VisualEffect" => arg switch
                {
                    "SubliminalFlash" => (IconKind.Sparkle, "Flashes a random word from your subliminal pool"),
                    "ExactSubliminal" => (IconKind.TextFont, "Flashes the matched keyword itself as subliminal text"),
                    "ImageFlash" => (IconKind.Flash, "Fires a flash burst image when the word is detected"),
                    "OverlayPulse" => (IconKind.WeatherFog, "Briefly intensifies the screen overlay"),
                    "MindWipe" => (IconKind.BrainCircuit, "Triggers the MindWipe effect"),
                    "Bubbles" => (IconKind.BubbleMultiple, "Spawns bubbles on screen"),
                    _ => (IconKind.Sparkle, $"Fires visual effect: {arg}"),
                },
                _ => null,
            };
        }

        /// <summary>WPF FormatTimeAgo.</summary>
        internal static string FormatTimeAgo(DateTime t, DateTime now)
        {
            var delta = now - t;
            if (delta.TotalSeconds < 10) return "just now";
            if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
            if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
            return t.ToString("HH:mm");
        }
    }
}
