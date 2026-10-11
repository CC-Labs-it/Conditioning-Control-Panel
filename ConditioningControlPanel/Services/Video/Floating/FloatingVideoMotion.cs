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
    /// here is that a window is never left outside its bounds, not even while it is held.
    /// Device pixels throughout.
    /// </summary>
    internal sealed class FloatingVideoMotion
    {
        private readonly FlashMotionState _s = new() { Style = FlashMotionStyle.Still };

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

        /// <summary>Adopt the window's real rectangle (something else may have placed it), pulled
        /// back inside the bounds.</summary>
        public void Resync(double x, double y, double w, double h)
        {
            _s.X = x; _s.Y = y; _s.W = w; _s.H = h; _s.MediaW = w; _s.MediaH = h;
            ClampInside();
        }

        /// <summary>The hand lands. Also catches a window in flight: the throw ends there.</summary>
        public void Press(double pointerX, double pointerY, double nowMs)
            => FlashDrag.Begin(_s, pointerX, pointerY, nowMs);

        /// <summary>One pointer position while held. The window follows on the next step.</summary>
        public void Move(double pointerX, double pointerY, double nowMs)
        {
            var d = _s.Drag;
            if (d != null) FlashDrag.Sample(d, pointerX, pointerY, nowMs);
        }

        /// <summary>
        /// The hand lets go. Tap = a touch and nothing moves; Place = it stays where it was put;
        /// Fling = it keeps the velocity. Motion level Off never throws, Reduced halves the throw.
        /// </summary>
        public FlashDragOutcome Release(double nowMs, MotionLevel level)
        {
            var outcome = FlashDrag.Release(_s, nowMs, level);
            ClampInside();
            return outcome;
        }

        /// <summary>Let go without a throw, whatever the hand was doing (a stop, a lost button-up).</summary>
        public void Drop()
        {
            _s.Drag = null;
            _s.Vx = 0;
            _s.Vy = 0;
            ClampInside();
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
            if (_s.BoundsW <= 0 || _s.BoundsH <= 0) return;
            _s.X = Clamp(_s.X, _s.W, _s.BoundsX, _s.BoundsW);
            _s.Y = Clamp(_s.Y, _s.H, _s.BoundsY, _s.BoundsH);
        }
    }
}
