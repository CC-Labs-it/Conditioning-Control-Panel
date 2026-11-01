// WPF AvatarTubeWindow.Speech.cs:2936-3033: the slow preset bubble typewriter.
using System;
using System.Text.RegularExpressions;
using Avalonia;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Views.Controls.Companion;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private static readonly Regex PresetLinks = new(@"\[([^\]]+)\]\((https?://[^\)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private DispatcherTimer? _typewriterTimer;
        private int _typewriterGeneration;
        // As with ThinkingFrame, headless tests can advance the same callback as the timer.
        internal Action? TypewriterFrame { get; private set; }
        internal TimeSpan? TypewriterInterval => _typewriterTimer?.Interval;
        internal TimeSpan? SpeechBubbleInterval => _speechTimer?.Interval;

        private static int PresetTypewriterStepMs(int length) => Math.Clamp(7000 / Math.Max(1, length), 24, 75);
        private static int PresetTypewriterDurationMs(int length)
        {
            if (length <= 0) return 0;
            int perTick = Math.Max(1, length / 100);
            return PresetTypewriterStepMs(length) * (int)Math.Ceiling((double)length / perTick);
        }

        private void StartPresetTypewriter(string text)
        {
            StopTypewriter();
            var plain = PresetLinks.Replace(text, "$1");
            _txtSpeech.Inlines?.Clear();
            _txtSpeech.Text = "";
            if (plain.Length == 0) return;
            int index = 0;
            var generation = ++_typewriterGeneration;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PresetTypewriterStepMs(plain.Length)) };
            _typewriterTimer = timer;
            TypewriterFrame = () =>
            {
                if (generation != _typewriterGeneration) return;
                index = Math.Min(plain.Length, index + Math.Max(1, plain.Length / 100));
                _txtSpeech.Text = plain.Substring(0, index);
                if (index == plain.Length) { StopTypewriter(); RenderPresetLinks(text); }
            };
            var frame = TypewriterFrame;
            timer.Tick += (_, _) => frame();
            timer.Start();
        }

        // WPF Speech.cs:1277-1313,1417-1455,3031-3033: restore markdown URLs after typing.
        private void RenderPresetLinks(string text)
        {
            var matches = PresetLinks.Matches(text);
            if (matches.Count == 0) return;
            _txtSpeech.Text = null;
            var inlines = _txtSpeech.Inlines!;
            inlines.Clear();
            int end = 0;
            foreach (Match match in matches)
            {
                inlines.Add(new Run(text.Substring(end, match.Index - end)));
                var label = match.Groups[1].Value;
                var url = match.Groups[2].Value;
                if (CompanionLinkNavigation.TryBrowserUri(url, out _))
                    inlines.Add(new InlineUIContainer(new SafeHyperlinkButton
                    { Content = label, Padding = new Thickness(0), FontFamily = _txtSpeech.FontFamily,
                      FontSize = _txtSpeech.FontSize, FontWeight = _txtSpeech.FontWeight,
                      Foreground = new SolidColorBrush(Color.FromRgb(255, 176, 224)), Command = CompanionLinkLauncher.CommandFor(url) }));
                else inlines.Add(new Run(label));
                end = match.Index + match.Length;
            }
            inlines.Add(new Run(text.Substring(end)));
        }

        private void StopTypewriter()
        {
            _typewriterGeneration++;
            _typewriterTimer?.Stop();
            _typewriterTimer = null;
            TypewriterFrame = null;
        }
    }
}
