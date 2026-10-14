// Finite-track subset adapted from parity/trunk a4ee52d40cd347d288dcf7fdaa8445b79f8e5038.
using System;
using System.Diagnostics;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers;

internal static class FxTrack
{
    internal static bool ManualClock;
    internal sealed class Run
    {
        private readonly double _ms;
        private readonly Action<double> _at;
        private readonly Action _done;
        private readonly long _started = Stopwatch.GetTimestamp();
        private DispatcherTimer? _timer;
        internal Run(double ms, Action<double> at, Action done) => (_ms, _at, _done) = (ms, at, done);
        internal bool IsDone { get; private set; }
        internal void Start()
        {
            Apply(0);
            if (IsDone || ManualClock) return;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
                (_, _) => Seek(Stopwatch.GetElapsedTime(_started).TotalMilliseconds));
            _timer.Start();
        }
        internal void Seek(double ms)
        {
            if (IsDone) return;
            if (ms < _ms) Apply(ms); else Finish();
        }
        internal void Finish()
        {
            if (IsDone) return;
            IsDone = true;
            _timer?.Stop();
            _timer = null;
            Apply(_ms);
            _done();
        }
        private void Apply(double ms)
        {
            try { _at(ms); }
            catch (Exception ex) { Log.Debug("FxTrack: {E}", ex.Message); Finish(); }
        }
    }
    internal static Run Play(double ms, Action<double> at, Action done)
    {
        var run = new Run(ms, at, done);
        run.Start();
        return run;
    }
    internal static double QuadOut(double u) => 1 - (1 - u) * (1 - u);
    internal static double BackOut(double u, double amplitude)
    {
        double v = 1 - u;
        return 1 - (v * v * v - v * amplitude * Math.Sin(Math.PI * v));
    }
    internal static double Keys(double at, params (double At, double Value, Func<double, double>? Ease)[] keys)
    {
        if (at <= keys[0].At) return keys[0].Value;
        for (int i = 1; i < keys.Length; i++)
        {
            if (at > keys[i].At) continue;
            double span = keys[i].At - keys[i - 1].At;
            double u = span <= 0 ? 1 : (at - keys[i - 1].At) / span;
            if (keys[i].Ease is { } ease) u = ease(u);
            return keys[i - 1].Value + (keys[i].Value - keys[i - 1].Value) * u;
        }
        return keys[^1].Value;
    }
}
