// WPF AvatarTubeWindow.Speech.cs:1631,2086,2147: trigger timer and panic quiet.
using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Avalonia.Controls;
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
                        if (index == plain.Length) { _triggerTypewriter?.Stop(); _triggerTypewriter = null; TriggerTypewriterStep = null; }
                    };
                    var tick = TriggerTypewriterStep;
                    _triggerTypewriter.Tick += (_, _) => tick?.Invoke();
                    _triggerTypewriter.Start();
                }
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

        internal static int TriggerStepMs(int length) => Math.Clamp(7000 / Math.Max(1, length), 24, 75);
        internal static double TriggerDuration(int length) => Math.Clamp(5.0 + length * 0.05, 5.0, 14.0)
            + (length <= 0 ? 0 : TriggerStepMs(length) * Math.Ceiling((double)length / Math.Max(1, length / 100)) / 1000.0);

        private void CancelTriggerPresentation()
        {
            _triggerGeneration++;
            _cancelTriggerLead();
            _cancelTriggerLead = static () => { };
            _triggerTypewriter?.Stop();
            _triggerTypewriter = null;
            TriggerTypewriterStep = null;
        }
    }
}
