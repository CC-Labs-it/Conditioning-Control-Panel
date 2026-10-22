// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs (WPF 7.1.5): Trigger mode -
// StartTriggerTimer :1594, StopTriggerTimer :1620, RestartTriggerTimer :1630, OnTriggerTick :2047,
// ShowTriggerBubble :2068 and ShowTriggerBubbleImmediate :2105. Deviations: no lead-in or slow
// typewriter (neither ported to this tube, see ShowSpeech), and Stop also cancels the 2 s first tick.

using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
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
            StartTriggerTimer();
        });

        /// <summary>WPF OnTriggerTick: a random custom trigger, unless a line is up or its gap is running.</summary>
        internal void OnTriggerTick()
        {
            if (!IsSpeechReady()) return;
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
            _ = CoreHaptics.Service?.TriggerSubliminalPatternAsync(trigger);
            if (IsMuted || !IsVisible || Windows.EmiDesk.EmiDeskService.Instance.AvatarMuted)
            {
                _isGiggling = false;
                NoteSpeechEnded(SpeechSource.Trigger, trigger.Length);
                Log.Information("TriggerMode: Haptic only (avatar not visible)");
                return;
            }
            SubliminalWhisperShow.PlayTriggerAudio(trigger);
            ShowSpeech(trigger, playSound: false, aiGenerated: false, null, false, preset: true, trigger: true);
        }

        /// <summary>WPF: 5 s plus 0.05 s a character, clamped to 5-14 s.</summary>
        internal static double TriggerDisplaySeconds(string trigger) => Math.Clamp(5.0 + trigger.Length * 0.05, 5.0, 14.0);
    }
}
