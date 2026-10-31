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
using ConditioningControlPanel.Localization;
namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AwarenessTabView
    {
        private bool _pulseSubscribed;
        internal Platform.KeywordAudioRuntime PulseSource = Platform.KeywordTriggerHead.Engine;   // test seam
        private void AttachPulseFeed()
        {
            if (_pulseSubscribed) return;
            PulseSource.TriggerFired += OnAwarenessTriggerFired;
            LocalizationManager.Instance.LanguageChanged += OnPulseLanguageChanged;
            _pulseSubscribed = true;
        }
        private void DetachPulseFeed()
        {
            PulseSource.TriggerFired -= OnAwarenessTriggerFired;
            LocalizationManager.Instance.LanguageChanged -= OnPulseLanguageChanged;
            _pulseSubscribed = false;
        }
        private void OnAwarenessTriggerFired(KeywordTrigger _) =>
            Dispatcher.UIThread.Post(() => { if (IsVisible) RefreshAwarenessPulseFeed(); });
        private void OnPulseLanguageChanged(object? sender, EventArgs e) => RefreshAwarenessPulseFeed();
        internal void RefreshAwarenessPulseFeed()
        {
            var fires = PulseSource.GetRecentFires();
            AwarenessPulseFeed.Children.Clear();
            if (fires.Count == 0)
            {
                TxtAwarenessPulseEmpty.Text = Loc.Get("awareness_pulse_empty");
                AwarenessPulseFeed.Children.Add(TxtAwarenessPulseEmpty);
                TxtAwarenessFireCount.Text = "";
                return;
            }
            for (var i = 0; i < fires.Count && i < 5; i++) AwarenessPulseFeed.Children.Add(BuildPulseRow(fires[i]));
            TxtAwarenessFireCount.Text = Loc.GetF("awareness_pulse_count", fires.Count);
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
            var sourceText = new TextBlock { Text = (f.Source == "Keyboard" ? Loc.Get("awareness_pulse_keyboard") : f.Source).ToUpperInvariant(), FontSize = 9, FontWeight = FontWeight.Bold };
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
        internal static (IconKind Kind, string Tip)? ActionChip(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var colon = key.IndexOf(':');
            var type = colon < 0 ? key : key[..colon];
            var arg = colon < 0 ? "" : key[(colon + 1)..];
            return type switch
            {
                "PlayAudio" => (IconKind.Speaker2, Loc.Get("deeper_friendly_action_play_audio")),
                "Highlight" => (IconKind.Eye, Loc.Get("tooltip_show_a_glowing_overlay_over_the_word_when_ocr")),
                "Haptic" => (IconKind.PhoneVibrate, Loc.Get("deeper_friendly_action_trigger_haptic")),
                "AvatarComment" => (IconKind.Chat, Loc.Get("awareness_pulse_avatar")),
                "ExtendSession" => (IconKind.Timer, Loc.GetF("awareness_pulse_extend", arg)),
                "ChasterAddTime" => (IconKind.LockClosed, Loc.GetF("awareness_pulse_chaster", arg)),
                "VisualEffect" => arg switch
                {
                    "SubliminalFlash" => (IconKind.Sparkle, Loc.Get("kwt_effect_subliminal")),
                    "ExactSubliminal" => (IconKind.TextFont, Loc.Get("kwt_effect_exact_subliminal")),
                    "ImageFlash" => (IconKind.Flash, Loc.Get("kwt_effect_image_flash")),
                    "OverlayPulse" => (IconKind.WeatherFog, Loc.Get("kwt_effect_overlay_pulse")),
                    "MindWipe" => (IconKind.BrainCircuit, Loc.Get("kwt_effect_mind_wipe")),
                    "Bubbles" => (IconKind.BubbleMultiple, Loc.Get("kwt_effect_bubbles")),
                    _ => (IconKind.Sparkle, Loc.Get("deeper_friendly_action_trigger_effect")),
                },
                _ => null,
            };
        }
        internal static string FormatTimeAgo(DateTime t, DateTime now)
        {
            var delta = now - t;
            if (delta.TotalSeconds < 10) return Loc.Get("companion_chat_time_now");
            if (delta.TotalSeconds < 60) return Loc.GetF("awareness_pulse_seconds", (int)delta.TotalSeconds);
            if (delta.TotalMinutes < 60) return Loc.GetF("companion_chat_time_minutes", (int)delta.TotalMinutes);
            if (delta.TotalHours < 24) return Loc.GetF("companion_chat_time_hours", (int)delta.TotalHours);
            return t.ToString("HH:mm");
        }
    }
}
