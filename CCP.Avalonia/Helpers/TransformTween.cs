using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// IMPORTED from trunk (c18c2d8bb, 62bdb4c19). Keyframes on a Transform (or a brush) object.
    /// Avalonia's <c>Animation.RunAsync(aTransform)</c> resolves to TransformAnimator, which casts its
    /// target to Visual and throws InvalidCastException: inside a try it leaves the effect silently
    /// inert, outside one it kills the app. This writes the property from a 16 ms timer instead. The
    /// easing runs over the whole progress and keys are linear in between, as Animation does with one
    /// Easing. Time comes from <see cref="Time"/> so tests step it (P08).
    /// </summary>
    internal sealed class TransformTween
    {
        internal static TimeProvider Time = TimeProvider.System;
        // Running tweens; a running DispatcherTimer roots them anyway, so this adds no lifetime.
        private static readonly HashSet<TransformTween> Live = new();

        private readonly AvaloniaObject _target;
        private readonly (AvaloniaProperty Prop, (double cue, AvaloniaProperty prop, double value)[] Keys)[] _props;
        private readonly double _ms;
        private readonly Easing? _easing;
        private readonly bool _loop;
        private readonly long _started = Time.GetTimestamp();
        private readonly DispatcherTimer _timer;

        private TransformTween(AvaloniaObject target, TimeSpan duration,
            IReadOnlyList<(double cue, AvaloniaProperty prop, double value)> keys, Easing? easing, bool loop)
        {
            _target = target;
            _props = keys.Select(k => k.prop).Distinct()
                .Select(p => (p, keys.Where(x => x.prop == p).OrderBy(x => x.cue).ToArray())).ToArray();
            _ms = Math.Max(1, duration.TotalMilliseconds);
            _easing = easing;
            _loop = loop;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
        }

        public static TransformTween Run(AvaloniaObject target, TimeSpan duration,
            IReadOnlyList<(double cue, AvaloniaProperty prop, double value)> keys,
            Easing? easing = null, bool loop = false)
        {
            var tween = new TransformTween(target, duration, keys, easing, loop);
            tween.Apply(0);
            tween._timer.Start();
            Live.Add(tween);
            return tween;
        }

        public bool IsRunning => _timer.IsEnabled;

        public void Stop()
        {
            _timer.Stop();
            Live.Remove(this);
        }

        /// <summary>Ticks every running tween once (tests, after stepping <see cref="Time"/>).</summary>
        internal static void StepAll()
        {
            foreach (var t in Live.ToArray()) t.Step();
        }

        /// <summary>One timer tick (tests call it after stepping <see cref="Time"/>).</summary>
        internal void Step()
        {
            if (!IsRunning) return;
            try
            {
                double elapsed = Time.GetElapsedTime(_started).TotalMilliseconds;
                if (_loop) { Apply(elapsed % _ms / _ms); return; }
                if (elapsed < _ms) { Apply(elapsed / _ms); return; }
                Apply(1);
                Stop();
            }
            catch (Exception ex) { Stop(); Log.Debug("TransformTween: {E}", ex.Message); }
        }

        private void Apply(double p)
        {
            double e = _easing?.Ease(p) ?? p;
            foreach (var (prop, k) in _props)
            {
                double v = k[^1].value;
                for (int i = 0; i < k.Length; i++)
                {
                    if (e > k[i].cue) continue;
                    if (i == 0) { v = k[0].value; break; }
                    double span = k[i].cue - k[i - 1].cue;
                    double t = span <= 0 ? 1 : (e - k[i - 1].cue) / span;
                    v = k[i - 1].value + (k[i].value - k[i - 1].value) * t;
                    break;
                }
                _target.SetValue(prop, v);
            }
        }
    }
}
