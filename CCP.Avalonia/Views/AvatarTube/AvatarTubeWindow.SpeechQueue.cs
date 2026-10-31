// PORTED from ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs (WPF 7.1.5): the speech
// QUEUE (:35 _speechQueue, :141 IsSpeechReady, :157 CalculateRequiredDelayAfterLastSpeech,
// :180 ProcessNextSpeech, :217 ShowSpeechBySource) and the hide-tick bookkeeping (:575-596).

using System;
using System.Collections.Generic;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        /// <summary>Where a line came from: decides the gap after it (WPF Speech.cs:71); a queued
        /// Trigger replays through its own bubble (ShowSpeechBySource :217).</summary>
        internal enum SpeechSource { Preset, AI, Trigger }

        // WPF Speech.cs:35: a QUEUED line carries text only - its clip is dropped, as WPF ("audio is
        // still text-only on this delayed path", Speech.cs:283).
        private readonly Queue<(string Text, SpeechSource Source)> _speechQueue = new();
        private DateTime _lastSpeechEndTime = DateTime.MinValue;
        private SpeechSource _lastSpeechSource = SpeechSource.Preset;
        private int _lastSpeechLength;

        // WPF Speech.cs:116-119, verbatim.
        private const double MinSpeechDelaySeconds = 2.0;
        private const double AiSpeechBonusSeconds = 5.0;
        private const int LongTextThreshold = 100;
        private const double PerCharDelaySeconds = 0.02;

        /// <summary>Cancels the pending post-line gap (WPF _speechDelayTimer.Stop). Never null.</summary>
        private Action _cancelSpeechDelay = static () => { };

        /// <summary>Runs <c>fire</c> once after the delay and returns its cancel. A DispatcherTimer, as
        /// WPF; tests swap in a manual one (P08).</summary>
        internal Func<TimeSpan, Action, Action> SpeechDelayScheduler = static (delay, fire) =>
        {
            var t = new DispatcherTimer { Interval = delay };
            t.Tick += (_, _) => { t.Stop(); fire(); };
            t.Start();
            return t.Stop;
        };

        /// <summary>How many lines wait behind the one on screen.</summary>
        internal int QueuedSpeechCount => _speechQueue.Count;

        /// <summary>WPF CalculateRequiredDelayAfterLastSpeech: 2 s, +5 s after an AI line, +0.02 s a
        /// char past 100 - based on the PREVIOUS line, so a long reply gets read before the next.</summary>
        internal static double RequiredDelayAfter(SpeechSource lastSource, int lastLength)
        {
            double delay = MinSpeechDelaySeconds;
            if (lastSource == SpeechSource.AI) delay += AiSpeechBonusSeconds;
            if (lastLength > LongTextThreshold) delay += (lastLength - LongTextThreshold) * PerCharDelaySeconds;
            return delay;
        }

        /// <summary>The clock the gap is measured on; tests step it (P08).</summary>
        internal TimeProvider SpeechClock = TimeProvider.System;

        private DateTime SpeechNow => SpeechClock.GetLocalNow().DateTime;

        private double SecondsUntilSpeechReady() => Math.Max(0,
            RequiredDelayAfter(_lastSpeechSource, _lastSpeechLength) - (SpeechNow - _lastSpeechEndTime).TotalSeconds);

        /// <summary>WPF IsSpeechReady: nothing on screen and the previous line's gap has passed.</summary>
        internal bool IsSpeechReady() => !_isGiggling && SecondsUntilSpeechReady() <= 0;

        /// <summary>WPF ProcessNextSpeech: the next queued line, after the gap the previous one earned.</summary>
        private void ProcessNextSpeech()
        {
            if (_speechQueue.Count == 0) { _isGiggling = false; return; }
            var next = _speechQueue.Dequeue();
            var remaining = SecondsUntilSpeechReady();
            _cancelSpeechDelay();
            _cancelSpeechDelay = static () => { };
            if (remaining > 0)
                _cancelSpeechDelay = SpeechDelayScheduler(TimeSpan.FromSeconds(remaining), () =>
                {
                    _cancelSpeechDelay = static () => { };
                    ShowQueued(next);
                });
            else
                ShowQueued(next);
        }

        /// <summary>WPF ShowSpeechBySource: a trigger gets its own bubble, a preset the giggle cue on every fifth.</summary>
        private void ShowQueued((string Text, SpeechSource Source) line)
        {
            if (line.Source == SpeechSource.Trigger) ShowTriggerBubbleImmediate(line.Text);
            else ShowSpeech(line.Text, NextPresetGiggleSound(), aiGenerated: false, null, false, preset: true);
        }

        /// <summary>WPF GigglePriority / PanicSilence: drop every waiting line and the pending gap.</summary>
        private void ClearSpeechQueue()
        {
            CancelTriggerAnimation(); // panic, close and priority speech retire pending lead-in and typing
            _speechQueue.Clear();
            _cancelSpeechDelay();
            _cancelSpeechDelay = static () => { };
        }

        /// <summary>A line left the screen (or was skipped): remember what it earned, then the next.</summary>
        private void NoteSpeechEnded(SpeechSource source, int length)
        {
            _lastSpeechEndTime = SpeechNow;
            _lastSpeechSource = source;
            _lastSpeechLength = length;
            ProcessNextSpeech();
        }

        private SpeechSource _currentSpeechSource;
        private int _currentSpeechLength;

        /// <summary>The bubble's auto-hide (WPF Speech.cs:588-596): collapse it, then the next line.</summary>
        internal void EndSpeechBubble()
        {
            CancelTriggerAnimation();
            _speechTimer?.Stop();
            _speechBubble.IsVisible = false;
            _isShowingAiBubble = false;
            NoteSpeechEnded(_currentSpeechSource, _currentSpeechLength);
        }
    }
}
