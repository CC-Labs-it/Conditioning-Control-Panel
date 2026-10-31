using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/VaultCardAura.cs: the Premium page's "this one is yours"
    /// livery - a soft rim glow in the card's group colour that breathes, and now and then a four-point glint
    /// on the top-right corner. Pens and a gradient shape, never an Effect (Depth law; P82 rule 3).
    ///
    /// <para>WPF is an Adorner with its own opacity clocks; here it is a template part laid over the card
    /// (the card does not clip, so the halo spills outside it as the adorner's did) and the vault's one
    /// motion clock drives it through <see cref="Frame"/>. With no clock it rests at
    /// <see cref="StaticOpacity"/>, WPF's static rim.</para>
    /// </summary>
    public sealed class VaultCardAura : Control
    {
        internal const double BreathSeconds = 2.6;
        internal const double GlintMinSeconds = 5.0, GlintMaxSeconds = 9.0;
        internal const double StaticOpacity = 0.6;
        private const double GlintSize = 26, Radius = 12;
        private static readonly byte[] HaloAlphas = { 0x5A, 0x40, 0x2A, 0x18, 0x0C };

        public static readonly StyledProperty<Color> HueProperty =
            AvaloniaProperty.Register<VaultCardAura, Color>(nameof(Hue), Color.FromRgb(0xFF, 0xC8, 0x5A));

        public static readonly StyledProperty<int> SeedProperty =
            AvaloniaProperty.Register<VaultCardAura, int>(nameof(Seed));

        static VaultCardAura()
        {
            AffectsRender<VaultCardAura>(HueProperty);
            IsHitTestVisibleProperty.OverrideDefaultValue<VaultCardAura>(false);
        }

        public VaultCardAura() => Opacity = StaticOpacity;

        /// <summary>The group colour (Basic gold, Prime cyan, Free lilac).</summary>
        public Color Hue { get => GetValue(HueProperty); set => SetValue(HueProperty, value); }

        /// <summary>Spreads the clocks so a row of cards never breathes in step (WPF Start's seed).</summary>
        public int Seed { get => GetValue(SeedProperty); set => SetValue(SeedProperty, value); }

        private Pen[]? _halo;
        private Pen? _rim;
        private IBrush? _core;
        private static Geometry? _star;
        private (double Opacity, double Scale, double Turn) _glint = (0.0, 0.2, 0.0);

        /// <summary>The glint's current opacity (tests).</summary>
        internal double GlintOpacity => _glint.Opacity;

        /// <summary>True while the motion clock drives it (tests).</summary>
        internal bool Breathing { get; private set; }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == HueProperty) _halo = null;
        }

        /// <summary>One frame of WPF Start: Full breath 0.32..0.95; Reduced 0.5..0.72 at half speed.
        /// Sine, auto-reverse, begun -3*jitter early, with the Full-only glint (fade/grow/turn over 620 ms, once a period). <paramref name="t"/> is seconds since the clock started.</summary>
        internal void Frame(double t, bool full = true)
        {
            int seed = Seed;
            double jitter = Math.Abs(seed) % 7 * 0.13;
            Opacity = Views.Tabs.ExclusivesTabView.Breath(t + jitter * 3, (full ? BreathSeconds : BreathSeconds * 2) + jitter,
                full ? 0.32 : 0.5, full ? 0.95 : 0.72);
            Breathing = true;
            double period = GlintMinSeconds + Math.Abs(seed * 37) % 100 / 100.0 * (GlintMaxSeconds - GlintMinSeconds);
            double begin = 0.6 + Math.Abs(seed * 13) % 100 / 100.0 * 2.4;
            var glint = !full || t < begin ? (0.0, 0.2, 0.0) : GlintAt((t - begin) % period * 1000);
            if (glint == _glint) return;
            _glint = glint;
            InvalidateVisual();
        }

        /// <summary>WPF Stop: the clocks park and the static rim stays.</summary>
        internal void Rest()
        {
            Opacity = StaticOpacity;
            Breathing = false;
            if (_glint.Opacity == 0) return;
            _glint = (0.0, 0.2, 0.0);
            InvalidateVisual();
        }

        /// <summary>The glint's keyframes at <paramref name="ms"/> into its period (WPF Start: fade 0 -> 1 at 140 ms
        /// quadratic out -> 0 at 620 ms quadratic in; grow 0.2 -> 1.15 at 180 ms back-out(0.4) -> 0.5 at 620 ms; turn 0 -> 90).</summary>
        internal static (double Opacity, double Scale, double Turn) GlintAt(double ms)
        {
            if (ms >= 620) return (0, 0.5, 90);
            double opacity = ms < 140 ? 1 - Math.Pow(1 - ms / 140, 2) : 1 - Math.Pow((ms - 140) / 480, 2);
            double scale = ms < 180 ? 0.2 + 0.95 * BackOut(ms / 180, 0.4) : 1.15 - 0.65 * (ms - 180) / 440;
            return (opacity, scale, 90 * ms / 620);
        }

        /// <summary>WPF BackEase, EaseOut mode.</summary>
        internal static double BackOut(double x, double amplitude)
        {
            double u = 1 - x;
            return 1 - (u * u * u - u * amplitude * Math.Sin(Math.PI * u));
        }

        public override void Render(DrawingContext dc)
        {
            var size = Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return;
            if (_halo == null)
            {
                var h = Hue;
                _halo = new Pen[HaloAlphas.Length];
                for (int i = 0; i < _halo.Length; i++)
                    _halo[i] = new Pen(new SolidColorBrush(Color.FromArgb(HaloAlphas[i], h.R, h.G, h.B)), 3);
                _rim = new Pen(new SolidColorBrush(Color.FromArgb(0xC8, h.R, h.G, h.B)), 1.5);
                _core = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Colors.White, 0),
                        new GradientStop(Color.FromArgb(0xE0, h.R, h.G, h.B), 0.45),
                        new GradientStop(Color.FromArgb(0, h.R, h.G, h.B), 1),
                    },
                };
            }

            var r = new Rect(size);
            for (int i = _halo.Length - 1; i >= 0; i--)
            {
                double grow = 2 + i * 3;
                dc.DrawRectangle(null, _halo[i], r.Inflate(grow), Radius + grow, Radius + grow, default);
            }
            dc.DrawRectangle(null, _rim, r.Deflate(0.75), Radius, Radius, default);

            if (_glint.Opacity <= 0.001) return;
            // On the top-right corner, a little outside it, where a light would catch the edge.
            double half = GlintSize / 2;
            var m = Matrix.CreateTranslation(-half, -half)
                  * Matrix.CreateScale(_glint.Scale, _glint.Scale)
                  * Matrix.CreateRotation(_glint.Turn * Math.PI / 180)
                  * Matrix.CreateTranslation(size.Width - GlintSize * 0.62 + half, -GlintSize * 0.38 + half);
            using (dc.PushTransform(m))
            using (dc.PushOpacity(_glint.Opacity))
                dc.DrawGeometry(_core, null, _star ??= Star(GlintSize));
        }

        /// <summary>A four-point sparkle: concave arms meeting in a bright centre (WPF Star).</summary>
        internal static Geometry Star(double size)
        {
            double c = size / 2, r = size / 2, k = size * 0.09;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c, c - r), true);
                ctx.QuadraticBezierTo(new Point(c + k, c - k), new Point(c + r, c), true);
                ctx.QuadraticBezierTo(new Point(c + k, c + k), new Point(c, c + r), true);
                ctx.QuadraticBezierTo(new Point(c - k, c + k), new Point(c - r, c), true);
                ctx.QuadraticBezierTo(new Point(c - k, c - k), new Point(c, c - r), true);
                ctx.EndFigure(true);
            }
            return g;
        }
    }
}
