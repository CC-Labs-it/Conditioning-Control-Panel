using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The long-stare heuristic, moved out of WebcamTrackingService so both heads share it: the last
    /// <see cref="BufferSize"/> emitted gaze points span at least <see cref="DurationMs"/> and all sit
    /// within <see cref="MaxDeviationPx"/> of their centroid -> one stare at that centroid, then a
    /// <see cref="CooldownMs"/> quiet period. Capture thread only.
    /// </summary>
    public sealed class LongStareDetector
    {
        public const int BufferSize = 30;
        public const int DurationMs = 3000;
        public const double MaxDeviationPx = 60.0;
        public const int CooldownMs = 5000;

        private readonly Queue<(DateTime Time, double X, double Y)> _buffer = new();
        private DateTime _lastStareAt = DateTime.MinValue;

        /// <summary>Drops the buffered points (face lost, tracking restarted). The cooldown survives, as on WPF.</summary>
        public void Clear() => _buffer.Clear();

        /// <summary>Feeds one emitted gaze point; returns the stare centre when this point completes a stare.</summary>
        public (double X, double Y)? Push(DateTime now, double x, double y)
        {
            _buffer.Enqueue((now, x, y));
            while (_buffer.Count > BufferSize) _buffer.Dequeue();
            if (_buffer.Count < 5) return null;

            if ((now - _buffer.Peek().Time).TotalMilliseconds < DurationMs) return null;
            if ((now - _lastStareAt).TotalMilliseconds < CooldownMs) return null;

            double cx = 0, cy = 0;
            foreach (var s in _buffer) { cx += s.X; cy += s.Y; }
            cx /= _buffer.Count;
            cy /= _buffer.Count;

            foreach (var s in _buffer)
            {
                double ddx = s.X - cx, ddy = s.Y - cy;
                if ((ddx * ddx + ddy * ddy) > MaxDeviationPx * MaxDeviationPx) return null;
            }

            _lastStareAt = now;
            return (cx, cy);
        }
    }
}
