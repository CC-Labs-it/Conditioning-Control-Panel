using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Conservative click residual calculation from WPF GazeDriftCorrectionService.ProcessClick.
    /// The caller owns the hook, consent, calibration windows and gaze sample lifetime.
    /// Click coordinates are used only for this call and are never retained.
    /// </summary>
    internal sealed class GazeDriftCorrection
    {
        private DateTime _lastNudge = DateTime.MinValue;
        private DateTime _lastPersist = DateTime.MinValue;
        private readonly Queue<(DateTime At, double Yaw, double Pitch)> _pose = new();
        private bool _anchored, _armed;
        private double _yaw, _pitch, _budget = 90000;
        private DateTime _lastPose, _armedAt, _fastUntil, _fastAt, _budgetAt;
        private int _nudges, _converged;

        // WPF GazeDriftCorrectionService.cs:280-495. Pose is a trigger only, never a correction term.
        internal void Pose(double yaw, double pitch, DateTime now)
        {
            if (!double.IsFinite(yaw) || !double.IsFinite(pitch)) return;
            Stale(now); Tick(now);
            if (_lastPose != DateTime.MinValue && (now - _lastPose).TotalMilliseconds > 1500)
            { _pose.Clear(); _armed = false; }
            var previous = _lastPose; _lastPose = now;
            _pose.Enqueue((now, yaw, pitch));
            while (_pose.Count > 0 && (now - _pose.Peek().At).TotalMilliseconds > 700) _pose.Dequeue();
            double minY = double.MaxValue, maxY = double.MinValue, minP = double.MaxValue, maxP = double.MinValue;
            foreach (var p in _pose)
            { minY = Math.Min(minY, p.Yaw); maxY = Math.Max(maxY, p.Yaw); minP = Math.Min(minP, p.Pitch); maxP = Math.Max(maxP, p.Pitch); }
            bool parked = _pose.Count >= 10 && maxY - minY <= 0.02 && maxP - minP <= 0.02;
            if (!_anchored) { if (parked) Anchor(yaw, pitch); return; }
            if (now < _fastUntil) { if (_budget <= 0) EndFast(); return; }
            double delta = Math.Max(Math.Abs(yaw - _yaw), Math.Abs(pitch - _pitch));
            if (delta <= 0.09)
            {
                _armed = false;
                if (parked)
                {
                    double dt = previous == DateTime.MinValue ? 0.033 : Math.Clamp((now - previous).TotalSeconds, 0, 1);
                    double a = Math.Clamp(dt / 60, 0, 1);
                    _yaw += (yaw - _yaw) * a; _pitch += (pitch - _pitch) * a;
                }
                return;
            }
            if (!_armed) { _armed = true; _armedAt = now; return; }
            if (parked)
            {
                Anchor(yaw, pitch);
                if (_budget >= 8000) { _fastAt = now; _fastUntil = now.AddSeconds(45); _nudges = _converged = 0; }
                return;
            }
            if ((now - _armedAt).TotalMilliseconds >= 20000) Anchor(yaw, pitch);
        }
        private void Anchor(double yaw, double pitch) { _yaw = yaw; _pitch = pitch; _anchored = true; _armed = false; }
        private void EndFast() { _fastUntil = DateTime.MinValue; _converged = 0; }
        private void Stale(DateTime now)
        {
            if (_fastUntil == DateTime.MinValue || _lastPose == DateTime.MinValue || (now - _lastPose).TotalMilliseconds < 1200) return;
            EndFast(); _pose.Clear(); _armed = false;
        }
        private void Tick(DateTime now)
        {
            if (_budgetAt == DateTime.MinValue) { _budgetAt = now; return; }
            double dt = (now - _budgetAt).TotalMilliseconds; _budgetAt = now;
            if (dt <= 0) return;
            dt = Math.Min(dt, 300000);
            _budget = Math.Clamp(_budget + (now < _fastUntil ? -dt : dt * 0.3), 0, 90000);
        }

        internal void Reset()
        {
            _lastNudge = DateTime.MinValue;
            _lastPersist = DateTime.MinValue;
            _pose.Clear(); _anchored = _armed = false; _lastPose = _fastUntil = _budgetAt = DateTime.MinValue;
            _budget = 90000; _nudges = _converged = 0;
        }

        internal (RuntimeOffsetData Offset, bool Persist)? TryCorrect(
            bool enabled, bool tracking, bool consent, bool calibrationOwned,
            WebcamCalibrationData? calibration, IReadOnlyList<(DateTime At, double X, double Y)> gaze,
            double physicalX, double physicalY, DateTime now)
        {
            if (!enabled || !tracking || !consent || calibrationOwned || calibration == null) return null;
            if (!double.IsFinite(physicalX) || !double.IsFinite(physicalY)) return null;
            Stale(now); Tick(now);
            if ((now - _lastNudge).TotalMilliseconds < 500) return null;

            int count = 0;
            double sx = 0, sy = 0;
            DateTime newest = DateTime.MinValue;
            foreach (var sample in gaze)
            {
                double age = (now - sample.At).TotalMilliseconds;
                if (age < 0 || age > 350) continue;
                if (!double.IsFinite(sample.X) || !double.IsFinite(sample.Y)) return null;
                count++;
                sx += sample.X;
                sy += sample.Y;
                if (sample.At > newest) newest = sample.At;
            }
            if (count < 5 || (now - newest).TotalMilliseconds > 300) return null;
            double gx = sx / count, gy = sy / count;
            foreach (var sample in gaze)
            {
                double age = (now - sample.At).TotalMilliseconds;
                if (age < 0 || age > 350) continue;
                double dx = sample.X - gx, dy = sample.Y - gy;
                if (Math.Sqrt(dx * dx + dy * dy) > 90) return null;
            }

            var bounds = calibration.MonitorBounds;
            double dpi = bounds?.DpiScale is > 0.25 and < 8.0 ? bounds.DpiScale : 1.0;
            double x = (physicalX - (bounds?.DeviceName != null ? bounds.X : 0)) / dpi;
            double y = (physicalY - (bounds?.DeviceName != null ? bounds.Y : 0)) / dpi;
            if (bounds != null && (x < 0 || y < 0 || x > bounds.Width || y > bounds.Height)) return null;
            double rx = x - gx, ry = y - gy;
            double residual = Math.Sqrt(rx * rx + ry * ry);
            bool fast = now < _fastUntil && _nudges < 15;
            double ramp = fast ? 1 - Math.Clamp((now - _fastAt).TotalMilliseconds / 45000, 0, 1) : 0;
            double gain = 0.15 + 0.30 * ramp, cap = 220 + 80 * ramp;
            if (!double.IsFinite(residual) || residual > cap) return null;
            if (fast) { if (residual < 45) { if (++_converged >= 3) EndFast(); } else _converged = 0; }
            if (residual < 15) return null;
            var previous = calibration.RuntimeOffset;
            double oldX = previous?.Dx ?? 0, oldY = previous?.Dy ?? 0;
            if (!double.IsFinite(oldX) || !double.IsFinite(oldY)) return null;
            var offset = new RuntimeOffsetData
            {
                Dx = Math.Clamp(oldX + rx * gain, -300, 300),
                Dy = Math.Clamp(oldY + ry * gain, -300, 300),
                CapturedAt = now
            };
            if (fast && ++_nudges >= 15) EndFast();
            bool persist = (now - _lastPersist).TotalMilliseconds >= 30000;
            if (persist) _lastPersist = now;
            _lastNudge = now;
            return (offset, persist);
        }
    }
}
