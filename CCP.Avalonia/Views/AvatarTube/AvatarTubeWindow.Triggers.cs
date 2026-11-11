using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _triggerTimer;
        private DispatcherTimer? _triggerFirstTimer;
        private DispatcherTimer? _triggerLeadTimer;
        private string? _pendingTrigger;
        private DispatcherTimer? _triggerTypewriter;
        private string _triggerText = "";
        private string _triggerOriginal = "";
        private int _triggerTextIndex;

        // WPF Speech.cs:1631-1671: enabled at construction, repeat at the configured
        // interval, and a first tick after two seconds. Replacing a timer retires its first tick.
        internal TimeSpan? TriggerInterval => _triggerTimer?.Interval;
        internal TimeSpan? FirstTriggerInterval => _triggerFirstTimer?.Interval;
        internal TimeSpan? TriggerBubbleInterval => _speechTimer?.Interval;
        internal Action<string> TriggerHaptic = text => _ = CoreHaptics.Service?.TriggerSubliminalPatternAsync(text);

        internal void RestartTriggerTimer()
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(RestartTriggerTimer); return; }
            StopTriggerTimer();
            if (!CoreSettings.Current.TriggerModeEnabled)
            {
                var wasPending = _pendingTrigger != null;
                CancelTriggerLead();
                if (wasPending) ProcessNextSpeech();
                return;
            }
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CoreSettings.Current.TriggerIntervalSeconds) };
            _triggerTimer.Tick += (_, _) => OnTriggerTick();
            _triggerTimer.Start();
            _triggerFirstTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _triggerFirstTimer.Tick += (_, _) => { _triggerFirstTimer?.Stop(); _triggerFirstTimer = null; OnTriggerTick(); };
            _triggerFirstTimer.Start();
        }

        private void StopTriggerTimer()
        {
            _triggerTimer?.Stop();
            _triggerTimer = null;
            _triggerFirstTimer?.Stop();
            _triggerFirstTimer = null;
        }

        // WPF Speech.cs:2086-2107,2147-2179: no trigger, voice or haptic during
        // panic quiet, no overlap with speech, and haptics even when the avatar is hidden.
        internal void OnTriggerTick()
        {
            if (!CoreSettings.Current.TriggerModeEnabled || PanicAftermath.TriggersSilenced() || !IsSpeechReady()) return;
            var triggers = CoreSettings.Current.CustomTriggers;
            if (triggers == null || triggers.Count == 0) return;
            var trigger = triggers[_random.Next(triggers.Count)];
            TriggerHaptic(trigger);
            if (IsMuted || !CoreSettings.Current.AvatarEnabled ||
                (_isAttached && (!IsVisible || _parentWindow == null || !_parentWindow.IsVisible || _parentWindow.WindowState == WindowState.Minimized)) ||
                Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted)
            {
                _isGiggling = false;
                NoteSpeechEnded(SpeechSource.Trigger, trigger.Length);
                return;
            }
            _isGiggling = true;
            _pendingTrigger = trigger;
            _triggerLeadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.6) };
            _triggerLeadTimer.Tick += (_, _) => FinishTriggerLead();
            _triggerLeadTimer.Start();
        }

        internal void FinishTriggerLead()
        {
            var text = _pendingTrigger;
            CancelTriggerLead();
            if (text == null) return;
            if (!CoreSettings.Current.TriggerModeEnabled || PanicAftermath.TriggersSilenced())
            { ProcessNextSpeech(); return; }
            ShowSpeech(text, false, aiGenerated: false, null, false, preset: true, trigger: true);
        }

        // WPF Speech.cs:2938-2957,2967-3031: the slow typewriter and its reading allowance.
        private static int TriggerStepMs(int length) => Math.Clamp(7000 / Math.Max(1, length), 24, 75);
        private static double TriggerTypewriterSeconds(int length) => length <= 0 ? 0 :
            TriggerStepMs(length) * Math.Ceiling((double)length / Math.Max(1, length / 100)) / 1000.0;

        private void StartTriggerTypewriter(string text)
        {
            StopTriggerTypewriter();
            _triggerOriginal = text;
            _triggerText = Regex.Replace(text, @"\[([^\]]+)\]\((https?://[^\)]+)\)", "$1", RegexOptions.IgnoreCase);
            _triggerTextIndex = 0;
            _txtSpeech.Text = "";
            if (_triggerText.Length == 0) return;
            _triggerTypewriter = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TriggerStepMs(_triggerText.Length)) };
            _triggerTypewriter.Tick += (_, _) => AdvanceTriggerTypewriter();
            _triggerTypewriter.Start();
        }

        internal void AdvanceTriggerTypewriter()
        {
            if (_triggerTypewriter == null) return;
            _triggerTextIndex = Math.Min(_triggerText.Length, _triggerTextIndex + Math.Max(1, _triggerText.Length / 100));
            _txtSpeech.Text = _triggerText.Substring(0, _triggerTextIndex);
            if (_triggerTextIndex == _triggerText.Length)
            {
                StopTriggerTypewriter();
                RenderSpeechLinks(_txtSpeech, _triggerOriginal);
            }
        }

        // WPF Speech.cs:1227-1256: retain the current snapshot until a reload.
        private static Dictionary<string, string> _knownVideoLinks =
            new(Services.HypnotubeDefaultLinks.KnownVideoTitles, StringComparer.OrdinalIgnoreCase);

        internal static void ReloadVideoLinks()
        {
            var pool = CoreMods.Service?.GetVideoLinks();
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (pool != null && pool.Count > 0)
            {
                foreach (var pair in pool)
                    if (!string.IsNullOrEmpty(pair.Key) && Uri.TryCreate(pair.Value, UriKind.Absolute, out var uri) &&
                        uri.Scheme == Uri.UriSchemeHttps)
                        current[pair.Key] = pair.Value;
            }
            else
                foreach (var pair in Services.HypnotubeDefaultLinks.KnownVideoTitles)
                    current[pair.Key] = pair.Value;
            _knownVideoLinks = current;
        }

        // WPF Speech.cs:1270-1455: markdown first, then longest known titles,
        // fuzzy candidates and raw URLs. The launcher owns the click guard.
        private void RenderSpeechLinks(TextBlock target, string text)
        {
            var hadMarkdown = Regex.IsMatch(text, @"\[([^\]]+)\]\(([^)]+)\)");
            var markdown = new List<(string Label, string Url)>();
            text = Regex.Replace(text, @"\[([^\]]+)\]\(([^)]+)\)", m =>
            {
                var label = m.Groups[1].Value;
                var url = m.Groups[2].Value.Trim();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    markdown.Add((label, url));
                return label;
            });
            text = Regex.Replace(text, @"\s*\[Url\]|\s*\(url\)", "", RegexOptions.IgnoreCase);

            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pool = CoreMods.Service?.GetVideoLinks();
            if (pool != null)
                foreach (var pair in pool)
                    if (!string.IsNullOrEmpty(pair.Key) && Uri.TryCreate(pair.Value, UriKind.Absolute, out var uri) &&
                        uri.Scheme == Uri.UriSchemeHttps)
                        table[pair.Key] = pair.Value;
            foreach (var pair in _knownVideoLinks)
                table[pair.Key] = pair.Value;
            foreach (var pair in Services.HypnotubeDefaultLinks.KnownVideoTitles)
                table.TryAdd(pair.Key, pair.Value);

            var positions = new List<(int Start, int Length, string Name, string Url)>();
            bool Overlaps(int start, int length) => positions.Any(p =>
                start < p.Start + p.Length && start + length > p.Start);
            foreach (var (label, url) in markdown)
            {
                var start = text.IndexOf(label, StringComparison.Ordinal);
                if (start >= 0 && !Overlaps(start, label.Length))
                    positions.Add((start, label.Length, label, url));
            }
            foreach (var pair in table.OrderByDescending(p => p.Key.Length))
            {
                var start = text.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase);
                if (start >= 0 && !Overlaps(start, pair.Key.Length))
                    positions.Add((start, pair.Key.Length, pair.Key, pair.Value));
            }
            var entries = table.Select(p => (Title: p.Key, Url: p.Value)).ToList();
            foreach (var (start, length, _) in Services.Companion.CompanionTitleMatcher.CandidateSpans(text))
            {
                if (length < Services.Companion.CompanionTitleMatcher.MinSpanLength || Overlaps(start, length)) continue;
                var match = Services.Companion.CompanionTitleMatcher.BestFuzzy(text.Substring(start, length), entries);
                if (match != null) positions.Add((start, length, match.Value.Title, match.Value.Url));
            }
            foreach (Match match in Regex.Matches(text, @"https?://[^\s,""'<>]+", RegexOptions.IgnoreCase))
                if (!Overlaps(match.Index, match.Length) && Uri.TryCreate(match.Value, UriKind.Absolute, out _))
                    positions.Add((match.Index, match.Length, match.Value, match.Value));

            if (positions.Count == 0)
            {
                if (!hadMarkdown) target.Text = text;
                else { target.Text = null; target.Inlines = new InlineCollection { new Run(text) }; }
                return;
            }
            var inlines = new InlineCollection();
            var last = 0;
            foreach (var (start, length, name, url) in positions.OrderBy(p => p.Start))
            {
                if (start > last) inlines.Add(new Run(text.Substring(last, start - last)));
                var actual = text.Substring(start, length);
                var label = actual;
                if (actual.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    label = table.FirstOrDefault(p => string.Equals(p.Value, url, StringComparison.OrdinalIgnoreCase)).Key
                        ?? global::ConditioningControlPanel.Helpers.HtUrlHelper.DeriveTitleFromUrl(url);
                else if (!string.Equals(name, actual, StringComparison.OrdinalIgnoreCase))
                    label = name;
                var link = new Button
                {
                    Theme = (ControlTheme)Resources["SpeechInlineLink"]!,
                    Content = new TextBlock { Text = label, FontSize = target.FontSize,
                        FontWeight = target.FontWeight, Foreground = new SolidColorBrush(Color.FromRgb(255, 176, 224)),
                        TextDecorations = TextDecorations.Underline, TextWrapping = TextWrapping.Wrap },
                    Command = CompanionLinkLauncher.CommandFor(url)
                };
                // Inline controls measure atomically. Constrain their own wrapping text to the
                // current line width, including after the bubble or history row is resized.
                link.Bind(Layoutable.MaxWidthProperty, new Binding(nameof(Bounds))
                {
                    Source = target,
                    Converter = new FuncValueConverter<Rect, double>(bounds => bounds.Width > 0
                        ? Math.Max(1, bounds.Width - target.Padding.Left - target.Padding.Right) : 320)
                });
                inlines.Add(new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.Baseline });
                last = start + length;
            }
            if (last < text.Length) inlines.Add(new Run(text.Substring(last)));
            target.Text = null;
            target.Inlines = inlines;
        }

        private void ChatHistoryText_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBlock target && target.Tag is string text)
                RenderSpeechLinks(target, text);
        }

        private void StopTriggerTypewriter()
        {
            _triggerTypewriter?.Stop();
            _triggerTypewriter = null;
        }

        private void CancelTriggerLead()
        {
            _triggerLeadTimer?.Stop();
            _triggerLeadTimer = null;
            if (_pendingTrigger != null) _isGiggling = false;
            _pendingTrigger = null;
        }
    }
}
