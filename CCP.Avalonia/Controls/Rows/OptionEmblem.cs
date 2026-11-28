using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Avalonia.Controls.Rows
{
    /// <summary>
    /// Readable Rows (owner 2026-11-24), the Avalonia twin of WPF <c>Controls/Rows/OptionEmblem.cs</c>:
    /// the small picture beside an option, chosen by what the option means. Draws
    /// <see cref="EmblemArt.Layers"/> as a sticker: shapes in the section hue, an edge around the whole
    /// picture (<see cref="RowHue.EdgeProperty"/>, shipped = <see cref="EmblemPalette.ShippedEdge"/>),
    /// tilted -4 degrees, a soft dark offset below. Depth law: NO Effect; the edge and the shadow are
    /// extra draw passes of the same cached geometry. No clock, no loop. An OFF option draws grey at 45%.
    /// </summary>
    public class OptionEmblem : Control
    {
        public static readonly StyledProperty<EmblemKind> KindProperty =
            AvaloniaProperty.Register<OptionEmblem, EmblemKind>(nameof(Kind), EmblemKind.Often);

        public static readonly StyledProperty<EmblemSubject> SubjectProperty =
            AvaloniaProperty.Register<OptionEmblem, EmblemSubject>(nameof(Subject), EmblemSubject.Image);

        public static readonly StyledProperty<double> ValueProperty =
            AvaloniaProperty.Register<OptionEmblem, double>(nameof(Value));

        public static readonly StyledProperty<bool> IsOnProperty =
            AvaloniaProperty.Register<OptionEmblem, bool>(nameof(IsOn), true);

        /// <summary>The hue; transparent (default) = the inherited <see cref="RowHue"/>, then PinkBrush.</summary>
        public static readonly StyledProperty<Color> HueProperty =
            AvaloniaProperty.Register<OptionEmblem, Color>(nameof(Hue), Colors.Transparent);

        static OptionEmblem()
        {
            // The inherited section hue and the inherited sticker edge (ccp-bugs #1742) repaint it too.
            AffectsRender<OptionEmblem>(KindProperty, SubjectProperty, ValueProperty, IsOnProperty, HueProperty,
                RowHue.HueProperty, RowHue.EdgeProperty);
        }

        public EmblemKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
        public EmblemSubject Subject { get => GetValue(SubjectProperty); set => SetValue(SubjectProperty, value); }
        /// <summary>Drives Many (count), Most (count) and Often (0..1 of a full turn).</summary>
        public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public bool IsOn { get => GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
        public Color Hue { get => GetValue(HueProperty); set => SetValue(HueProperty, value); }

        /// <summary>The colour this emblem draws in right now (own, inherited, PinkBrush).</summary>
        public Color EffectiveHue => RowHue.Resolve(this, Hue, RowHue.FindBrush(this, "PinkBrush"));

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            // A theme change can swap PinkBrush, the fallback hue.
            if (change.Property == ThemeVariantScope.ActualThemeVariantProperty) InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double s = RowFonts.EmblemSize;
            return new Size(Math.Min(s, availableSize.Width), Math.Min(s, availableSize.Height));
        }

        public override void Render(DrawingContext dc)
        {
            double size = Math.Min(Bounds.Width, Bounds.Height);
            if (size <= 0) return;
            double scale = size / 32.0;
            var parts = Shapes(Kind, Subject, Value);
            uint hue = RowHue.ToArgb(EffectiveHue);
            bool on = IsOn;

            // Same order as the WPF matrix: centre the 32-unit grid, scale, tilt, move to the middle.
            var place = Matrix.CreateTranslation(-16, -16)
                        * Matrix.CreateScale(scale, scale)
                        * Matrix.CreateRotation(EmblemPalette.StickerTiltDegrees * Math.PI / 180.0)
                        * Matrix.CreateTranslation(Bounds.Width / 2, Bounds.Height / 2);

            var passes = EmblemPalette.EdgePasses(RowHue.GetEdge(this));
            double edge = passes[0].Dip / scale;                   // grid units, the outermost pass
            double dy = EmblemPalette.StickerShadowDyDip;          // DIP, straight down

            using var off = on ? default(DrawingContext.PushedState?) : dc.PushOpacity(EmblemPalette.OffEmblemOpacity);

            // Pass 1: the soft dark offset below (a wider faint halo, then the core).
            var shadow = RowHue.Brush(EmblemPalette.StickerShadow);
            using (dc.PushTransform(Matrix.CreateTranslation(0, dy)))
            using (dc.PushTransform(place))
            {
                using (dc.PushOpacity(0.2)) Silhouette(dc, parts, shadow, edge + 0.75 / scale);
                using (dc.PushOpacity(0.35)) Silhouette(dc, parts, shadow, edge);
            }

            using (dc.PushTransform(place))
            {
                // Pass 2: the edge around the whole picture, outermost first.
                foreach (var (argb, dip) in passes)
                    Silhouette(dc, parts, RowHue.Brush(argb), dip / scale);

                // Pass 3: the inks.
                foreach (var (geo, layer) in parts)
                {
                    uint argb = on ? EmblemPalette.Ink(layer.Ink, hue, layer.FixedArgb) : EmblemPalette.OffInk(layer.Ink, hue, layer.FixedArgb);
                    var brush = RowHue.Brush(argb);
                    if (layer.IsStroke) dc.DrawGeometry(null, MakePen(brush, layer.StrokeWidth, layer.RoundCaps), geo);
                    else dc.DrawGeometry(brush, null, geo);
                }
            }
        }

        private static void Silhouette(DrawingContext dc, IReadOnlyList<(Geometry Geo, EmblemLayer Layer)> parts, IBrush brush, double edge)
        {
            foreach (var (geo, layer) in parts)
            {
                if (layer.IsStroke) dc.DrawGeometry(null, MakePen(brush, layer.StrokeWidth + 2 * edge, layer.RoundCaps), geo);
                else dc.DrawGeometry(brush, MakePen(brush, 2 * edge, false), geo);
            }
        }

        private static IPen MakePen(IBrush brush, double thickness, bool roundCaps)
            => new Pen(brush, thickness, lineCap: roundCaps ? PenLineCap.Round : PenLineCap.Flat, lineJoin: PenLineJoin.Round);

        // ---------- geometry cache: per (kind, subject, value) as EmblemArt folds them ----------

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
            // F1 = nonzero fill, the SVG default (the path mini-language defaults to even-odd).
            for (int i = 0; i < layers.Count; i++)
                built[i] = (Geometry.Parse("F1 " + layers[i].PathData), layers[i]);
            lock (CacheGate) { Cache[key] = built; }
            return built;
        }
    }
}
