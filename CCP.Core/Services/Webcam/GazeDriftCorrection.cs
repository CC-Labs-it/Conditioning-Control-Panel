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

        internal void Reset()
        {
            _lastNudge = DateTime.MinValue;
            _lastPersist = DateTime.MinValue;
        }

        internal (RuntimeOffsetData Offset, bool Persist)? TryCorrect(
            bool enabled, bool tracking, bool consent, bool calibrationOwned,
            WebcamCalibrationData? calibration, IReadOnlyList<(DateTime At, double X, double Y)> gaze,
            double physicalX, double physicalY, DateTime now)
        {
            if (!enabled || !tracking || !consent || calibrationOwned || calibration == null) return null;
            if (!double.IsFinite(physicalX) || !double.IsFinite(physicalY)) return null;
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
            if (!double.IsFinite(residual) || residual < 15 || residual > 220) return null;
            var previous = calibration.RuntimeOffset;
            double oldX = previous?.Dx ?? 0, oldY = previous?.Dy ?? 0;
            if (!double.IsFinite(oldX) || !double.IsFinite(oldY)) return null;
            var offset = new RuntimeOffsetData
            {
                Dx = Math.Clamp(oldX + rx * 0.15, -300, 300),
                Dy = Math.Clamp(oldY + ry * 0.15, -300, 300),
                CapturedAt = now
            };
            bool persist = (now - _lastPersist).TotalMilliseconds >= 30000;
            if (persist) _lastPersist = now;
            _lastNudge = now;
            return (offset, persist);
        }
    }
}
