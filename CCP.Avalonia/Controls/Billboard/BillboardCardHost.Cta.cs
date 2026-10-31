using System;
using Avalonia;
using ConditioningControlPanel.Services.Billboard;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.UI;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard;

// WPF 80a8cdfc9 BillboardCardHost.Cta: retained gradient layers, never an animated blur.
internal sealed partial class BillboardCardHost
{
    private readonly Border _ctaGlow = new()
    {
        CornerRadius = new CornerRadius(18), Margin = new Thickness(-10, -8, -10, -6),
        IsHitTestVisible = false, Opacity = 0.32,
    };
    private readonly Rectangle _ctaShine = new() { IsHitTestVisible = false, RadiusX = 9, RadiusY = 9 };
    private readonly TranslateTransform _ctaShineMove = new();
    private readonly ScaleTransform _ctaScale = new(1, 1), _ctaSquash = new(1, 1);
    private readonly TranslateTransform _ctaTravel = new();
    private BeatLoop? _ctaBeat;
    private bool _ctaHovered, _ctaDown, _ctaReleasing, _ctaReleaseTravel, _ctaOnScreen;
    private double _ctaNow, _hoverAt, _pressAt, _travelAt, _glowAt = double.NegativeInfinity, _ctaShineAt = double.NegativeInfinity, _ctaLandAt = double.NegativeInfinity;
    private double _ctaSweepAt;
    private double _fromScale = 1, _fromX = 1, _fromY = 1, _fromTravel, _travelTarget, _fromGlow;

    internal bool CtaLoopsRunning => _ctaBeat != null;
    internal double CtaGlowOpacity => _ctaGlow.Opacity;
    internal double CtaShineOpacity => _ctaShine.Opacity;
    internal double CtaShineX => _ctaShineMove.X;
    internal (double Hover, double X, double Y, double Travel) CtaPose =>
        (_ctaScale.ScaleX, _ctaSquash.ScaleX, _ctaSquash.ScaleY, _ctaTravel.Y);

    private void BuildCtaFx(Grid root)
    {
        root.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        root.RenderTransform = _ctaScale;
        _ctaFace.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
        _ctaFace.RenderTransform = new TransformGroup { Children = { _ctaSquash, _ctaTravel } };
        _ctaShine.RenderTransform = _ctaShineMove;
        _ctaShine.Fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.Transparent, 0.36),
                new GradientStop(Color.FromArgb(0x8c, 0xff, 0xf6, 0xfc), 0.5),
                new GradientStop(Colors.Transparent, 0.64) },
        };
    }

    private void LandCtaFx(bool landed)
    {
        var hue = HueOf(_card!);
        _ctaGlow.Background = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(0xb0, hue.R, hue.G, hue.B), 0),
                new GradientStop(Color.FromArgb(0x70, hue.R, hue.G, hue.B), 0.55),
                new GradientStop(Color.FromArgb(0, hue.R, hue.G, hue.B), 1) },
        };
        _ctaHovered = _ctaDown = _ctaReleasing = false;
        _ctaLandAt = _ctaShineAt = landed && Env.AllowTransitions ? _ctaNow : double.NegativeInfinity;
        _hoverAt = _pressAt = _travelAt = _ctaSweepAt = _ctaNow;
        _glowAt = double.NegativeInfinity;
        _ctaReleaseTravel = false; _travelTarget = 0;
        _fromScale = _ctaScale.ScaleX; _fromX = _ctaSquash.ScaleX;
        _fromY = _ctaSquash.ScaleY; _fromTravel = _ctaTravel.Y;
    }

    internal void SetCtaHover(bool on)
    {
        _fromScale = _ctaScale.ScaleX; _fromGlow = _ctaGlow.Opacity;
        _hoverAt = _glowAt = _ctaNow; _ctaHovered = on;
        // WPF Lift leaves a depressed face to PressUp; hover never restarts the squash spring.
        if (!_ctaDown && _ctaTravel.Y <= 0)
        {
            _fromTravel = _ctaTravel.Y; _travelAt = _ctaNow;
            _travelTarget = on ? -DepthLaw.HoverLiftPx : 0; _ctaReleaseTravel = false;
        }
        if (on && Env.AllowTransitions) _ctaShineAt = _ctaNow;
        else { _ctaShineAt = double.NegativeInfinity; _ctaSweepAt = _ctaNow; }
        RefreshCtaFx(_ctaOnScreen);
    }

    internal void SetCtaPressed(bool down)
    {
        if (_ctaDown == down) return; // release and capture-lost may both arrive for one press
        _fromX = _ctaSquash.ScaleX; _fromY = _ctaSquash.ScaleY; _fromTravel = _ctaTravel.Y;
        _pressAt = _travelAt = _ctaNow; _ctaReleasing = _ctaReleaseTravel = !down; _ctaDown = down;
        _travelTarget = down ? DepthLaw.PressTravelPx : 0;
        RefreshCtaFx(_ctaOnScreen);
    }

    private void RefreshCtaFx(bool onScreen)
    {
        _ctaOnScreen = onScreen;
        if (!Env.AllowTransitions)
        {
            _ctaLandAt = _ctaShineAt = double.NegativeInfinity;
            _ctaScale.ScaleX = _ctaScale.ScaleY = _ctaSquash.ScaleX = _ctaSquash.ScaleY = 1;
            _ctaTravel.Y = 0; _ctaReleasing = _ctaReleaseTravel = false;
        }
        bool oneShot = Env.AllowTransitions && (_ctaNow - _hoverAt < 0.3 || _ctaNow - _pressAt < 0.3 || _ctaNow - _glowAt < 0.3 ||
            _ctaNow - _ctaShineAt < 0.6 || _ctaNow - _ctaLandAt < 0.7);
        if (onScreen && (Env.AllowAmbientLoops || oneShot))
        {
            if (_ctaBeat != null) return;
            double start = _ctaNow;
            _ctaBeat = new BeatLoop(_cta, t => StepCtaFx(start + t));
            _ctaBeat.Start();
        }
        else StopCtaFx();
    }

    private void StopCtaFx()
    {
        _ctaBeat?.Stop(); _ctaBeat = null;
        _ctaGlow.Opacity = _ctaHovered && Env.AllowTransitions ? 0.95 : 0.32;
        _ctaShine.Opacity = 0;
    }

    // One shared 30 fps beat updates render properties only. Tests step this same frame path.
    internal void StepCtaFx(double t)
    {
        _ctaNow = t;
        if (!Env.AllowTransitions) { RefreshCtaFx(_ctaOnScreen); return; }
        double speed = Env.Level == global::ConditioningControlPanel.Models.MotionLevel.Reduced ? 0.5 : 1;
        double hover = Math.Clamp((t - _hoverAt) / ((_ctaHovered ? 0.26 : 0.16) * speed), 0, 1);
        double hoverEase = _ctaHovered ? BillboardTiming.Thud(hover) : QuadOut(hover);
        _ctaScale.ScaleX = _ctaScale.ScaleY = _fromScale + ((_ctaHovered ? 1.05 : 1) - _fromScale) * hoverEase;
        double elapsed = Math.Max(0, t - _pressAt);
        double press = Math.Clamp(elapsed / (DepthLaw.PressMs * speed / 1000), 0, 1);
        double Spring(double from, double past) => ReleaseAt(elapsed / (DepthLaw.ReleaseMs * 1.6 * speed / 1000), from, past, 1, 0.45);
        _ctaSquash.ScaleX = _ctaDown ? _fromX + (1.07 - _fromX) * press : _ctaReleasing ? Spring(_fromX, 0.95) : 1;
        _ctaSquash.ScaleY = _ctaDown ? _fromY + (0.86 - _fromY) * press : _ctaReleasing ? Spring(_fromY, 1.07) : 1;
        if (elapsed >= DepthLaw.ReleaseMs * 1.6 * speed / 1000) _ctaReleasing = false;
        double travelElapsed = Math.Max(0, t - _travelAt);
        double travelP = Math.Clamp(travelElapsed / (DepthLaw.Ms(_ctaDown ? DepthLaw.PressMs : DepthLaw.HoverMs, Env.Level) / 1000d), 0, 1);
        _ctaTravel.Y = _ctaReleaseTravel
            ? ReleaseAt(travelElapsed / (DepthLaw.ReleaseMs * speed / 1000), _fromTravel, -DepthLaw.ReleaseOvershootPx, 0, 0.6)
            : _fromTravel + (_travelTarget - _fromTravel) * travelP;
        if (_ctaReleaseTravel && travelElapsed >= DepthLaw.ReleaseMs * speed / 1000)
        {
            _ctaReleaseTravel = false; _fromTravel = _travelTarget = 0;
        }
        double glowElapsed = Math.Max(0, t - _glowAt);
        double idleTime = double.IsFinite(_glowAt) ? Math.Max(0, glowElapsed - 0.26) : t;
        double idle = Env.AllowAmbientLoops ? 0.32 + 0.30 * BeatLoop.Breath(idleTime, 1.6) : 0.32;
        double land = Math.Clamp(1 - (t - _ctaLandAt) / (0.7 * speed), 0, 1);
        _ctaGlow.Opacity = _ctaHovered ? _fromGlow + (0.95 - _fromGlow) * Math.Clamp(glowElapsed / 0.16, 0, 1)
            : land > 0 ? idle + (0.95 - idle) * land
            : Env.AllowAmbientLoops && glowElapsed < 0.26 ? _fromGlow + (0.32 - _fromGlow) * (glowElapsed / 0.26) : idle;
        double shine = (t - _ctaShineAt) / speed;
        // WPF hover replaces the periodic track with ShineOnce, held at its end.
        // Leaving installs a fresh track whose first sweep starts 1.2 seconds later.
        if (shine >= 0.6 && !_ctaHovered && Env.AllowAmbientLoops)
        {
            double loop = t - _ctaSweepAt - 1.2;
            shine = loop >= 0 ? loop % 4.6 : double.PositiveInfinity;
        }
        _ctaShine.Opacity = shine >= 0 && shine < 0.6 ? 1 : 0;
        double q = Math.Clamp(shine / 0.6, 0, 1);
        q = q < 0.5 ? 2 * q * q : 1 - Math.Pow(-2 * q + 2, 2) / 2;
        _ctaShineMove.X = (-1.2 + 2.4 * q) * _ctaFace.Bounds.Width;
        RefreshCtaFx(_ctaOnScreen);
    }
    private static double QuadOut(double p) => 1 - (1 - p) * (1 - p);
    private static double QuadInOut(double p) => p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;
    // WPF EasingDoubleKeyFrame: first segment quadratic Out, final segment quadratic InOut.
    private static double ReleaseAt(double p, double from, double past, double end, double split) =>
        p < split ? from + (past - from) * QuadOut(Math.Clamp(p / split, 0, 1))
            : past + (end - past) * QuadInOut(Math.Clamp((p - split) / (1 - split), 0, 1));

}
