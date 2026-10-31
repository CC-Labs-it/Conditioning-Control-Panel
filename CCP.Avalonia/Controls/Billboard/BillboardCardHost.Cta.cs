using System;
using Avalonia;
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
    private bool _ctaHovered, _ctaDown, _ctaReleasing, _ctaOnScreen;
    private double _ctaNow, _ctaInputAt, _ctaShineAt = double.NegativeInfinity, _ctaLandAt = double.NegativeInfinity;
    private double _fromScale = 1, _fromX = 1, _fromY = 1, _fromTravel;

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
        _ctaInputAt = _ctaNow;
        CaptureCtaPose();
    }

    private void CaptureCtaPose()
    {
        _fromScale = _ctaScale.ScaleX; _fromX = _ctaSquash.ScaleX;
        _fromY = _ctaSquash.ScaleY; _fromTravel = _ctaTravel.Y;
    }

    internal void SetCtaHover(bool on)
    {
        CaptureCtaPose(); _ctaInputAt = _ctaNow; _ctaHovered = on;
        if (on && Env.AllowTransitions) _ctaShineAt = _ctaNow;
        RefreshCtaFx(_ctaOnScreen);
    }

    internal void SetCtaPressed(bool down)
    {
        if (_ctaDown == down) return; // release and capture-lost may both arrive for one press
        CaptureCtaPose(); _ctaInputAt = _ctaNow; _ctaReleasing = _ctaDown && !down; _ctaDown = down;
        RefreshCtaFx(_ctaOnScreen);
    }

    private void RefreshCtaFx(bool onScreen)
    {
        _ctaOnScreen = onScreen;
        if (!Env.AllowTransitions)
        {
            _ctaLandAt = _ctaShineAt = double.NegativeInfinity;
            _ctaScale.ScaleX = _ctaScale.ScaleY = _ctaSquash.ScaleX = _ctaSquash.ScaleY = 1;
            _ctaTravel.Y = 0;
        }
        bool oneShot = Env.AllowTransitions && (_ctaNow - _ctaInputAt < 0.3 ||
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
        double elapsed = Math.Max(0, t - _ctaInputAt);
        double speed = Env.Level == global::ConditioningControlPanel.Models.MotionLevel.Reduced ? 0.5 : 1;
        double p = Math.Clamp(elapsed / ((_ctaDown ? DepthLaw.PressMs : 260) * speed / 1000), 0, 1);
        double ease = _ctaDown ? p : 1 - Math.Pow(1 - p, 3);
        double Mix(double a, double b) => a + (b - a) * ease;
        _ctaScale.ScaleX = _ctaScale.ScaleY = Mix(_fromScale, _ctaHovered ? 1.05 : 1);
        double travelP = Math.Clamp(elapsed / (DepthLaw.Ms(_ctaDown ? DepthLaw.PressMs : DepthLaw.HoverMs, Env.Level) / 1000d), 0, 1);
        double travel = DepthLaw.TravelFor(true, _ctaDown, false, _ctaHovered);
        _ctaTravel.Y = _ctaReleasing ? BeatLoop.Keys(elapsed, new[]
        {
            (0d, _fromTravel), (DepthLaw.ReleaseMs * 0.6 * speed / 1000, -DepthLaw.ReleaseOvershootPx),
            (DepthLaw.ReleaseMs * speed / 1000, 0d),
        }, sine: true) : _fromTravel + (travel - _fromTravel) * travelP;
        double Spring(double from, double past) => BeatLoop.Keys(elapsed,
            new[] { (0d, from), (DepthLaw.ReleaseMs * 1.6 * 0.45 * speed / 1000, past), (DepthLaw.ReleaseMs * 1.6 * speed / 1000, 1d) }, sine: true);
        _ctaSquash.ScaleX = _ctaDown ? Mix(_fromX, 1.07) : _ctaReleasing ? Spring(_fromX, 0.95) : 1;
        _ctaSquash.ScaleY = _ctaDown ? Mix(_fromY, 0.86) : _ctaReleasing ? Spring(_fromY, 1.07) : 1;
        double idle = Env.AllowAmbientLoops ? 0.32 + 0.30 * BeatLoop.Breath(t, 1.6) : 0.32;
        double land = Math.Clamp(1 - (t - _ctaLandAt) / (0.7 * speed), 0, 1);
        _ctaGlow.Opacity = _ctaHovered ? 0.95 : idle + (0.95 - idle) * land;
        double shine = (t - _ctaShineAt) / speed;
        if (shine >= 0.6 && Env.AllowAmbientLoops) shine = (t + 4.6 - 1.2) % 4.6;
        _ctaShine.Opacity = shine >= 0 && shine < 0.6 ? 1 : 0;
        double q = Math.Clamp(shine / 0.6, 0, 1);
        q = q < 0.5 ? 2 * q * q : 1 - Math.Pow(-2 * q + 2, 2) / 2;
        _ctaShineMove.X = (-1.2 + 2.4 * q) * _ctaFace.Bounds.Width;
        RefreshCtaFx(_ctaOnScreen);
    }
}
