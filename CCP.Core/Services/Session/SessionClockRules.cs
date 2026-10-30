using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>Clock faces, user resume policy and guarded session steps.</summary>
    public static class SessionClockRules
    {
        /// <summary>What a session clock is doing right now.</summary>
        public enum ClockFace
        {
            /// <summary>No session: the clocks give the room back.</summary>
            None,
            /// <summary>The session's own one-second tick drives the clocks.</summary>
            Counting,
            /// <summary>The tick is held: the clocks keep their value and carry the pause mark.</summary>
            Paused,
        }

        public static ClockFace Face(bool sessionRunning, bool sessionPaused) =>
            !sessionRunning ? ClockFace.None
            : sessionPaused ? ClockFace.Paused
            : ClockFace.Counting;

        /// <summary>A held clock always carries the pause label.</summary>
        public static string PauseMark(bool sessionPaused, string pausedLabel) =>
            sessionPaused ? " [" + (pausedLabel ?? "").Trim() + "]" : "";

        /// <summary>Only an explicit user START resumes a parked session.</summary>
        public static bool StartResumesParkedSession(bool engineRunning, bool sessionRunning, bool sessionPaused) =>
            engineRunning && sessionRunning && sessionPaused;

        /// <summary>A remote controller keeps ownership of the engine.</summary>
        public static bool ResumeStartsEngine(bool engineRunning, bool controllerConnected) =>
            !engineRunning && !controllerConnected;

        /// <summary>Report a failed step without blocking the remaining work.</summary>
        public static bool RunStep(Action step, Action<Exception>? onError)
        {
            try
            {
                step();
                return true;
            }
            catch (Exception ex)
            {
                try { onError?.Invoke(ex); } catch { }
                return false;
            }
        }

        /// <summary>Count repeats and report each step in full only once per session.</summary>
        public sealed class StepFailures
        {
            private readonly System.Collections.Generic.Dictionary<string, int> _counts = new(StringComparer.Ordinal);

            /// <summary>TRUE for the first failure of <paramref name="step"/> since the last Clear.</summary>
            public bool Note(string step)
            {
                _counts.TryGetValue(step, out var n);
                _counts[step] = n + 1;
                return n == 0;
            }

            public int Count(string step) => _counts.TryGetValue(step, out var n) ? n : 0;

            public bool Any => _counts.Count > 0;

            public void Clear() => _counts.Clear();

            /// <summary>"progress x120, ramp x3", sorted by step name; empty when nothing failed.</summary>
            public string Summary()
            {
                var keys = new System.Collections.Generic.List<string>(_counts.Keys);
                keys.Sort(StringComparer.Ordinal);
                return string.Join(", ", keys.ConvertAll(k => k + " x" + _counts[k]));
            }
        }
    }
}
