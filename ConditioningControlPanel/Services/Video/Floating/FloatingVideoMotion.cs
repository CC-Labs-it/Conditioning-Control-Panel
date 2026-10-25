using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;

namespace ConditioningControlPanel.Services.Video.Floating
{
    /// <summary>
    /// How one floating video window moves under the hand: grab, drag, throw, bounce, rest.
    /// No WPF, no Win32. The rules and the numbers are the flash images' own
    /// (<see cref="FlashDrag"/>, <see cref="FlashMotion.Reflect"/>): same tap test, same throw
    /// threshold, same 25% lost per bounce, same friction, same stop speed. The one rule added
    /// rules added here: a window is never left outside its bounds; a throw is scaled and capped
    /// by <see cref="Speed"/> (the hand itself stays 1:1); and a HELD window may cross to any
    /// monitor (<see cref="SetDesktop"/>), taking the work area it is let go on as its new bounds
    /// (<see cref="MonitorAt"/>). Device pixels throughout.
    /// </summary>
    internal sealed class FloatingVideoMotion
    {
        /// <summary>Slowest and fastest <see cref="Speed"/>.</summary>
        public const double MinSpeed = 0.1, MaxSpeed = 1.0;
        /// <summary>The fastest a thrown window travels at speed 1, px/s. Scaled by <see cref="Speed"/>.</summary>
        public const double TopSpeedPxPerSec = 3600.0;

        private readonly FlashMotionState _s = new() { Style = FlashMotionStyle.Still };
        private double _speed = 1.0;
        private double _dx, _dy, _dw, _dh;   // the whole desktop; zero size = none given

        /// <summary>The throw is measured over this much of the hand's recent path. Two samples of
        /// a fast mouse sit a millisecond apart, and the speed and direction between just those
        /// two is noise: a quick drag let go came out as a full-speed throw the wrong way.</summary>
        public const double ThrowWindowMs = 80.0;
        private readonly System.Collections.Generic.Queue<(double X, double Y, double Ms)> _path = new();

        /// <summary>
        /// How fast a throw travels: 1 = the flash images' own speed. Scales the velocity a throw
        /// starts with and the top speed. Live: a window in flight slows or quickens with it.
        /// </summary>
        public double Speed
        {
            get => _speed;
            set
            {
                var v = double.IsNaN(value) ? 1.0 : Math.Clamp(value, MinSpeed, MaxSpeed);
                if (v == _speed) return;
                if (Flying) { _s.Vx *= v / _speed; _s.Vy *= v / _speed; }
                _speed = v;
            }
        }

        /// <summary>
        /// The monitor seam: the work area of the monitor nearest to a point (the window's
        /// centre), or null when it cannot say. Without it the bounds never change.
        /// </summary>
        public Func<double, double, (double X, double Y, double W, double H)?>? MonitorAt { get; set; }

        public (double X, double Y, double W, double H) Bounds => (_s.BoundsX, _s.BoundsY, _s.BoundsW, _s.BoundsH);

        public FloatingVideoMotion(double x, double y, double w, double h,
            double bx, double by, double bw, double bh)
        {
            SetBounds(bx, by, bw, bh);
            Resync(x, y, w, h);
        }

        public double X => _s.X;
        public double Y => _s.Y;
        public double W => _s.W;
        public double H => _s.H;
        public double Vx => _s.Vx;
        public double Vy => _s.Vy;

        /// <summary>The button is down on this window.</summary>
        public bool Held => _s.Drag?.Held == true;
        /// <summary>Thrown and still moving.</summary>
        public bool Flying => _s.Drag?.Flinging == true;
        /// <summary>Held or flying: the clock must keep stepping.</summary>
        public bool Active => _s.Drag != null;

        public void SetBounds(double bx, double by, double bw, double bh)
        {
            _s.BoundsX = bx; _s.BoundsY = by; _s.BoundsW = bw; _s.BoundsH = bh;
            ClampInside();
        }

        /// <summary>The whole desktop (every monitor). A held window is kept inside this instead of
        /// its own monitor, so the hand can carry it across.</summary>
        public void SetDesktop(double x, double y, double w, double h)
        {
            _dx = x; _dy = y; _dw = w; _dh = h;
        }

        /// <summary>
        /// Take the work area of the monitor under the window's centre as the bounds and pull the
        /// window inside. Called when the hand lets go, and when the monitors change (one
        /// unplugged, a new resolution): a window is never left outside every work area.
        /// </summary>
        public void Rehome()
        {
            var area = MonitorAt?.Invoke(_s.X + _s.W / 2.0, _s.Y + _s.H / 2.0);
            if (area is { W: > 0, H: > 0 } a) { _s.BoundsX = a.X; _s.BoundsY = a.Y; _s.BoundsW = a.W; _s.BoundsH = a.H; }
            ClampInside();
        }

        /// <summary>
        /// The window changed size by itself (it landed on a monitor with another scale). A held
        /// window keeps the same spot of itself under the hand, so the pointer never slips off it.
        /// </summary>
        public void Resize(double w, double h)
        {
            if (w <= 0 || h <= 0 || (w == _s.W && h == _s.H)) return;
            var d = _s.Drag;
            if (d != null && d.Held && _s.W > 0 && _s.H > 0)
            {
                double px, py;
                lock (d.Sync) { px = d.LastX; py = d.LastY; }
                d.GrabDx *= w / _s.W;
                d.GrabDy *= h / _s.H;
                _s.X = px + d.GrabDx;
                _s.Y = py + d.GrabDy;
            }
            _s.W = w; _s.H = h; _s.MediaW = w; _s.MediaH = h;
            ClampInside();
        }

        /// <summary>Adopt the window's real rectangle (something else may have placed it), pulled
        /// back inside the bounds.</summary>
        /// <param name="rehome">True takes the monitor the rectangle is on as the new bounds first.</param>
        public void Resync(double x, double y, double w, double h, bool rehome = false)
        {
            _s.X = x; _s.Y = y; _s.W = w; _s.H = h; _s.MediaW = w; _s.MediaH = h;
            if (rehome) Rehome(); else ClampInside();
        }

        /// <summary>The hand lands. Also catches a window in flight: the throw ends there.</summary>
        public void Press(double pointerX, double pointerY, double nowMs)
        {
            FlashDrag.Begin(_s, pointerX, pointerY, nowMs);
            _path.Clear();
            _path.Enqueue((pointerX, pointerY, nowMs));
        }

        /// <summary>One pointer position while held. The window follows on the next step.</summary>
        public void Move(double pointerX, double pointerY, double nowMs)
        {
            var d = _s.Drag;
            if (d == null) return;
            FlashDrag.Sample(d, pointerX, pointerY, nowMs);
            _path.Enqueue((pointerX, pointerY, nowMs));
            while (_path.Count > 2 && nowMs - _path.Peek().Ms > ThrowWindowMs) _path.Dequeue();
        }

        /// <summary>The hand's velocity in px/ms across the last <see cref="ThrowWindowMs"/> of
        /// its path, zero when it has been parked (the flash rule: a stale hand does not throw).</summary>
        private (double Vx, double Vy) PathVelocity(double nowMs)
        {
            if (_path.Count < 2) return (0, 0);
            (double X, double Y, double Ms) first = default, last = default;
            bool any = false;
            foreach (var p in _path)
            {
                last = p;
                if (!any && nowMs - p.Ms <= ThrowWindowMs) { first = p; any = true; }
            }
            if (!any || nowMs - last.Ms > FlashDrag.StaleMs) return (0, 0);
            var dt = last.Ms - first.Ms;
            if (dt < 8.0) return (0, 0);   // too short a stretch to read a direction from
            return ((last.X - first.X) / dt, (last.Y - first.Y) / dt);
        }

        /// <summary>
        /// The hand lets go. Tap = a touch and nothing moves; Place = it stays where it was put;
        /// Fling = it keeps the velocity. Motion level Off never throws, Reduced halves the throw.
        /// </summary>
        public FlashDragOutcome Release(double nowMs, MotionLevel level)
        {
            var drag = _s.Drag;
            var outcome = FlashDrag.Release(_s, nowMs, level);
            if (outcome != FlashDragOutcome.Tap && drag != null)
            {
                // Flash reads the throw off the last two samples; a window reads it off the path.
                var (pvx, pvy) = PathVelocity(nowMs);
                var scale = FlashMotion.LevelScale(level);
                if (scale > 0 && FlashDrag.ShouldFling(pvx, pvy))
                {
                    drag.Flinging = true;
                    _s.Drag = drag;
                    _s.Vx = pvx * 1000.0 * scale;
                    _s.Vy = pvy * 1000.0 * scale;
                    outcome = FlashDragOutcome.Fling;
                }
                else
                {
                    _s.Drag = null;
                    _s.Vx = 0;
                    _s.Vy = 0;
                    outcome = FlashDragOutcome.Place;
                }
            }
            _path.Clear();
            if (outcome == FlashDragOutcome.Fling)
            {
                // The flash throw, scaled, then capped: a wild flick is still a drift at a low speed.
                _s.Vx *= _speed;
                _s.Vy *= _speed;
                var v = Math.Sqrt(_s.Vx * _s.Vx + _s.Vy * _s.Vy);
                var top = TopSpeedPxPerSec * _speed * FlashMotion.LevelScale(level);
                if (v > top && v > 0) { _s.Vx *= top / v; _s.Vy *= top / v; }
            }
            Rehome();   // it bounces on the monitor it was let go on
            return outcome;
        }

        /// <summary>Let go without a throw, whatever the hand was doing (a stop, a lost button-up).</summary>
        public void Drop()
        {
            var held = Held;
            _s.Drag = null;
            _s.Vx = 0;
            _s.Vy = 0;
            if (held) Rehome(); else ClampInside();
        }

        /// <summary>Advance <paramref name="dt"/> seconds. True when the rectangle moved.</summary>
        public bool Step(double dt)
        {
            if (dt <= 0 || _s.Drag == null) return false;
            double ox = _s.X, oy = _s.Y;
            FlashMotion.Step(_s, dt);
            ClampInside();
            return _s.X != ox || _s.Y != oy;
        }

        /// <summary>One axis: inside [min, min + extent - size], parked at min when it cannot fit.</summary>
        public static double Clamp(double pos, double size, double min, double extent)
        {
            var max = min + extent - size;
            if (max <= min) return min;
            return Math.Clamp(pos, min, max);
        }

        private void ClampInside()
        {
            if (Held && _dw > 0 && _dh > 0)
            {
                // In the hand: any monitor will do, only the edge of the desktop stops it.
                _s.X = Clamp(_s.X, _s.W, _dx, _dw);
                _s.Y = Clamp(_s.Y, _s.H, _dy, _dh);
                return;
            }
            if (_s.BoundsW <= 0 || _s.BoundsH <= 0) return;
            _s.X = Clamp(_s.X, _s.W, _s.BoundsX, _s.BoundsW);
            _s.Y = Clamp(_s.Y, _s.H, _s.BoundsY, _s.BoundsH);
        }
    }
}
