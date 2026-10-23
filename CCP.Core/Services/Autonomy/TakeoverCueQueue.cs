using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The takeover cue's queue and timing (WPF Overlays/TakeoverAnnouncerOverlay.cs). One cue at a
    /// time: fade in 150 ms, hold 1700 ms, fade out 250 ms, then the next queued label starts from
    /// opacity 0. A label that arrives while a cue shows waits its turn; none is dropped. The head
    /// draws; this decides what shows and how far along it is. Time is the caller's (real elapsed).
    /// </summary>
    public sealed class TakeoverCueQueue
    {
        public const int InMs = 150, HoldMs = 1700, OutMs = 250;
        public const int CueMs = InMs + HoldMs + OutMs;
        public const double PeakOpacity = 1.0;
        public const double ShakeSec = 0.26, ShakeAmp = 3.5;     // impact shake: peak +/- DIP, decays to still
        public const float BloomIn = 0.15f, BloomOut = 0.70f;     // bloom pulse ramp up / ease down (s)
        public const float BloomTotal = BloomIn + BloomOut;

        private readonly Queue<string> _queue = new();
        private TimeSpan _start;

        /// <summary>The label on screen now (as given), or null when idle.</summary>
        public string? Current { get; private set; }
        public int Pending => _queue.Count;
        /// <summary>Bumps each time a cue starts, so the head can tell a new cue from the same label again.</summary>
        public int Serial { get; private set; }

        /// <summary>WPF Announce: queues a label. True when it starts showing now (nothing was showing).
        /// Null/blank labels are ignored.</summary>
        public bool Enqueue(string? label, TimeSpan now)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            _queue.Enqueue(label);
            if (Current != null) return false;
            Next(now);
            return true;
        }

        /// <summary>Age of the cue on screen, rolling to the next queued label once the fade-out
        /// ends. Null when idle (the head hides the window).</summary>
        public TimeSpan? Tick(TimeSpan now)
        {
            if (Current == null) return null;
            if (now - _start < TimeSpan.FromMilliseconds(CueMs)) return now - _start;
            Current = null;
            if (_queue.Count == 0) return null;
            Next(now);
            return TimeSpan.Zero;
        }

        /// <summary>WPF CloseActive: drops the visible cue and everything queued.</summary>
        public void Clear() { _queue.Clear(); Current = null; }

        private void Next(TimeSpan now) { Current = _queue.Dequeue(); _start = now; Serial++; }

        /// <summary>Window opacity at a cue's age: linear fade in, hold at peak, linear fade out.</summary>
        public static double Opacity(TimeSpan age)
        {
            var ms = age.TotalMilliseconds;
            if (ms <= 0) return 0;
            if (ms < InMs) return PeakOpacity * ms / InMs;
            if (ms < InMs + HoldMs) return PeakOpacity;
            if (ms < CueMs) return PeakOpacity * (1 - (ms - InMs - HoldMs) / OutMs);
            return 0;
        }

        /// <summary>WPF BloomEnv: 0 -> 1 over 0.15 s, back to 0 over 0.70 s.</summary>
        public static float BloomEnvelope(double sec)
        {
            if (sec < 0 || sec > BloomTotal) return 0f;
            if (sec < BloomIn) return (float)(sec / BloomIn);
            return (float)(1 - (sec - BloomIn) / BloomOut);
        }

        /// <summary>Shake amplitude (DIP) at a cue's age: 3.5 decaying linearly to 0 at 0.26 s.</summary>
        public static double ShakeAmplitude(double sec)
            => sec < 0 || sec >= ShakeSec ? 0 : ShakeAmp * (1 - sec / ShakeSec);
    }
}
