using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// A text glow baked into a bitmap once (lag-fixes-b, P82 rule 3). An <see cref="Visual.Effect"/> is
    /// re-run on every frame anything under or over it moves - a pulse, a breath, a drifting mote - so a
    /// glow that sits under a loop is rendered here once per text change, through the very same Effect,
    /// and shown as an Image whose Opacity and transform the loop may move for free. The shape is the
    /// Effect's own (glyph-shaped), not a rounded-rect BoxShadow.
    /// </summary>
    internal static class GlowBake
    {
        /// <summary>A copy of <paramref name="source"/>'s text rendered through <paramref name="effect"/>,
        /// padded by <paramref name="pad"/> DIPs on every side for the spread. Show it as an Image with
        /// <c>Stretch=None</c> and <c>Margin=-pad</c> beside the source so the glyphs line up. Null when
        /// there is no text.</summary>
        public static Bitmap? Text(TextBlock source, IBrush foreground, IEffect? effect, double pad)
        {
            if (string.IsNullOrEmpty(source.Text)) return null;
            var copy = new TextBlock
            {
                Text = source.Text,
                FontSize = source.FontSize,
                FontFamily = source.FontFamily,
                FontWeight = source.FontWeight,
                FontStyle = source.FontStyle,
                FontFeatures = source.FontFeatures,
                Foreground = foreground,
                Effect = effect,
            };
            var host = new Border { Padding = new Thickness(pad), Child = copy };
            host.Measure(Size.Infinity);
            host.Arrange(new Rect(host.DesiredSize));
            var px = new PixelSize(Math.Max(1, (int)Math.Ceiling(host.DesiredSize.Width)),
                                   Math.Max(1, (int)Math.Ceiling(host.DesiredSize.Height)));
            var bitmap = new RenderTargetBitmap(px);
            bitmap.Render(host);
            return bitmap;
        }

        /// <summary>Puts a fresh bake on <paramref name="image"/> and frees the previous one.</summary>
        public static void Swap(Image image, Bitmap? bitmap, double pad)
        {
            var old = image.Source as IDisposable;
            image.Source = bitmap;
            image.Margin = new Thickness(-pad);
            old?.Dispose();
        }
    }
}
