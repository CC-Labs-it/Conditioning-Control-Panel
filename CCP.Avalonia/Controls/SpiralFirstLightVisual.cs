using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Serilog;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>WPF SpiralFirstLightVisual drawing port: immutable ramps and gradients, fixed seed,
    /// one paint pass driven by the existing Core timeline. This control owns no clock.</summary>
    public sealed class SpiralFirstLightVisual : Control
    {

        private const int RampSteps = 32;

        private static readonly Color Hot = Color.FromRgb(0xFF, 0xD9, 0xEC);

        private static readonly IBrush[] HotRamp = BuildRamp(Hot);

        private readonly IBrush[] _accentRamp;
        private readonly IPen[] _armPens;

        private static readonly IBrush Backdrop = BuildBackdrop();

        private readonly IBrush _fogBrush;
        private readonly IBrush _bloomBrush;

        private static IBrush[] BuildRamp(Color color)
        {
            var ramp = new IBrush[RampSteps + 1];
            for (int i = 0; i <= RampSteps; i++)
            {
                var a = (byte)Math.Clamp(Math.Round(255.0 * i / RampSteps), 0, 255);
                var b = new ImmutableSolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
                ramp[i] = b;
            }
            return ramp;
        }

        private static IPen[] BuildPens(IBrush[] ramp, double thickness)
        {
            var pens = new IPen[ramp.Length];
            for (int i = 0; i < ramp.Length; i++)
            {
                var p = new ImmutablePen((IImmutableBrush)ramp[i], thickness, lineCap: PenLineCap.Round);
                pens[i] = p;
            }
            return pens;
        }

        private static IBrush BuildBackdrop()
        {
            var b = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            };
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x24, 0x10, 0x41), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x1A, 0x1A, 0x2E), 0.55));
            b.GradientStops.Add(new GradientStop(Color.FromRgb(0x0A, 0x05, 0x14), 1.0));
            return b.ToImmutable();
        }

        private static int Step(double alpha)
        {
            if (double.IsNaN(alpha) || alpha <= 0) return 0;
            if (alpha >= 1) return RampSteps;
            return (int)(alpha * RampSteps);
        }


        private const int ArmSamples = 200;

        private const double ArmTurns = 3.0;

        private const double ArmRadius = 0.78;

        private readonly double[] _armAngle = new double[ArmSamples + 1];
        private readonly double[] _armRadius = new double[ArmSamples + 1];

        private readonly double[] _fogPhase = new double[SpiralFirstLightTimeline.FogBlobs];
        private readonly double[] _fogRate = new double[SpiralFirstLightTimeline.FogBlobs];
        private readonly double[] _fogOrbit = new double[SpiralFirstLightTimeline.FogBlobs];
        private readonly double[] _fogSize = new double[SpiralFirstLightTimeline.FogBlobs];
        private readonly double[] _fogAlpha = new double[SpiralFirstLightTimeline.FogBlobs];

        private readonly double[] _emberX = new double[SpiralFirstLightTimeline.EmberCount];
        private readonly double[] _emberSpeed = new double[SpiralFirstLightTimeline.EmberCount];
        private readonly double[] _emberDrift = new double[SpiralFirstLightTimeline.EmberCount];
        private readonly double[] _emberSize = new double[SpiralFirstLightTimeline.EmberCount];
        private readonly double[] _emberSeed = new double[SpiralFirstLightTimeline.EmberCount];


        private bool _reduced;
        private double _elapsed;
        private double _ambient;

        internal double ElapsedSeconds => _elapsed;
        internal double AmbientSeconds => _ambient;

        public SpiralFirstLightVisual(Color accent)
        {
            IsHitTestVisible = false;
            UseLayoutRounding = true;

            _accentRamp = BuildRamp(accent);
            _armPens = BuildPens(_accentRamp, 3.2);

            var fog = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            };
            fog.GradientStops.Add(new GradientStop(Color.FromArgb(0x5C, accent.R, accent.G, accent.B), 0.0));
            fog.GradientStops.Add(new GradientStop(Color.FromArgb(0x24, accent.R, accent.G, accent.B), 0.45));
            fog.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
            _fogBrush = fog.ToImmutable();

            var bloom = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
            };
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0xD2, accent.R, accent.G, accent.B), 0.0));
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0x78, accent.R, accent.G, accent.B), 0.32));
            bloom.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1.0));
            _bloomBrush = bloom.ToImmutable();

            for (int i = 0; i <= ArmSamples; i++)
            {
                var t = i / (double)ArmSamples;
                _armAngle[i] = t * ArmTurns * Math.PI * 2;
                _armRadius[i] = ArmRadius * t;
            }

            var rng = new Random(0x11F1A57);

            for (int i = 0; i < SpiralFirstLightTimeline.FogBlobs; i++)
            {
                _fogPhase[i] = rng.NextDouble() * Math.PI * 2;
                _fogRate[i] = 0.16 + rng.NextDouble() * 0.22;     // radians/second, all slow
                _fogOrbit[i] = 0.20 + rng.NextDouble() * 0.34;    // fraction of the short half-side
                _fogSize[i] = 0.70 + rng.NextDouble() * 0.65;
                _fogAlpha[i] = 0.55 + rng.NextDouble() * 0.45;
            }

            for (int i = 0; i < SpiralFirstLightTimeline.EmberCount; i++)
            {
                _emberX[i] = rng.NextDouble();                    // 0..1 across the window
                _emberSpeed[i] = 0.10 + rng.NextDouble() * 0.16;  // fraction of height per second
                _emberDrift[i] = (rng.NextDouble() - 0.5) * 0.05;
                _emberSize[i] = 1.0 + rng.NextDouble() * 1.7;
                _emberSeed[i] = rng.NextDouble();
            }
        }


        public void Begin(bool reducedMotion) => _reduced = reducedMotion;

        public void SetFrame(double elapsedSeconds, double ambientSeconds)
        {
            _elapsed = elapsedSeconds;
            _ambient = ambientSeconds;
            InvalidateVisual();
        }


        public override void Render(DrawingContext dc)
        {
            try
            {
                double w = Bounds.Width, h = Bounds.Height;
                if (w <= 1 || h <= 1) return;

                var layer = SpiralFirstLightTimeline.LayerOpacity(_elapsed, _reduced);
                if (layer <= 0.004) return;

                var centre = new Point(w / 2, h / 2);
                double unit = Math.Min(w, h) / 2.0;

                using var layerOpacity = dc.PushOpacity(layer);

                dc.DrawRectangle(Backdrop, null, new Rect(0, 0, w, h));

                DrawFog(dc, centre, w, h);
                DrawArm(dc, centre, unit);
                DrawEmbers(dc, w, h);
                DrawBloom(dc, centre, unit);

            }
            catch (Exception ex)
            {
                Log.Debug("[Spiral] first-light frame failed: {E}", ex.Message);
            }
        }


        private void DrawFog(DrawingContext dc, Point centre, double w, double h)
        {
            var envelope = SpiralFirstLightTimeline.FogOpacity(_elapsed, _reduced);
            if (envelope <= 0.004) return;

            var spread = Math.Max(w, h) * 0.55;

            for (int i = 0; i < SpiralFirstLightTimeline.FogBlobs; i++)
            {
                var angle = _fogPhase[i] + _ambient * _fogRate[i];
                var orbit = _fogOrbit[i] * spread;

                var cx = centre.X + Math.Cos(angle) * orbit;
                var cy = centre.Y + Math.Sin(angle * 0.78) * orbit * 0.72;

                var breathe = 1.0 + 0.14 * Math.Sin(_ambient * 0.55 + _fogPhase[i]);
                var radius = spread * 0.52 * _fogSize[i] * breathe;

                using var fogOpacity = dc.PushOpacity(envelope * _fogAlpha[i]);
                dc.DrawEllipse(_fogBrush, null, new Point(cx, cy), radius, radius);
            }
        }


        private void DrawArm(DrawingContext dc, Point centre, double unit)
        {
            var drawn = SpiralFirstLightTimeline.DrawFraction(_elapsed, _reduced);
            if (drawn <= 0) return;

            var swirl = SpiralFirstLightTimeline.SwirlAngle(_elapsed, _reduced);

            int last = (int)Math.Round(drawn * ArmSamples);
            if (last < 1) last = 1;

            var prev = PointOn(0, centre, unit, swirl);
            for (int i = 1; i <= last && i <= ArmSamples; i++)
            {
                var next = PointOn(i, centre, unit, swirl);

                var behind = (last - i) / (double)ArmSamples;
                var a = 0.58 + 0.42 * Math.Max(0, 1.0 - behind * 9.0);
                dc.DrawLine(_armPens[Step(a)], prev, next);

                prev = next;
            }

            if (drawn < 1.0)
            {
                var head = PointOn(last, centre, unit, swirl);
                dc.DrawEllipse(HotRamp[Step(0.75)], null, head, unit * 0.014, unit * 0.014);
                dc.DrawEllipse(_accentRamp[Step(0.30)], null, head, unit * 0.038, unit * 0.038);
            }

            foreach (var at in SpiralFirstLightTimeline.DotAt)
            {
                var g = SpiralFirstLightTimeline.DotGlow(_elapsed, at, _reduced);
                if (g <= 0.004) continue;

                int idx = (int)Math.Round(at * ArmSamples);
                if (idx > ArmSamples) idx = ArmSamples;
                var pt = PointOn(idx, centre, unit, swirl);

                dc.DrawEllipse(_accentRamp[Step(0.28 * g)], null, pt, unit * (0.007 + 0.022 * g), unit * (0.007 + 0.022 * g));
                dc.DrawEllipse(HotRamp[Step(g)], null, pt, unit * 0.009, unit * 0.009);
            }
        }

        private Point PointOn(int i, Point centre, double unit, double swirl)
        {
            var a = _armAngle[i] + swirl;
            var r = _armRadius[i] * unit;
            return new Point(centre.X + Math.Cos(a) * r, centre.Y + Math.Sin(a) * r);
        }


        private void DrawEmbers(DrawingContext dc, double w, double h)
        {
            var envelope = SpiralFirstLightTimeline.EmberOpacity(_elapsed, _reduced);
            if (envelope <= 0.004) return;

            for (int i = 0; i < SpiralFirstLightTimeline.EmberCount; i++)
            {
                var climb = (_emberSeed[i] + _ambient * _emberSpeed[i]) % 1.0;

                var y = h * (1.05 - climb * 1.15);
                var x = w * _emberX[i] + Math.Sin(_ambient * 0.9 + _emberSeed[i] * 6.28) * w * _emberDrift[i];

                var edge = Math.Min(1.0, Math.Min(climb * 6.0, (1.0 - climb) * 3.0));
                var a = envelope * edge * (0.35 + 0.5 * _emberSeed[i]);
                if (a <= 0.004) continue;

                dc.DrawEllipse(HotRamp[Step(a)], null, new Point(x, y), _emberSize[i], _emberSize[i]);
            }
        }


        private void DrawBloom(DrawingContext dc, Point centre, double unit)
        {
            var b = SpiralFirstLightTimeline.BloomOpacity(_elapsed, _reduced);
            if (b <= 0.004) return;

            var radius = unit * (0.9 + 1.8 * b);

            using (dc.PushOpacity(Math.Min(1.0, b * 1.15)))
                dc.DrawEllipse(_bloomBrush, null, centre, radius, radius);

            var core = Math.Max(0, 1.0 - b);
            if (core > 0.01)
                dc.DrawEllipse(HotRamp[Step(core * 0.6)], null, centre, unit * 0.04 * core, unit * 0.04 * core);
        }
    }
}
