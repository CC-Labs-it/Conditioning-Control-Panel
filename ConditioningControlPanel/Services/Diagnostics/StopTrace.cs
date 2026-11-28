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
    /// list entries); a fast stop writes no such line. No behaviour change: marks never catch or reorder.
    /// UI thread only, like the stop paths it times. Format pinned by StopTraceTests.
    /// <para>Video trace only, every stop (7.4.2 follow-up, hunt.md B6): a <c>stop:begin</c> line BEFORE the
    /// first step (open windows, private MB), so a process that dies mid-stop still leaves it; a
    /// <c>stop:end</c> line; then the time to the first rendered frame and to dispatcher idle after it.</para>
    /// </summary>
    internal sealed class StopTrace : IStopTrace
    {
        /// <summary>A stop under this writes nothing.</summary>
        public const long SlowMs = 1000;

        private readonly string _path;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly List<(string Step, long Ms)> _steps = new();
        private long _lastMark;

        private StopTrace(string path)
        {
            _path = path;
            try { VideoDiag.Log("STOP", BeginLine(path, App.IsEngineRunning, OpenWindowCount(), StallFacts.PrivateMb())); }
            catch { }
        }

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
                long total = _clock.ElapsedMilliseconds;
                try { VideoDiag.Log("STOP", EndLine(_path, total, StallFacts.PrivateMb())); } catch { }
                WatchFirstFrame();
                var line = Line(_path, total, _steps);
                if (line == null) return null;
                try { App.Logger?.Warning("{Line}", line); } catch { }
                // Mirrored into the video trace: a bug report attaches its tail even when a relaunch
                // has scrolled the rolling log (the watchdog lines do the same).
                try { VideoDiag.Log("STOP", line); } catch { }
                return line;
            }
            catch { return null; }
        }

        public static string BeginLine(string path, bool engineRunning, int windows, long privMb)
            => string.Create(CultureInfo.InvariantCulture,
                $"stop:begin source={path} engine={(engineRunning ? "on" : "off")} windows={(windows >= 0 ? windows.ToString(CultureInfo.InvariantCulture) : "?")} privMB={(privMb >= 0 ? privMb.ToString(CultureInfo.InvariantCulture) : "?")}");

        public static string EndLine(string path, long totalMs, long privMb)
            => string.Create(CultureInfo.InvariantCulture,
                $"stop:end source={path} took {totalMs}ms privMB={(privMb >= 0 ? privMb.ToString(CultureInfo.InvariantCulture) : "?")}");

        /// <summary>The engine stop's one GC.Collect: GC work during the call and the LOH size reported by the last
        /// completed GC before and after (the collect is non-blocking, so "after" may still be the old figure).</summary>
        public static string GcLine(string path, GcMark before, GcMark after, long lohBeforeMb, long lohAfterMb)
            => string.Create(CultureInfo.InvariantCulture,
                $"{path}: gc collect gc2+{after.Gen2 - before.Gen2} gcPause+{Math.Max(0, after.PauseMs - before.PauseMs)}ms loh(last gc) {lohBeforeMb}->{lohAfterMb} MB");

        /// <summary>LOH size in MB as of the last completed GC, or -1.</summary>
        public static long LohMb()
        {
            try
            {
                var gens = GC.GetGCMemoryInfo(GCKind.Any).GenerationInfo;
                return gens.Length > 3 ? gens[3].SizeAfterBytes / (1024 * 1024) : -1;
            }
            catch { return -1; }
        }

        private static int OpenWindowCount()
        {
            try
            {
                var app = System.Windows.Application.Current;
                return app != null && app.Dispatcher.CheckAccess() ? app.Windows.Count : -1;
            }
            catch { return -1; }
        }

        /// <summary>One CompositionTarget.Rendering (unsubscribed when it fires) names a render-pass stall after the
        /// stop; one Background-priority post names a queued-callback stall. UI thread only; a no-op elsewhere.</summary>
        private void WatchFirstFrame()
        {
            try
            {
                var app = System.Windows.Application.Current;
                if (app == null || !app.Dispatcher.CheckAccess() || app.Dispatcher.HasShutdownStarted) return;
                var clock = Stopwatch.StartNew();
                var path = _path;
                EventHandler? onFrame = null;
                onFrame = (_, _) =>
                {
                    System.Windows.Media.CompositionTarget.Rendering -= onFrame;
                    try { VideoDiag.Log("STOP", string.Create(CultureInfo.InvariantCulture, $"{path}: first frame after stop in {clock.ElapsedMilliseconds}ms")); } catch { }
                };
                System.Windows.Media.CompositionTarget.Rendering += onFrame;
                app.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
                {
                    try { VideoDiag.Log("STOP", string.Create(CultureInfo.InvariantCulture, $"{path}: dispatcher idle after stop in {clock.ElapsedMilliseconds}ms")); } catch { }
                }));
            }
            catch { }
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
        public static string HandlerName(Delegate d) => CoreStopTrace.HandlerName(d);
    }
}
