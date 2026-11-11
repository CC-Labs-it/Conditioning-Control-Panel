// WPF AvatarTubeWindow.Speech.cs:1631,2086,2147: trigger timer and panic quiet.
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _triggerTimer;
        private Models.AppSettings? _triggerSettings;
        internal double? TriggerInterval => _triggerTimer?.Interval.TotalSeconds;
        private Action _cancelFirstTrigger = static () => { };
        private int _firstTriggerGeneration;
        private Action _cancelTriggerLead = static () => { };
        private DispatcherTimer? _triggerTypewriter;
        internal Action? TriggerTypewriterStep;
        internal double? TriggerDisplayInterval => _speechTimer?.Interval.TotalSeconds;
        private int _triggerGeneration;
        private bool _triggerHasLinks;
        internal Func<bool>? TriggerVisibilityOverride;
        internal Action<string> TriggerAudio = Overlays.SubliminalWhisperShow.PlayTriggerAudio;
        internal Action<string> TriggerHaptic = text => { _ = CoreHaptics.Service?.TriggerSubliminalPatternAsync(text); };

        internal void StartTriggerMode()
        {
            _triggerSettings = CoreSettings.Current;
            _triggerSettings.PropertyChanged += TriggerSettingsChanged;
            RestartTriggerTimer();
        }

        internal void StopTriggerMode()
        {
            if (_triggerSettings != null) _triggerSettings.PropertyChanged -= TriggerSettingsChanged;
            _triggerSettings = null;
            _triggerTimer?.Stop();
            _triggerTimer = null;
            _firstTriggerGeneration++;
            _cancelFirstTrigger();
            _cancelFirstTrigger = static () => { };
            CancelTriggerPresentation();
        }

        private void TriggerSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.TriggerModeEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.TriggerIntervalSeconds))
                Dispatcher.UIThread.Post(RestartTriggerTimer);
        }

        internal void RestartTriggerTimer()
        {
            _triggerTimer?.Stop();
            _triggerTimer = null;
            _firstTriggerGeneration++;
            _cancelFirstTrigger();
            _cancelFirstTrigger = static () => { };
            if (_triggerSettings?.TriggerModeEnabled != true) return;
            _triggerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_triggerSettings.TriggerIntervalSeconds) };
            _triggerTimer.Tick += (_, _) => TriggerTick();
            _triggerTimer.Start();
            var generation = _firstTriggerGeneration;
            _cancelFirstTrigger = SpeechDelayScheduler(TimeSpan.FromSeconds(2), () =>
            {
                if (generation == _firstTriggerGeneration) TriggerTick();
            });
        }

        internal void TriggerTick()
        {
            if (_triggerSettings?.TriggerModeEnabled != true || PanicAftermath.TriggersSilenced() || !IsSpeechReady()) return;
            var triggers = _triggerSettings.CustomTriggers;
            if (triggers == null || triggers.Count == 0) return;
            ShowTrigger(triggers[_random.Next(triggers.Count)]);
        }

        // WPF Windowing.cs:2210: a detached tube does not depend on its parent.
        internal static bool TriggerVisible(bool enabled, bool visible, bool attached, bool parentVisible, bool parentMinimized, bool fullscreen) =>
            enabled && visible && (!attached || (parentVisible && !parentMinimized && !fullscreen));

        internal void ShowTrigger(string text)
        {
            if (PanicAftermath.TriggersSilenced()) return;
            TriggerHaptic(text);
            bool visible = TriggerVisibilityOverride?.Invoke() ?? TriggerVisible(CoreSettings.Current.AvatarEnabled, IsVisible, _isAttached,
                _parentWindow?.IsVisible == true, _parentWindow?.WindowState == WindowState.Minimized,
                _isAttached && IsVisible && Platform.Win32Windows.OtherAppExclusiveFullscreen());
            if (IsMuted || !visible || Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted)
            {
                NoteSpeechEnded(SpeechSource.Trigger, text.Length);
                return;
            }
            CancelTriggerPresentation();
            _isGiggling = true;
            var generation = _triggerGeneration;
            _cancelTriggerLead = SpeechDelayScheduler(TimeSpan.FromSeconds(0.6), () =>
            {
                if (generation != _triggerGeneration || !_isGiggling) return;
                if (PanicAftermath.TriggersSilenced()) { _isGiggling = false; ProcessNextSpeech(); return; }
                HideListeningBubble();
                SyncAskButtonsFor(text);
                TriggerAudio(text);
                if (_isShowingChatHistory)
                {
                    _isShowingChatHistory = false;
                    _chatHistoryView.IsVisible = false;
                    _speechScroller.IsVisible = true;
                }
                _aiBadge.IsVisible = false;
                _policyBadge.IsVisible = false;
                _isShowingAiBubble = false;
                _currentSpeechSource = SpeechSource.Trigger;
                _currentSpeechLength = text.Length;
                var plain = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
                _txtSpeech.Text = "";
                _speechBubble.MaxWidth = 380;
                ApplySpeechBubblePlacement();
                _speechBubble.IsVisible = true;
                var step = TriggerStepMs(plain.Length);
                var index = 0;
                if (plain.Length > 0)
                {
                    _triggerTypewriter = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(step) };
                    TriggerTypewriterStep = () =>
                    {
                        if (generation != _triggerGeneration) return;
                        index = Math.Min(plain.Length, index + Math.Max(1, plain.Length / 100));
                        _txtSpeech.Text = plain[..index];
                        if (index == plain.Length) { _triggerTypewriter?.Stop(); _triggerTypewriter = null; TriggerTypewriterStep = null; RenderTriggerLinks(text); }
                    };
                    var tick = TriggerTypewriterStep;
                    _triggerTypewriter.Tick += (_, _) => tick?.Invoke();
                    _triggerTypewriter.Start();
                }
                else RenderTriggerLinks(text);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(TriggerDuration(text.Length)) };
                DateTime? hover = null;
                timer.Tick += (_, _) =>
                {
                    hover ??= DateTime.UtcNow;
                    if (Services.Companion.SpeechLatchRule.HoldForHover(_isMouseOverSpeechBubble, DateTime.UtcNow - hover.Value))
                    { timer.Interval = TimeSpan.FromSeconds(1); return; }
                    if (_speechTimer == timer) EndSpeechBubble(); else timer.Stop();
                };
                _speechTimer?.Stop();
                _speechTimer = timer;
                timer.Start();
            });
        }

        // WPF Speech.cs:1259,3030: restore links once the slow typewriter has finished.
        private void RenderTriggerLinks(string text)
        {
            var titles = new List<(string Name, string Url)>();
            if (CoreMods.Service?.GetVideoLinks() is { } pool) titles.AddRange(pool.Select(pair => (pair.Key, pair.Value)));
            titles.AddRange(Services.BambiSprite.ContentCatalogue);
            titles.AddRange(Services.HypnotubeDefaultLinks.KnownVideoTitles.Select(pair => (pair.Key, pair.Value)));
            titles = titles.Where(x => !string.IsNullOrWhiteSpace(x.Name) && IsWebUrl(x.Url))
                .OrderByDescending(x => x.Name.Length).ToList();
            var parts = new List<(string Text, string? Url)>();
            void Plain(string part)
            {
                while (part.Length > 0)
                {
                    int first = int.MaxValue; (string Name, string Url) found = default;
                    foreach (var title in titles)
                    {
                        int at = part.IndexOf(title.Name, StringComparison.OrdinalIgnoreCase);
                        if (at >= 0 && at < first) { first = at; found = title; }
                    }
                    if (first == int.MaxValue) { parts.Add((part, null)); break; }
                    if (first > 0) parts.Add((part[..first], null));
                    parts.Add((part.Substring(first, found.Name.Length), found.Url));
                    part = part[(first + found.Name.Length)..];
                }
            }
            int previous = 0;
            foreach (Match match in Regex.Matches(text, @"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)|(?<raw>https?://[^\s,""'<>]+)", RegexOptions.IgnoreCase))
            {
                Plain(text[previous..match.Index]);
                var raw = match.Groups["raw"].Value;
                var url = raw.Length > 0 ? raw.TrimEnd('.', ',', '!', '?', ';', ':') : match.Groups["url"].Value.Trim();
                var label = raw.Length > 0 ? url : match.Groups["label"].Value;
                parts.Add((label, IsWebUrl(url) ? url : null));
                if (raw.Length > url.Length) Plain(raw[url.Length..]);
                previous = match.Index + match.Length;
            }
            Plain(text[previous..]);
            if (!parts.Any(part => part.Url != null)) return;
            _txtSpeech.Text = null;
            _txtSpeech.Inlines = new InlineCollection();
            foreach (var (label, url) in parts)
                if (url == null) _txtSpeech.Inlines.Add(new Run(label));
                else _txtSpeech.Inlines.Add(new InlineUIContainer(new SafeHyperlinkButton
                { Content = label, Padding = new global::Avalonia.Thickness(0), FontSize = _txtSpeech.FontSize, Command = CompanionLinkLauncher.CommandFor(url) }));
            _triggerHasLinks = true;
        }

        private static bool IsWebUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        internal static int TriggerStepMs(int length) => Math.Clamp(7000 / Math.Max(1, length), 24, 75);
        internal static double TriggerDuration(int length) => Math.Clamp(5.0 + length * 0.05, 5.0, 14.0)
            + (length <= 0 ? 0 : TriggerStepMs(length) * Math.Ceiling((double)length / Math.Max(1, length / 100)) / 1000.0);

        private void CancelTriggerPresentation()
        {
            if (_triggerHasLinks) { _txtSpeech.Inlines?.Clear(); _triggerHasLinks = false; }
            _triggerGeneration++;
            _cancelTriggerLead();
            _cancelTriggerLead = static () => { };
            _triggerTypewriter?.Stop();
            _triggerTypewriter = null;
            TriggerTypewriterStep = null;
        }
    }
}
