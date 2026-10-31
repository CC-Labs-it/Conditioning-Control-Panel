// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs (WPF 7.2): Trigger mode -
// StartTriggerTimer :1630, StopTriggerTimer :1656, RestartTriggerTimer :1666, OnTriggerTick :2086,
// ShowTriggerBubble :2112 and ShowTriggerBubbleImmediate :2152. Deviations: no lead-in or slow
// typewriter (neither ported to this tube, see ShowSpeech), and Stop also cancels the 2 s first tick.

using System;
using System.ComponentModel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Avalonia.Threading;
using Avalonia.Controls;
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
            SubliminalWhisperShow.PlayTriggerAudio(trigger);
            ShowSpeech(trigger, playSound: false, aiGenerated: false, null, false, preset: true, trigger: true);
        }

        // WPF Windowing.cs:2210-2233. The head's parent visibility handler also hides the dock.
        private bool IsTriggerAvatarVisible => CoreSettings.Current.AvatarEnabled && IsVisible
            && WindowState != WindowState.Minimized
            && (!_isAttached || _parentWindow is { IsVisible: true, WindowState: not WindowState.Minimized });

        /// <summary>WPF: 5 s plus 0.05 s a character, clamped to 5-14 s.</summary>
        internal static double TriggerDisplaySeconds(string trigger) => Math.Clamp(5.0 + trigger.Length * 0.05, 5.0, 14.0);
    }
}
