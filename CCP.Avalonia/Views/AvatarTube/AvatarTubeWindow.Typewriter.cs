// WPF AvatarTubeWindow.Speech.cs:2936-3033: the slow preset bubble typewriter.
using System;
using System.Text.RegularExpressions;
using Avalonia.Threading;

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
                if (index == plain.Length) StopTypewriter();
            };
            var frame = TypewriterFrame;
            timer.Tick += (_, _) => frame();
            timer.Start();
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
