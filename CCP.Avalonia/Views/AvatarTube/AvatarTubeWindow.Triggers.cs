using System;
using System.Text.RegularExpressions;
using Avalonia.Controls;
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
            if (_triggerTextIndex == _triggerText.Length) StopTriggerTypewriter();
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
