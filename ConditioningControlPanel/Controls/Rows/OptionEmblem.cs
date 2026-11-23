using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Controls.Rows
{
    /// <summary>
    /// Readable Rows (owner 2026-11-24): the small picture beside an option, chosen by what the option
    /// means. Draws <see cref="EmblemArt.Layers"/> as a sticker: shapes in the section hue, a white edge
    /// around the whole picture, tilted -4 degrees, a soft dark offset below. Depth law: NO Effect;
    /// the edge and the shadow are extra draw passes of the same frozen geometry. No clock, no loop.
    /// An OFF option draws grey at 45%.
    /// </summary>
    public class OptionEmblem : FrameworkElement
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(EmblemKind), typeof(OptionEmblem),
            new FrameworkPropertyMetadata(EmblemKind.Often, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SubjectProperty = DependencyProperty.Register(
            nameof(Subject), typeof(EmblemSubject), typeof(OptionEmblem),
            new FrameworkPropertyMetadata(EmblemSubject.Image, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(OptionEmblem),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
            nameof(IsOn), typeof(bool), typeof(OptionEmblem),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>The hue; transparent (default) = the inherited <see cref="RowHue"/>, then PinkBrush.</summary>
        public static readonly DependencyProperty HueProperty = DependencyProperty.Register(
            nameof(Hue), typeof(Color), typeof(OptionEmblem),
            new FrameworkPropertyMetadata(Colors.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly DependencyProperty FallbackBrushProperty = DependencyProperty.Register(
            "FallbackBrush", typeof(Brush), typeof(OptionEmblem),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        static OptionEmblem()
        {
            // The inherited section hue repaints the picture when it changes.
            RowHue.HueProperty.OverrideMetadata(typeof(OptionEmblem), new FrameworkPropertyMetadata(
                Colors.Transparent, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
        }

        public OptionEmblem()
        {
            SetResourceReference(FallbackBrushProperty, "PinkBrush");
            SnapsToDevicePixels = false;
        }

        public EmblemKind Kind { get => (EmblemKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public EmblemSubject Subject { get => (EmblemSubject)GetValue(SubjectProperty); set => SetValue(SubjectProperty, value); }
        /// <summary>Drives Many (count), Most (count) and Often (0..1 of a full turn).</summary>
        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
        public Color Hue { get => (Color)GetValue(HueProperty); set => SetValue(HueProperty, value); }

        /// <summary>The colour this emblem draws in right now (own, inherited, PinkBrush).</summary>
        public Color EffectiveHue => RowHue.Resolve(this, Hue, (Brush?)GetValue(FallbackBrushProperty));

        protected override Size MeasureOverride(Size availableSize)
        {
            double s = RowFonts.EmblemSize;
            return new Size(System.Math.Min(s, availableSize.Width), System.Math.Min(s, availableSize.Height));
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = System.Math.Min(RenderSize.Width, RenderSize.Height);
            if (size <= 0) return;
            double scale = size / 32.0;
            var parts = Shapes(Kind, Subject, Value);
            uint hue = RowHue.ToArgb(EffectiveHue);
            bool on = IsOn;

            var m = Matrix.Identity;
            m.Translate(-16, -16);
            m.Scale(scale, scale);
            m.Rotate(EmblemPalette.StickerTiltDegrees);
            m.Translate(RenderSize.Width / 2, RenderSize.Height / 2);
            var place = new MatrixTransform(m);
            place.Freeze();

            double edge = EmblemPalette.StickerEdgeDip / scale;   // grid units
            double dy = EmblemPalette.StickerShadowDyDip;          // DIP, straight down

            if (!on) dc.PushOpacity(EmblemPalette.OffEmblemOpacity);

            // Pass 1: the soft dark offset below (a wider faint halo, then the core).
            var shadow = RowHue.Frozen(EmblemPalette.StickerShadow);
            dc.PushTransform(new TranslateTransform(0, dy));
            dc.PushTransform(place);
            dc.PushOpacity(0.2);
            Silhouette(dc, parts, shadow, edge + 0.75 / scale);
            dc.Pop();
            dc.PushOpacity(0.35);
            Silhouette(dc, parts, shadow, edge);
            dc.Pop();
            dc.Pop();
            dc.Pop();

            dc.PushTransform(place);
            // Pass 2: the white edge around the whole picture.
            Silhouette(dc, parts, RowHue.Frozen(EmblemPalette.StickerEdge), edge);

            // Pass 3: the inks.
            foreach (var (geo, layer) in parts)
            {
                uint argb = on ? EmblemPalette.Ink(layer.Ink, hue, layer.FixedArgb) : EmblemPalette.OffInk(layer.Ink, hue, layer.FixedArgb);
                var brush = RowHue.Frozen(argb);
                if (layer.IsStroke) dc.DrawGeometry(null, MakePen(brush, layer.StrokeWidth, layer.RoundCaps), geo);
                else dc.DrawGeometry(brush, null, geo);
            }
            dc.Pop();

            if (!on) dc.Pop();
        }

        private static void Silhouette(DrawingContext dc, IReadOnlyList<(Geometry Geo, EmblemLayer Layer)> parts, Brush brush, double edge)
        {
            foreach (var (geo, layer) in parts)
            {
                if (layer.IsStroke) dc.DrawGeometry(null, MakePen(brush, layer.StrokeWidth + 2 * edge, layer.RoundCaps), geo);
                else dc.DrawGeometry(brush, MakePen(brush, 2 * edge, false), geo);
            }
        }

        private static Pen MakePen(Brush brush, double thickness, bool roundCaps)
        {
            var cap = roundCaps ? PenLineCap.Round : PenLineCap.Flat;
            var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = cap, EndLineCap = cap };
            pen.Freeze();
            return pen;
        }

        // ---------- geometry cache: per (kind, subject, value) as EmblemArt folds them, frozen ----------

        private static readonly object CacheGate = new();
        private static readonly Dictionary<(EmblemKind, EmblemSubject, double), (Geometry, EmblemLayer)[]> Cache = new();

        internal static IReadOnlyList<(Geometry Geo, EmblemLayer Layer)> Shapes(EmblemKind kind, EmblemSubject subject, double value)
        {
            var key = EmblemArt.ShapeKey(kind, subject, value);
            lock (CacheGate)
            {
                if (Cache.TryGetValue(key, out var hit)) return hit;
            }
            var layers = EmblemArt.Layers(key.Kind, key.Subject, key.Value);
            var built = new (Geometry, EmblemLayer)[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                // F1 = nonzero fill, the SVG default (WPF's mini-language defaults to even-odd).
                var g = Geometry.Parse("F1 " + layers[i].PathData);
                g.Freeze();
                built[i] = (g, layers[i]);
            }
            lock (CacheGate) { Cache[key] = built; }
            return built;
        }
    }
}
