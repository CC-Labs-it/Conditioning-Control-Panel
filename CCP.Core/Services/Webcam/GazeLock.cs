using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Gaze lock-on, moved out of WebcamTrackingService so both heads share it: an optional "the
    /// user is trying to hit THIS" hint set by UI flows that know the current target (the
    /// calibration bubble test, GazeFocusService for live gaze-pop bubbles). The lock is STATEFUL:
    /// an engagement level builds while the gaze lingers near the target and drains when it leaves -
    /// or when the gaze sway grows too large relative to the target radius, so a user who is clearly
    /// not holding the target loses the lock gradually instead of it snapping off. While engaged, the
    /// deviation from the target is contracted (wobble stays visible, scaled down in proportion to
    /// the lock), tapering to nothing by 4x the radius so there's no seam at the boundary.
    ///
    /// <para>Tuned QUADRANT-strong (round-11 user direction): "if we are in that quadrant, we are
    /// looking at that target". Gaze anywhere in a target's broad region is treated as intent.</para>
    ///
    /// <para><see cref="SetTarget"/>/<see cref="Clear"/> are UI-thread safe; <see cref="Apply"/>
    /// runs on the capture thread only and picks the target up next frame.</para>
    /// </summary>
    public sealed class GazeLock
    {
        private const double MaxStrength = 0.93; // deviation contraction at full engagement
        private const double CaptureFrac = 3.5;  // capture zone, x radius - quadrant-scale for a typical bubble
        private const double BuildRate = 0.22;   // per-frame engagement growth toward 1 (~0.2s to lock at 30fps)
        private const double LeaveDrain = 0.05;  // per-frame drain while outside the capture zone (~0.6s release)
        private const double SwayFrac = 1.4;     // sway (EMA dips) above this x radius starts draining the lock
        private const double SwayDrain = 0.12;   // drain rate per unit of excess swayNorm

        private sealed record Target(double X, double Y, double Radius);
        private volatile Target? _target;

        // Capture thread only.
        private double _engage;
        private double _centerX = double.NaN, _centerY = double.NaN;
        private double _emaX = double.NaN, _emaY = double.NaN;
        private double _swayEma;

        /// <summary>Declare the current gaze target (coordinates in the OnGazeMove DIP space). Safe to call every tick with a drifting center - engagement carries over unless the center jumps by more than the radius.</summary>
        public void SetTarget(double centerX, double centerY, double radius) => _target = new Target(centerX, centerY, radius);

        public void Clear() => _target = null;

        /// <summary>
        /// Contracts <paramref name="x"/>,<paramref name="y"/> (the PRE-lock gaze) toward the target
        /// by the current engagement. Sway and distance are always measured on the raw point, so the
        /// lock can't feed its own output back and hold forever. No target: resets and returns the
        /// point unchanged.
        /// </summary>
        public (double X, double Y) Apply(double x, double y)
        {
            var att = _target;
            if (att == null)
            {
                _engage = 0;
                _centerX = _centerY = double.NaN;
                _emaX = _emaY = double.NaN;
                _swayEma = 0;
                return (x, y);
            }

            // Target switch (a different bubble, not the same one drifting):
            // engagement doesn't carry over to a new target.
            if (!double.IsNaN(_centerX))
            {
                double mdx = att.X - _centerX, mdy = att.Y - _centerY;
                if (Math.Sqrt(mdx * mdx + mdy * mdy) > att.Radius) _engage = 0;
            }
            _centerX = att.X;
            _centerY = att.Y;

            // Sway estimate: EMA of the deviation from a short-horizon EMA of
            // the raw gaze - roughly the wobble amplitude in DIPs.
            if (double.IsNaN(_emaX)) { _emaX = x; _emaY = y; }
            _emaX += (x - _emaX) * 0.25;
            _emaY += (y - _emaY) * 0.25;
            double wx = x - _emaX, wy = y - _emaY;
            _swayEma += (Math.Sqrt(wx * wx + wy * wy) - _swayEma) * 0.15;

            double dx = x - att.X, dy = y - att.Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist <= att.Radius * CaptureFrac)
                _engage += (1.0 - _engage) * BuildRate;
            else
                _engage -= LeaveDrain;

            double swayNorm = _swayEma / Math.Max(1e-6, att.Radius);
            if (swayNorm > SwayFrac)
                _engage -= (swayNorm - SwayFrac) * SwayDrain;

            _engage = Math.Max(0, Math.Min(1, _engage));
            if (_engage <= 0) return (x, y);

            // Distance taper: full pull inside the radius, fading to nothing
            // by 4x - quadrant reach, so an engaged lock draws the cursor in
            // from anywhere in the target's region of the screen.
            double taper = dist <= att.Radius
                ? 1.0
                : dist <= att.Radius * 4.0
                    ? (att.Radius * 4.0 - dist) / (att.Radius * 3.0)
                    : 0.0;
            double k = MaxStrength * _engage * taper;
            if (k <= 0) return (x, y);
            return (x - dx * k, y - dy * k);
        }
    }
}
