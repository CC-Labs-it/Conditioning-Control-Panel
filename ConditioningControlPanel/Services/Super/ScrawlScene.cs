using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super Scrawl's drawing: wall stamps, wall glows, slam shockwaves, ink sparks and the corner
    /// label, in virtual-desktop DIP. The live state is Core's <see cref="ScrawlField"/> (shared with
    /// the Linux head); BouncingTextService feeds it hits and the frame clock, and every
    /// <see cref="ScrawlLayer"/> (one per bouncing-text window, under the words) draws it. Geometry is
    /// built and frozen once per stamp, brushes and pens once per scene, so a frame only pushes
    /// opacities. UI thread only.
    /// </summary>
    internal sealed class ScrawlScene
    {
        /// <summary>The frozen glyph and ink-dot geometry of one stamp, cached on <see cref="ScrawlStamp.Look"/>.</summary>
        private sealed class Look
        {
            public Geometry Text = null!;
            public Geometry? Dots;
            public readonly MatrixTransform Body = new(), Echo = new();   // reused every frame, never reallocated
        }

        private const int Gold = ScrawlField.Gold;

        private static readonly Color[] Inks =
        {
            Color.FromRgb(255, 111, 181), Color.FromRgb(95, 255, 208), Color.FromRgb(190, 150, 255),
            Color.FromRgb(255, 207, 107), Colors.White,
        };
        private static readonly Brush[] FillBrushes = Frozen(c => new SolidColorBrush(Alpha(c, ScrawlRules.FillAlpha)));
        private static readonly Brush[] DotBrushes = Frozen(c => new SolidColorBrush(Alpha(c, ScrawlRules.DotAlpha)));
        private static readonly Brush[] SparkBrushes = Frozen(c => new SolidColorBrush(c));
        private static readonly Brush[] GlowBrushes = Frozen(c => new RadialGradientBrush(Alpha(c, ScrawlRules.GlowAlpha), Alpha(c, 0)));
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 226, 154)));
        private static readonly Brush BeadBrush = Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)));

        private readonly Pen[] _strokePens, _slamPens, _haloPens, _dripPens;
        private readonly Pen _wavePen0, _wavePen1;
        private readonly ScrawlField _f;
        private Geometry? _label;                                   // the CORNER label, built once per corner hit
        private int _labelSerial;
        private readonly MatrixTransform _labelMove = new();        // where it floats this frame, and its entrance scale
        private readonly Typeface _typeface;
        private readonly double _fs, _k, _ppd;

        /// <summary>The live state (tests).</summary>
        internal ScrawlField Field => _f;

        /// <summary>
        /// The option box, read live by the service: how long a wall stamp stays (s), how many
        /// stamps the walls hold, and the slam's shake multiplier. A lower cap drops the oldest at once.
        /// </summary>
        public void Configure(double stampLife, int stampCap, double slamShake, bool cornerLabel = true)
            => _f.Configure(stampLife, stampCap, slamShake, cornerLabel);

        /// <summary>Wall stamp life in seconds (tests).</summary>
        internal double StampLife => _f.StampLife;

        /// <summary>The shake offset for this frame, in DIP. The service moves the windows' canvas by it.</summary>
        public double ShakeX => _f.ShakeX;
        public double ShakeY => _f.ShakeY;

        /// <summary>Sparks in flight (tests).</summary>
        internal int LiveSparks => _f.LiveSparks;

        /// <summary>Anything left to draw or shake. Layers stop invalidating once this goes false.</summary>
        public bool IsLive => _f.IsLive;

        public ScrawlScene(FontFamily family, double fontSize, double pixelsPerDip)
        {
            _typeface = new Typeface(family, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            _ppd = pixelsPerDip <= 0 ? 1 : pixelsPerDip;
            _f = new ScrawlField(fontSize, Measure, () => Localization.Loc.Get("super_scrawl_corner"));
            _fs = _f.FontSize;
            _k = _f.K;
            _strokePens = Pens(ScrawlRules.StrokeWidth * _k, ScrawlRules.StrokeAlpha);
            _slamPens = Pens(ScrawlRules.SlamStrokeWidth * _k, ScrawlRules.StrokeAlpha);
            _haloPens = Pens(ScrawlRules.SlamStrokeWidth * _k * 3, 0.22);
            _dripPens = Pens(0.9 * _k, 0.5);
            _wavePen0 = Freeze(new Pen(new SolidColorBrush(Alpha(Inks[1], 0.6)), 3 * _k));
            _wavePen1 = Freeze(new Pen(new SolidColorBrush(Alpha(Inks[1], 0.6)), 2 * _k));
        }

        /// <summary>A word hit a wall. A corner (both axes near) gets the gold stamp; anything else a wall stamp.</summary>
        public void WallHit(string word, ScrawlWall wall, double left, double top, double right, double bottom,
            ScrawlBounds b, Point labelAt) => _f.WallHit(word, wall, left, top, right, bottom, b, labelAt.X, labelAt.Y);

        /// <summary>A slammed word arrived at the centre: the huge stamp, the shockwave, the shake, every stamp flares.</summary>
        public void SlamLanded(string word, double x, double y, double screenWidth) => _f.SlamLanded(word, x, y, screenWidth);

        /// <summary>Advance every timer by dt. <paramref name="motion"/> and <paramref name="photosafe"/> are read once per frame by the caller.</summary>
        public void Tick(double dt, MotionLevel motion, bool photosafe = false) => _f.Tick(dt, motion, photosafe);

        /// <summary>Draw in virtual DIP. The layer's own transform moves it into its window.</summary>
        public void Render(DrawingContext dc)
        {
            foreach (var s in _f.Stamps)
            {
                var k = LookOf(s);
                double life = s.Slam ? ScrawlRules.SlamStampLife : _f.StampLife;
                bool leftover = !s.Slam;
                // Leftovers hold, then let go (ease-in); the short-lived slam stamp keeps the mockup's linear fade.
                double al = leftover ? ScrawlRules.LeftoverAlpha(s.Age, life, s.Pulse) : ScrawlRules.StampAlpha(s.Age, life, s.Pulse);
                if (al <= 0.003) continue;
                double melt = leftover ? ScrawlRules.MeltAmount(s.Age, life, _f.Motion) : 0;
                var (along, across) = ScrawlRules.LandPunch(s.Age, s.Slam, _f.Motion);
                double breath = leftover ? ScrawlRules.IdleBreath(s.Age, s.Phase, _f.Motion) : 1;
                if (leftover && ScrawlRules.EchoAt(s.Age, _f.Motion, out double es, out double ea))
                {
                    // The one-off ghost echo, out from where the stamp landed.
                    k.Echo.Matrix = new Matrix(es, 0, 0, es, s.X - es * s.X, s.Y - es * s.Y);
                    dc.PushTransform(k.Echo);
                    dc.PushOpacity(al * ea);
                    dc.DrawGeometry(null, _strokePens[s.Ink], k.Text);
                    dc.Pop();
                    dc.Pop();
                }
                bool moved = SetBody(s, k, along * breath, across * breath, melt);
                if (moved) dc.PushTransform(k.Body);
                if (leftover)
                {
                    // Afterglow: a halo that settles into a slow breath, phased per stamp.
                    dc.PushOpacity(al * ScrawlRules.AfterglowStrength(s.Age, life, _f.Motion, s.Phase));
                    dc.DrawGeometry(null, _haloPens[s.Ink], k.Text);
                    dc.Pop();
                }
                dc.PushOpacity(al);
                if (s.Slam || s.Gold) dc.DrawGeometry(null, _haloPens[s.Ink], k.Text);
                dc.DrawGeometry(FillBrushes[s.Ink], s.Slam ? _slamPens[s.Ink] : _strokePens[s.Ink], k.Text);
                if (k.Dots != null) dc.DrawGeometry(DotBrushes[s.Ink], null, k.Dots);
                dc.Pop();
                if (moved) dc.Pop();
                if (leftover && melt > 0.001) DrawDrips(dc, s, life, al);
            }
            double gr = ScrawlRules.GlowRadius * _fs;
            foreach (var g in _f.Glows)
            {
                dc.PushOpacity(ScrawlRules.GlowAlphaAt(g.Age));
                dc.DrawEllipse(GlowBrushes[g.Ink], null, new Point(g.X, g.Y), gr, gr);
                dc.Pop();
            }
            foreach (var w in _f.Waves)
            {
                double u = w.Age / ScrawlRules.WaveLife;
                for (int j = 0; j < 2; j++)
                {
                    double uu = Math.Clamp(u - j * ScrawlRules.WaveLag, 0, 1);
                    if (uu <= 0) continue;
                    dc.PushOpacity(1 - uu);
                    dc.DrawEllipse(null, j == 0 ? _wavePen0 : _wavePen1, new Point(w.X, w.Y), uu * w.Reach, uu * w.Reach);
                    dc.Pop();
                }
            }
            foreach (ref readonly var p in _f.Sparks)
            {
                double a = 1 - p.T / p.Life;
                dc.PushOpacity(a);
                double r = (1 + 2.2 * a) * _k;
                dc.DrawEllipse(SparkBrushes[p.Ink], null, new Point(p.X, p.Y), r, r);
                dc.Pop();
            }
            var label = LabelGeometry();
            if (label != null && _f.LabelAge >= 0)
            {
                double u = _f.LabelAge / ScrawlRules.FloatLife;
                double ls = ScrawlRules.LabelScale(_f.LabelAge, _f.Motion);
                _labelMove.Matrix = new Matrix(ls, 0, 0, ls, _f.LabelX, _f.LabelY - u * _fs * 2);
                dc.PushTransform(_labelMove);
                dc.PushOpacity(Math.Min(1, _f.LabelAge / ScrawlRules.LabelAlphaIn) * (1 - u * u));
                dc.DrawGeometry(null, _haloPens[Gold], label);   // the mockup's gold glow
                dc.DrawGeometry(LabelBrush, null, label);
                dc.Pop();
                dc.Pop();
            }
        }

        private void DrawDrips(DrawingContext dc, ScrawlStamp s, double life, double al)
        {
            for (int i = 0; i + 2 < s.Drips.Length; i += 3)
            {
                double t = ScrawlRules.DripProgress(s.Age, life, s.Drips[i + 1], _f.Motion);
                if (t <= 0) continue;
                double fall = t * t * ScrawlRules.DripReach * _fs * s.Drips[i + 2];
                double x0 = s.X + s.Drips[i] * s.HalfW, y0 = s.Y + s.HalfH * 0.55;
                double x = x0 + ScrawlRules.DripSway(s.Age, s.Phase + i, t, _f.Motion) * _fs;
                double y = y0 + fall;
                double rx = 0.07 * _fs * (1 - 0.5 * t), ry = rx * (1 + 1.8 * t);
                dc.PushOpacity(al * (1 - t * 0.6));
                if (t < 0.6 && fall > ry)
                {
                    // The thin strand the drop still hangs from, thinning out as it lets go.
                    dc.PushOpacity(1 - t / 0.6);
                    dc.DrawLine(_dripPens[s.Ink], new Point(x0, y0), new Point(x, y - ry));
                    dc.Pop();
                }
                dc.DrawEllipse(DotBrushes[s.Ink], null, new Point(x, y), rx, ry);
                dc.DrawEllipse(BeadBrush, null, new Point(x - rx * 0.35, y - ry * 0.35), rx * 0.32, ry * 0.26);   // bead highlight
                dc.Pop();
            }
        }

        /// <summary>Sets the stamp's reused body transform: melt (stretch, then sag), then the landing punch and breath in stamp space. False = nothing to push.</summary>
        private bool SetBody(ScrawlStamp s, Look k, double along, double across, double melt)
        {
            bool punch = Math.Abs(along - 1) > 1e-4 || Math.Abs(across - 1) > 1e-4, melting = melt > 0.001;
            if (!punch && !melting) return false;
            var m = Matrix.Identity;
            if (melting)
            {
                m.ScaleAt(1, 1 + ScrawlRules.MeltStretch * melt, s.X, s.Y - s.HalfH);
                m.Translate(0, melt * ScrawlRules.MeltSag * _fs);
            }
            if (punch)
            {
                // Stamp space: the text runs along local X, the wall normal is local Y.
                double deg = s.Rot * 180 / Math.PI;
                m.Translate(-s.X, -s.Y);
                m.Rotate(-deg);
                m.Scale(across, along);
                m.Rotate(deg);
                m.Translate(s.X, s.Y);
            }
            k.Body.Matrix = m;
            return true;
        }

        /// <summary>Every stamp's box, for the OCR self-exclusion list (the stamps are the player's text too).</summary>
        public void AppendRects(List<Rect> into)
        {
            var boxes = new List<(double X, double Y, double W, double H)>();
            _f.AppendRects(boxes);
            foreach (var r in boxes) into.Add(new Rect(r.X, r.Y, r.W, r.H));
        }

        public void Clear() { _f.Clear(); _label = null; }

        private FormattedText Text(string word, double size) =>
            new(word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface, size, Brushes.White, _ppd);

        private (double W, double H) Measure(string word, double size)
        {
            var ft = Text(word, size);
            return (ft.Width, ft.Height);
        }

        /// <summary>The stamp's glyphs and ink dots, built and frozen on first draw.</summary>
        private Look LookOf(ScrawlStamp s)
        {
            if (s.Look is Look k) return k;
            var ft = Text(s.Word, s.Size);
            var place = new TransformGroup();
            place.Children.Add(new RotateTransform(s.Rot * 180 / Math.PI));
            place.Children.Add(new TranslateTransform(s.X, s.Y));
            place.Freeze();
            var text = ft.BuildGeometry(new Point(-ft.Width / 2, -ft.Height / 2));
            text.Transform = place;
            text.Freeze();
            GeometryGroup? dotGeo = null;
            if (s.Dots.Length > 0)
            {
                dotGeo = new GeometryGroup { Transform = place };
                foreach (var q in s.Dots) dotGeo.Children.Add(new EllipseGeometry(new Point(q.X, q.Y), q.R, q.R));
                dotGeo.Freeze();
            }
            k = new Look { Text = text, Dots = dotGeo };
            s.Look = k;
            return k;
        }

        private Geometry? LabelGeometry()
        {
            if (_f.LabelText == null) return _label = null;
            if (_label != null && _labelSerial == _f.LabelSerial) return _label;
            var ft = new FormattedText(_f.LabelText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface,
                _f.LabelSize, LabelBrush, _ppd);
            _label = ft.BuildGeometry(new Point(-ft.Width / 2, -ft.Height / 2));
            _label.Freeze();
            _labelSerial = _f.LabelSerial;
            return _label;
        }

        private Pen[] Pens(double width, double alpha)
        {
            var pens = new Pen[Inks.Length];
            for (int i = 0; i < pens.Length; i++)
                pens[i] = Freeze(new Pen(new SolidColorBrush(Alpha(Inks[i], alpha)), width) { LineJoin = PenLineJoin.Round });
            return pens;
        }

        private static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

        private static Brush[] Frozen(Func<Color, Brush> make)
        {
            var arr = new Brush[Inks.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = Freeze(make(Inks[i]));
            return arr;
        }

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }

    /// <summary>
    /// The click-through surface one bouncing-text window draws the Scrawl scene on. It sits first
    /// in the window's canvas, so the stamps are always under the words.
    /// </summary>
    internal sealed class ScrawlLayer : FrameworkElement
    {
        private readonly ScrawlScene _scene;

        public ScrawlLayer(ScrawlScene scene, double originX, double originY)
        {
            _scene = scene;
            IsHitTestVisible = false;
            Focusable = false;
            RenderTransform = Freeze(new TranslateTransform(-originX, -originY));
        }

        protected override void OnRender(DrawingContext dc) => _scene.Render(dc);

        private static Transform Freeze(Transform t) { t.Freeze(); return t; }
    }
}
