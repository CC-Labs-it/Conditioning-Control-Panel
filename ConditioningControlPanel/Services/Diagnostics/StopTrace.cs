using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ConditioningControlPanel.Services.Diagnostics
{
    /// <summary>
    /// Freeze hunt breadcrumbs for the stop paths (ccp-bugs #1747 #1748 #1760 #1721: a 10 to 14 s UI
    /// freeze right after Stop, session end, panic stop-all or Lockdown end, and no report carried a
    /// stack). A stop path calls <see cref="Mark"/> after each step; <see cref="Finish"/> writes ONE
    /// line with every step's milliseconds when the whole stop took at least <see cref="SlowMs"/>, so
    /// the next bug report's log tail names the slow step. Always on and cheap (one Stopwatch, a few
    /// list entries); a fast stop writes nothing. No behaviour change: marks never catch or reorder.
    /// UI thread only, like the stop paths it times. Format pinned by StopTraceTests.
    /// </summary>
    internal sealed class StopTrace
    {
        /// <summary>A stop under this writes nothing.</summary>
        public const long SlowMs = 1000;

        private readonly string _path;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<(string Step, long Ms)> _steps = new();
        private long _lastMark;

        private StopTrace(string path) => _path = path;

        /// <summary>Start timing one stop path ("engine stop", "session stop", "panic stop-all", "lockdown end").</summary>
        public static StopTrace Begin(string path) => new(path);

        /// <summary>The step that just ran took the time since the previous mark.</summary>
        public void Mark(string step)
        {
            var now = _clock.ElapsedMilliseconds;
            _steps.Add((step, now - _lastMark));
            _lastMark = now;
        }

        /// <summary>Raise each handler of an event in order, marking each by its method name. Exactly
        /// a plain Invoke: a throw still propagates and skips the handlers after it.</summary>
        public void Each(Delegate? handlers, Action<Delegate> invoke)
        {
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                invoke(d);
                Mark(HandlerName(d));
            }
        }

        /// <summary>Total so far, for tests and the line.</summary>
        public long ElapsedMs => _clock.ElapsedMilliseconds;

        /// <summary>Write the line when the stop was slow; returns it (null when fast). Never throws.</summary>
        public string? Finish()
        {
            try
            {
                var line = Line(_path, _clock.ElapsedMilliseconds, _steps);
                if (line == null) return null;
                try { App.Logger?.Warning("{Line}", line); } catch { }
                // Mirrored into the video trace: a bug report attaches its tail even when a relaunch
                // has scrolled the rolling log (the watchdog lines do the same).
                try { VideoDiag.Log("STOP", line); } catch { }
                return line;
            }
            catch { return null; }
        }

        /// <summary>The line, or null under <see cref="SlowMs"/>. Steps in run order, steps under 1 ms left out,
        /// the slowest step named first, and "after=" for time past the last mark.</summary>
        public static string? Line(string path, long totalMs, IReadOnlyList<(string Step, long Ms)> steps)
        {
            if (totalMs < SlowMs) return null;
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("[StopTrace] ").Append(path).Append(" took ").Append(totalMs.ToString(ci)).Append(" ms");
            if (steps.Count > 0)
            {
                var slowest = steps.OrderByDescending(s => s.Ms).First();
                sb.Append("; slowest ").Append(slowest.Step).Append(' ').Append(slowest.Ms.ToString(ci));
                sb.Append("; steps");
                foreach (var (step, ms) in steps)
                    if (ms >= 1) sb.Append(' ').Append(step).Append('=').Append(ms.ToString(ci));
                long untracked = totalMs - steps.Sum(s => s.Ms);
                if (untracked > 0) sb.Append(" after=").Append(untracked.ToString(ci));
            }
            return sb.ToString();
        }

        /// <summary>"Type.Method" of a handler (a lambda reads as its enclosing type plus the compiler name).</summary>
        public static string HandlerName(Delegate d)
        {
            var m = d.Method;
            var type = m.DeclaringType;
            while (type is { IsNested: true } && type.Name.StartsWith("<", StringComparison.Ordinal)) type = type.DeclaringType;
            return (type?.Name ?? "?") + "." + m.Name;
        }
    }
}
