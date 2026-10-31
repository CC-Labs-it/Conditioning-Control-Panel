// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs (WPF 7.2): Trigger mode -
// StartTriggerTimer :1630, StopTriggerTimer :1656, RestartTriggerTimer :1666, OnTriggerTick :2086,
// ShowTriggerBubble :2110 and ShowTriggerBubbleImmediate :2147. Lead-in and slow typing follow
// Speech.cs:2173-2247,2938-3042. Stop also cancels the 2 s first tick.

using System;
using System.ComponentModel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private bool _triggersAttached;
        private AppSettings? _triggerSettings;
        private SettingsService? _triggerService;

        // Observe settings so Workshop, quick menu and restored profiles use the same running loop.
        private void AttachTriggerSettings()
        {
            _triggersAttached = true;
            _triggerService = CoreSettings.Service;
            if (_triggerService != null) _triggerService.CurrentReplaced += OnTriggerSettingsReplaced;
            OnTriggerSettingsReplaced();
        }

        private void OnTriggerSettingsReplaced() => RunOnAvatar(() =>
        {
            if (!_triggersAttached) return;
            if (_triggerSettings != null) _triggerSettings.PropertyChanged -= OnTriggerSettingChanged;
            _triggerSettings = CoreSettings.Current;
            _triggerSettings.PropertyChanged += OnTriggerSettingChanged;
            RestartTriggerTimer();
        });

        private void OnTriggerSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or "" or nameof(AppSettings.TriggerModeEnabled)
                or nameof(AppSettings.TriggerIntervalSeconds)) RestartTriggerTimer();
        }

        private void DetachTriggerSettings()
        {
            _triggersAttached = false;
            if (_triggerService != null) _triggerService.CurrentReplaced -= OnTriggerSettingsReplaced;
            if (_triggerSettings != null) _triggerSettings.PropertyChanged -= OnTriggerSettingChanged;
            _triggerService = null;
            _triggerSettings = null;
            StopTriggerTimer();
        }

        private Action? _stopTriggerLoop;
        private Action _cancelFirstTrigger = static () => { };

        /// <summary>Runs <c>tick</c> every interval until the returned stop; tests swap in a manual one (P08).</summary>
        internal Func<TimeSpan, Action, Action> TriggerLoopScheduler = static (interval, tick) =>
        {
            var t = new DispatcherTimer { Interval = interval };
            t.Tick += (_, _) => tick();
            t.Start();
            return t.Stop;
        };

        internal bool TriggerLoopRunning => _stopTriggerLoop != null;

        private void StartTriggerTimer()
        {
            var s = CoreSettings.Current;
            if (!s.TriggerModeEnabled) { Log.Debug("TriggerMode: Not enabled, skipping timer start"); return; }
            var interval = s.TriggerIntervalSeconds;
            _stopTriggerLoop = TriggerLoopScheduler(TimeSpan.FromSeconds(interval), OnTriggerTick);
            Log.Information("TriggerMode: Started with {Interval}s interval", interval);
            // WPF: the first trigger shows 2 s after start rather than a whole interval later.
            _cancelFirstTrigger = SpeechDelayScheduler(TimeSpan.FromSeconds(2), OnTriggerTick);
        }

        private void StopTriggerTimer()
        {
            _cancelFirstTrigger();
            _cancelFirstTrigger = static () => { };
            _stopTriggerLoop?.Invoke();
            _stopTriggerLoop = null;
        }

        /// <summary>WPF RestartTriggerTimer: call when Trigger mode or its interval changes.</summary>
        public void RestartTriggerTimer() => RunOnAvatar(() =>
        {
            StopTriggerTimer();
            if (_triggersAttached) StartTriggerTimer();
        });

        /// <summary>WPF OnTriggerTick: a random custom trigger, unless a line is up or its gap is running.</summary>
        internal void OnTriggerTick()
        {
            if (!_triggersAttached || !CoreSettings.Current.TriggerModeEnabled || PanicAftermath.TriggersSilenced() || !IsSpeechReady()) return;
            var triggers = CoreSettings.Current.CustomTriggers;
            if (triggers == null || triggers.Count == 0) { Log.Debug("TriggerMode: No triggers configured"); return; }
            ShowTriggerBubble(triggers[_random.Next(triggers.Count)]);
        }

        private void ShowTriggerBubble(string trigger) => RunOnAvatar(() =>
        {
            if (IsMuted) { ShowTriggerBubbleImmediate(trigger); return; }   // haptic only, below
            if (_isGiggling || !IsSpeechReady())
            {
                _speechQueue.Enqueue((trigger, SpeechSource.Trigger));
                if (!_isGiggling) { _isGiggling = true; ProcessNextSpeech(); }
                return;
            }
            ShowTriggerBubbleImmediate(trigger);
        });

        /// <summary>The haptic always fires; word, voice and bubble only when she is shown and unmuted.</summary>
        private void ShowTriggerBubbleImmediate(string trigger)
        {
            if (PanicAftermath.TriggersSilenced()) { _isGiggling = false; ProcessNextSpeech(); return; }
            _ = CoreHaptics.Service?.TriggerSubliminalPatternAsync(trigger);
            if (IsMuted || !IsTriggerAvatarVisible || Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted)
            {
                _isGiggling = false;
                NoteSpeechEnded(SpeechSource.Trigger, trigger.Length);
                Log.Information("TriggerMode: Haptic only (avatar not visible)");
                return;
            }
            CancelTriggerAnimation();
            _isGiggling = true;
            var generation = _triggerAnimationGeneration;
            _cancelTriggerAnimation = SpeechDelayScheduler(TimeSpan.FromSeconds(0.6), () =>
            {
                if (generation != _triggerAnimationGeneration || !_triggersAttached) return;
                if (PanicAftermath.TriggersSilenced()) { PanicSilence(); return; }
                SubliminalWhisperShow.PlayTriggerAudio(trigger);
                ShowSpeech(trigger, playSound: false, aiGenerated: false, null, false, preset: true, trigger: true);
                // WPF strips markdown links for intermediate frames; the final pass uses the full text.
                var plain = System.Text.RegularExpressions.Regex.Replace(trigger,
                    @"\[([^\]]+)\]\((https?://[^\)]+)\)", "$1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                _txtSpeech.Text = "";
                if (plain.Length == 0) { RenderTriggerText(trigger); return; }
                var index = 0;
                _cancelTriggerAnimation = TriggerLoopScheduler(TimeSpan.FromMilliseconds(TriggerTypewriterStepMs(plain.Length)), () =>
                {
                    if (generation != _triggerAnimationGeneration || !_triggersAttached) return;
                    if (PanicAftermath.TriggersSilenced()) { PanicSilence(); return; }
                    index = Math.Min(plain.Length, index + Math.Max(1, plain.Length / 100));
                    if (index == plain.Length) { _cancelTriggerAnimation(); RenderTriggerText(trigger); }
                    else _txtSpeech.Text = plain[..index];
                });
            });
        }

        // WPF Speech.cs:1278-1295,1448-1455,3033: final labels become clickable HTTP(S) links.
        private void RenderTriggerText(string text)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(text,
                @"\[([^\]]+)\]\(([^)]+)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (matches.Count == 0) { _txtSpeech.Text = text; return; }
            _txtSpeech.Text = "";
            _txtSpeech.Inlines!.Clear();
            var at = 0;
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                _txtSpeech.Inlines!.Add(new Run(text[at..match.Index]));
                var label = match.Groups[1].Value;
                if (Uri.TryCreate(match.Groups[2].Value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                    _txtSpeech.Inlines.Add(new InlineUIContainer(new global::ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton
                    {
                        Content = label, Padding = new(0), FontSize = _txtSpeech.FontSize,
                        Foreground = global::Avalonia.Media.Brush.Parse("#ffb0e0"),
                        Command = Controls.Companion.CompanionLinkLauncher.CommandFor(uri.AbsoluteUri)
                    }));
                else _txtSpeech.Inlines.Add(new Run(label));
                at = match.Index + match.Length;
            }
            _txtSpeech.Inlines!.Add(new Run(text[at..]));
        }

        // WPF Windowing.cs:2210-2233. The head's parent visibility handler also hides the dock.
        private bool IsTriggerAvatarVisible => CoreSettings.Current.AvatarEnabled && IsVisible
            && WindowState != WindowState.Minimized
            && (!_isAttached || _parentWindow is { IsVisible: true, WindowState: not WindowState.Minimized });

        private int _triggerAnimationGeneration;
        private Action _cancelTriggerAnimation = static () => { };
        private void CancelTriggerAnimation()
        {
            _triggerAnimationGeneration++;
            _cancelTriggerAnimation();
            _cancelTriggerAnimation = static () => { };
        }

        // WPF Speech.cs:2938-2965: slow profile, batched ticks, plus readable time after typing.
        private static int TriggerTypewriterStepMs(int length) => Math.Clamp(7000 / Math.Max(1, length), 24, 75);
        internal static int TriggerTypingMs(int length) => length <= 0 ? 0 :
            TriggerTypewriterStepMs(length) * (int)Math.Ceiling((double)length / Math.Max(1, length / 100));
        internal TimeSpan? SpeechHideDelay => _speechTimer?.Interval;
        internal static double TriggerDisplaySeconds(string trigger) =>
            Math.Clamp(5.0 + trigger.Length * 0.05, 5.0, 14.0) + TriggerTypingMs(trigger.Length) / 1000.0;
    }
}
