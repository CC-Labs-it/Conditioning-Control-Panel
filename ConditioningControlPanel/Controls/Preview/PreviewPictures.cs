using System;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview
{
    /// <summary>
    /// The fake pictures of a preview: a diagonal gradient card in one of six hue pairs with a white
    /// spiral stroke, as in the mockup. Generated in code; never the user's media, a pack, a mod picture
    /// or Scrolller. Each call makes a new image because a FlashLayer takes ownership of its frames.
    /// </summary>
    public static class PreviewPictures
    {
        public static readonly (uint A, uint B)[] Hues =
        {
            (0xff69b4, 0x8f4bff), (0x5ea8ff, 0x6a4cff), (0xffb347, 0xff5f8f),
            (0x5ce0a5, 0x2f8fff), (0xff5fd2, 0xff9a5f), (0xb58cff, 0xff69b4),
        };

        /// <summary>The aspect ratios (w / h) a fake picture can have.</summary>
        public static readonly float[] Aspects = { 1f, .74f, 1.36f };

        private const string Spiral = "M20 20c0-2 3-2 3 1s-5 4-6 0 3-8 8-6 6 9 1 12-13 1-13-7 9-13 16-9";
        private static readonly SKPath SpiralPath = SKPath.ParseSvgPathData(Spiral);

        /// <summary>A picture about <paramref name="longSidePx"/> on its long side.</summary>
        public static SKImage Make(int hue, float aspect, double angleDeg, int longSidePx = 220)
        {
            int w = aspect >= 1 ? longSidePx : Math.Max(8, (int)(longSidePx * aspect));
            int h = aspect >= 1 ? Math.Max(8, (int)(longSidePx / aspect)) : longSidePx;
            using var s = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            var c = s.Canvas;
            var (a, b) = Hues[((hue % Hues.Length) + Hues.Length) % Hues.Length];
            // CSS linear-gradient(angle): 0deg points up, 90deg right; the line spans the box's projection.
            var rad = angleDeg * Math.PI / 180;
            float dx = (float)Math.Sin(rad), dy = (float)-Math.Cos(rad);
            float half = (Math.Abs(w * dx) + Math.Abs(h * dy)) / 2;
            var mid = new SKPoint(w / 2f, h / 2f);
            using (var p = new SKPaint { IsAntialias = true })
            {
                p.Shader = SKShader.CreateLinearGradient(new SKPoint(mid.X - dx * half, mid.Y - dy * half),
                    new SKPoint(mid.X + dx * half, mid.Y + dy * half),
                    new[] { PreviewDesktop.Css(a), PreviewDesktop.Css(b) }, null, SKShaderTileMode.Clamp);
                c.DrawRect(0, 0, w, h, p);
                p.Shader = null;
                // The 40x40 spiral, "slice" fit (covers the card), white at 60%.
                float k = Math.Max(w, h) / 40f;
                c.Save();
                c.Translate((w - 40 * k) / 2, (h - 40 * k) / 2);
                c.Scale(k);
                p.Style = SKPaintStyle.Stroke;
                p.StrokeWidth = 2;
                p.StrokeCap = SKStrokeCap.Round;
                p.Color = new SKColor(255, 255, 255, 153);
                c.DrawPath(SpiralPath, p);
                c.Restore();
            }
            return s.Snapshot();
        }
    }
}
