// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs:133-368 - the "Last Detected"
// pulse feed, fed by KeywordTriggerHead.Engine's fire ring buffer. WPF's emoji chips are IconGlyphs here.

using System;
using System.Collections.Generic;
using Avalonia;
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
        private bool _pulseSubscribed;
        internal Platform.KeywordAudioRuntime PulseSource = Platform.KeywordTriggerHead.Engine;   // test seam

        /// <summary>WPF subscribes once (SyncAwarenessTabUI :86); this view detaches with its window (P41).</summary>
        private void AttachPulseFeed()
        {
            if (_pulseSubscribed) return;
            PulseSource.TriggerFired += OnAwarenessTriggerFired;
            _pulseSubscribed = true;
        }

        private void DetachPulseFeed()
        {
            PulseSource.TriggerFired -= OnAwarenessTriggerFired;
            _pulseSubscribed = false;
        }

        private void OnAwarenessTriggerFired(KeywordTrigger _) =>
            Dispatcher.UIThread.Post(() => { if (IsVisible) RefreshAwarenessPulseFeed(); });

        /// <summary>WPF RefreshAwarenessPulseFeed :144: up to five rows, newest first.</summary>
        internal void RefreshAwarenessPulseFeed()
        {
            var fires = PulseSource.GetRecentFires();
            AwarenessPulseFeed.Children.Clear();
            if (fires.Count == 0)
            {
                AwarenessPulseFeed.Children.Add(TxtAwarenessPulseEmpty);
                TxtAwarenessFireCount.Text = "";
                return;
            }
            for (var i = 0; i < fires.Count && i < 5; i++) AwarenessPulseFeed.Children.Add(BuildPulseRow(fires[i]));
            TxtAwarenessFireCount.Text = fires.Count == 1 ? "1 fire today" : $"{fires.Count} fires today";
        }

        private Border BuildPulseRow(TriggerFireRecord f)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("6,Auto,Auto,*,Auto,Auto") };

            var bar = new Rectangle { Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5, VerticalAlignment = VerticalAlignment.Center };
            bar.Bind(Shape.FillProperty, bar.GetResourceObservable("PinkBrush"));
            grid.Children.Add(bar);

            var keyword = new TextBlock { Text = $"\"{f.Keyword}\"", Foreground = Brushes.White, FontSize = 13,
                FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(keyword, 1);
            grid.Children.Add(keyword);

            var chips = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            foreach (var key in f.ActionKeys ?? new List<string>())
            {
                if (ActionChip(key) is not { } chip) continue;
                var glyph = IconGlyph.Of(chip.Kind, size: 13);
                glyph.Margin = new Thickness(0, 0, 4, 0);
                ToolTip.SetTip(glyph, chip.Tip);
                ToolTip.SetShowDelay(glyph, 200);
                global::Avalonia.Automation.AutomationProperties.SetName(glyph, chip.Tip);
                chips.Children.Add(glyph);
            }
            Grid.SetColumn(chips, 2);
            grid.Children.Add(chips);

            var sourceText = new TextBlock { Text = f.Source.ToUpperInvariant(), FontSize = 9, FontWeight = FontWeight.Bold };
            sourceText.Bind(TextBlock.ForegroundProperty, sourceText.GetResourceObservable("TextDimBrush"));
            var source = new Border { Background = new SolidColorBrush(Color.Parse("#2A2A4A")), CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Child = sourceText };
            Grid.SetColumn(source, 4);
            grid.Children.Add(source);

            var time = new TextBlock { Text = FormatTimeAgo(f.FiredAt, DateTime.Now), FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            time.Bind(TextBlock.ForegroundProperty, time.GetResourceObservable("TextMutedBrush"));
            Grid.SetColumn(time, 5);
            grid.Children.Add(time);

            var row = new Border { BorderBrush = new SolidColorBrush(Color.Parse("#2A2A40")), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Child = grid };
            row.Bind(Border.BackgroundProperty, row.GetResourceObservable("DarkerBgBrush"));
            return row;
        }

        /// <summary>WPF GetActionChipDisplay :325 (same tooltips); unknown keys draw nothing.</summary>
        internal static (IconKind Kind, string Tip)? ActionChip(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
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

        /// <summary>WPF FormatTimeAgo :360.</summary>
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
