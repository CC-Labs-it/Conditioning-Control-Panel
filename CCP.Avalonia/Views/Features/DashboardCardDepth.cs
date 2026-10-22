using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Features
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Services/UI/DashboardCardDepth.cs. Visual feedback only:
    /// the owning card keeps all click and toggle semantics. The face travels by Core
    /// <see cref="DepthLaw.TravelFor"/> (pressed sinks, on sits in its socket, hovered idle lifts), an
    /// off tile at rest stands <see cref="RestLiftPx"/> proud, the bevel swaps DepthRaisedBevel /
    /// DepthPressedBevel, and a hovered tile leans toward the pointer where tilt is allowed.
    /// WPF's keyframe spring is a transition with <see cref="ReleaseSpring"/> easing (P82: no
    /// Animation on a Transform).
    /// </summary>
    internal sealed class DashboardCardDepth
    {
        /// <summary>An off tile at rest stands this far proud of its socket (up = negative).</summary>
        internal const double RestLiftPx = DepthLaw.HoverLiftPx / 2;

        private readonly Control _owner;
        private readonly Border _bevel;
        private readonly TranslateTransform _travel = new();
        private readonly RotateTransform _tilt = new();
        private readonly Func<bool> _enabled;
        private readonly Func<bool> _active;
        private bool _pressed;
        private bool _hovered;
        private double _target = double.NaN;

        internal double Travel => _travel.Y;
        internal double TiltAngle => _tilt.Angle;
        internal bool IsPressed => _pressed;

        public DashboardCardDepth(Control owner, Control face, Border bevel,
            Func<bool> enabled, Func<bool> active, Func<PointerPressedEventArgs, bool> accepts)
        {
            _owner = owner;
            _bevel = bevel;
            _enabled = enabled;
            _active = active;
            face.RenderTransform = new TransformGroup { Children = { _tilt, _travel } };
            face.RenderTransformOrigin = RelativePoint.Center;
            // WPF PreviewMouseDown/Up: tunnel, so the owner sees the press before any child.
            owner.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                var kind = e.GetCurrentPoint(owner).Properties.PointerUpdateKind;
                if (!_enabled() || kind is not (PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.RightButtonPressed) || !accepts(e)) return;
                _pressed = true;
                Refresh();
            }, RoutingStrategies.Tunnel);
            owner.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Release(), RoutingStrategies.Tunnel);
            // Hover is the FACE's, not the owner's (WPF: a split tile hands each half its own face).
            face.PointerEntered += (_, _) => { _hovered = true; Refresh(); };
            face.PointerExited += (_, _) => { _hovered = false; SettleTilt(); Release(); };
            face.PointerMoved += (_, e) => TiltToward(e.GetPosition(face), face.Bounds.Size);
            owner.PointerExited += (_, _) => { if (_hovered) { _hovered = false; SettleTilt(); } Release(); };
            owner.PointerCaptureLost += (_, _) => Release();
            owner.Unloaded += (_, _) => Park();
            owner.Loaded += (_, _) => Refresh();
        }

        /// <summary>WPF IsVisibleChanged/Unloaded: a hidden tile forgets its hover and press.</summary>
        internal void Park()
        {
            _hovered = false;
            Release();
        }

        /// <summary>Where the face sits, px DOWN from the socket line (WPF TravelFor).</summary>
        internal static double TravelFor(bool enabled, bool pressed, bool active, bool hovered)
        {
            if (!enabled) return 0;
            double t = DepthLaw.TravelFor(enabled, pressed, active, hovered);
            return !pressed && !active && !hovered ? -RestLiftPx : t;
        }

        /// <summary>On and pressed sit in the socket; everything else is raised.</summary>
        internal static string BevelKeyFor(bool pressed, bool active) =>
            pressed || active ? "DepthPressedBevel" : "DepthRaisedBevel";

        /// <summary>The tilt for a pointer at (nx, ny) in -1..1 over the tile: the launcher's lean.</summary>
        internal static double TiltFor(double nx, double ny) =>
            Math.Clamp(nx, -1, 1) * -Math.Clamp(ny, -1, 1) * DepthLaw.TiltDegrees;

        private static bool TiltAllowed => Env.Level != MotionLevel.Off && Env.CurrentTier != PerformanceTier.Performance;

        private void TiltToward(Point at, Size size)
        {
            if (!_hovered || _pressed || !_enabled() || !TiltAllowed || size.Width <= 0 || size.Height <= 0) return;
            double nx = at.X / size.Width * 2 - 1;
            double ny = at.Y / size.Height * 2 - 1;
            Move(_tilt, RotateTransform.AngleProperty, TiltFor(nx, ny), DepthLaw.Ms(DepthLaw.HoverMs, Env.Level), new LinearEasing());
        }

        private void SettleTilt()
        {
            int ms = DepthLaw.Ms(DepthLaw.HoverMs, Env.Level);
            Move(_tilt, RotateTransform.AngleProperty, 0, _owner.IsLoaded ? ms : 0, new QuadraticEaseOut());
        }

        private void Release()
        {
            _pressed = false;
            Refresh();
        }

        public void Refresh()
        {
            bool enabled = _enabled();
            bool active = enabled && _active();
            double target = TravelFor(enabled, _pressed, active, _hovered);
            _bevel.IsVisible = enabled;
            _bevel.Opacity = _pressed ? 1 : active ? 0.75 : 0.9;
            if (_owner.TryFindResource(BevelKeyFor(_pressed, active), _owner.ActualThemeVariant, out var lamp) && lamp is IBrush brush)
                _bevel.BorderBrush = brush;
            if (target == _target && Env.AllowTransitions) return;
            _target = target;
            double from = _travel.Y;
            int ms = DepthLaw.Ms(_pressed ? DepthLaw.PressMs : DepthLaw.ReleaseMs, Env.Level);
            if (ms <= 0 || !Env.AllowTransitions || !_owner.IsLoaded || !_owner.IsEffectivelyVisible) ms = 0;
            // Going down (press, sink) eases out; coming back UP springs past rest, then settles.
            Easing easing = _pressed || target >= from
                ? new QuadraticEaseOut()
                : new ReleaseSpring(DepthLaw.ReleaseOvershootPx / (from - target));
            Move(_travel, TranslateTransform.YProperty, target, ms, easing);
        }

        private static void Move(Animatable t, StyledProperty<double> property, double to, int ms, Easing easing)
        {
            t.Transitions = ms > 0
                ? new Transitions { new DoubleTransition { Property = property, Duration = TimeSpan.FromMilliseconds(ms), Easing = easing } }
                : null;
            t.SetValue(property, to);
        }

        /// <summary>WPF's release keyframes as one easing: QuadOut to <c>1 + overshoot</c> at 65%,
        /// then QuadInOut back to 1. <paramref name="overshoot"/> is the overshoot as a fraction of the travel.</summary>
        internal sealed class ReleaseSpring(double overshoot) : Easing
        {
            public override double Ease(double p)
            {
                if (p <= 0.65)
                {
                    double q = p / 0.65;
                    return (1 + overshoot) * (1 - (1 - q) * (1 - q));
                }
                double r = (p - 0.65) / 0.35;
                double io = r < 0.5 ? 2 * r * r : 1 - Math.Pow(-2 * r + 2, 2) / 2;
                return 1 + overshoot - overshoot * io;
            }
        }
    }
}
