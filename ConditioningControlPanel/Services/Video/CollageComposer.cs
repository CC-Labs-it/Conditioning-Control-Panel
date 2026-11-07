using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Renders a "cinematic collage" desktop wallpaper from the user's own images: one large
    /// hero bleeding off an edge, and the opposite half packed with smaller, tilted, feathered
    /// tiles that overlap so almost no background shows. A blurred atmosphere layer sits behind
    /// everything, and one colour grade + vignette (keyed to the active mod's accent) ties the
    /// unrelated images into a single composition. No text.
    ///
    /// This is a straight port of the approved Python/PIL preview, drawn with SkiaSharp (already
    /// shipped with the app via SkiaSharp.Views.WPF). Every call produces a completely fresh
    /// composition - new random draw, new hero, new bleed side, re-rolled tilts - and writes it
    /// to a JPEG on disk, returning the path. Windows can only be handed a file, so the caller is
    /// responsible for setting it as the wallpaper and for the temp-file lifecycle.
    /// </summary>
    public static partial class CollageComposer
    {
        // Helpers marked internal are shared with DynamicWallpaperService, so the moving collage is drawn
        // with exactly the same look code. The layout plan and Compose are in CollageComposer.Plan.cs.

        // Formats SkiaSharp can decode as a SOURCE. Wider than what Windows can SET as a wallpaper
        // (webp/gif are fine here because the OUTPUT is always a jpeg).
        internal static readonly string[] SourceExtensions =
            { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".apng" };

        private const int MaxTiltDeg = 20;       // collage tilt: random +/- this, a third kept upright
        private const int DecodeBudget = 40;     // images decoded per compose (hero + atmosphere + tiles)
        private const int TileDecodeCap = 768;   // longest side kept per tile: none is drawn much larger than this

        internal static readonly SKColor DefaultAccent = new(0xE8, 0x43, 0x93);       // default-mod pink
        internal static readonly SKColor DefaultBackground = new(0x08, 0x08, 0x0C);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;

        /// <summary>The collage canvas: the primary screen's size (never smaller than 640×480).</summary>
        internal static (int W, int H) CanvasSize()
            => (Math.Max(640, GetSystemMetrics(SM_CXSCREEN)), Math.Max(480, GetSystemMetrics(SM_CYSCREEN)));

        /// <summary>
        /// Draw <paramref name="src"/> cover-cropped into a w×h tile, feather each edge by its own amount
        /// (pixels; 0 = hard), then rotate about (cx,cy) and stamp it. Feathering multiplies a horizontal
        /// edge ramp by a vertical one (two DstIn passes) - an exact per-edge fade, cheaper than a blur.
        /// </summary>
        private static void DrawTileCore(SKCanvas canvas, SKBitmap src, float cx, float cy, int w, int h,
            float fL, float fT, float fR, float fB, float angleDeg, SKImageFilter? sharpen = null)
        {
            if (w <= 0 || h <= 0) return;
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) return;
            var tc = surface.Canvas;
            tc.Clear(SKColors.Transparent);

            var srcRect = CoverSrcRect(src.Width, src.Height, w, h);
            using (var ip = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
            {
                if (sharpen != null) ip.ImageFilter = sharpen;
                tc.DrawBitmap(src, srcRect, new SKRect(0, 0, w, h), ip);
            }

            ApplyEdgeFeather(tc, w, h, fL, fT, fR, fB);

            using var img = surface.Snapshot();
            canvas.Save();
            canvas.Translate(cx, cy);
            if (angleDeg != 0f) canvas.RotateDegrees(angleDeg);
            using (var dp = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
                canvas.DrawImage(img, -w / 2f, -h / 2f, dp);
            canvas.Restore();
        }

        /// <summary>Fade the tile's alpha in from each edge by its own width (px) - a horizontal L/R ramp
        /// then a vertical T/B ramp, each DstIn so the two multiply. An edge of 0 stays hard.</summary>
        internal static void ApplyEdgeFeather(SKCanvas tc, int w, int h, float fL, float fT, float fR, float fB)
        {
            if (w > 0 && (fL > 0.5f || fR > 0.5f))
            {
                float l = Math.Clamp(fL, 0, w), r = Math.Clamp(w - fR, 0, w);
                if (r <= l) { l = w / 2f; r = w / 2f; }
                var colors = new[] { SKColors.White.WithAlpha(0), SKColors.White, SKColors.White, SKColors.White.WithAlpha(0) };
                var pos = new[] { 0f, l / w, r / w, 1f };
                using var sh = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, 0), colors, pos, SKShaderTileMode.Clamp);
                using var p = new SKPaint { BlendMode = SKBlendMode.DstIn, Shader = sh, IsAntialias = true };
                tc.DrawRect(0, 0, w, h, p);
            }
            if (h > 0 && (fT > 0.5f || fB > 0.5f))
            {
                float t = Math.Clamp(fT, 0, h), b = Math.Clamp(h - fB, 0, h);
                if (b <= t) { t = h / 2f; b = h / 2f; }
                var colors = new[] { SKColors.White.WithAlpha(0), SKColors.White, SKColors.White, SKColors.White.WithAlpha(0) };
                var pos = new[] { 0f, t / h, b / h, 1f };
                using var sh = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), colors, pos, SKShaderTileMode.Clamp);
                using var p = new SKPaint { BlendMode = SKBlendMode.DstIn, Shader = sh, IsAntialias = true };
                tc.DrawRect(0, 0, w, h, p);
            }
        }

        private static readonly SKRect DefaultFocus = new SKRect(0.2f, 0.15f, 0.8f, 0.85f);   // safe centred box

        /// <summary>Estimate where a picture's detail concentrates - a focus box in 0..1 of the source,
        /// from gradient energy on a 40px thumbnail. Cheap (≈1600 px, once per image); no recognition
        /// model. Any failure returns <see cref="DefaultFocus"/>.</summary>
        internal static SKRect ComputeFocus(SKBitmap bmp)
        {
            try
            {
                const int S = 40;
                using var small = new SKBitmap(new SKImageInfo(S, S, SKColorType.Bgra8888, SKAlphaType.Premul));
                if (!bmp.ScalePixels(small.PeekPixels(), SKFilterQuality.Low)) return DefaultFocus;
                var px = small.Pixels;
                if (px == null || px.Length < S * S) return DefaultFocus;

                var lum = new float[S * S];
                for (int i = 0; i < S * S; i++) { var c = px[i]; lum[i] = 0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue; }

                var e = new float[S * S];
                float sum = 0, mx = 0, my = 0;
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    int i = y * S + x;
                    float gx = Math.Abs(lum[i] - lum[i + (x < S - 1 ? 1 : 0)]);
                    float gy = Math.Abs(lum[i] - lum[i + (y < S - 1 ? S : 0)]);
                    float en = gx + gy; e[i] = en; sum += en; mx += en * x; my += en * y;
                }
                if (sum < 1e-3f) return DefaultFocus;

                float cx = mx / sum, cy = my / sum, vx = 0, vy = 0;
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                {
                    float en = e[y * S + x]; vx += en * (x - cx) * (x - cx); vy += en * (y - cy) * (y - cy);
                }
                float sx = (float)Math.Sqrt(vx / sum), sy = (float)Math.Sqrt(vy / sum), k = 1.15f;
                float x0 = Clamp01((cx - k * sx) / S), x1 = Clamp01((cx + k * sx) / S);
                float y0 = Clamp01((cy - k * sy) / S), y1 = Clamp01((cy + k * sy) / S);
                return new SKRect(x0, y0, Math.Max(x0 + 0.08f, x1), Math.Max(y0 + 0.08f, y1));
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "collage focus estimate");   // focus is an optimisation: default box
                return DefaultFocus;
            }
        }

        /// <summary>Per-edge feather amounts (px) from a focus box: an edge the focus hugs feathers less
        /// (~6% of the short side), an edge with empty space feathers fully (~20%). The
        /// focus is mapped through the tile's centred cover-crop first so it lines up with what's shown.</summary>
        internal static void PerEdgeFeather(SKRect focus, int sw, int sh, int tw, int th,
            out float fL, out float fT, out float fR, out float fB)
        {
            float scale = Math.Max((float)tw / sw, (float)th / sh);
            float cropW = tw / scale, cropH = th / scale;
            float left = (sw - cropW) / 2f, top = (sh - cropH) / 2f;
            float fx0 = Clamp01((focus.Left * sw - left) / cropW);
            float fx1 = Clamp01((focus.Right * sw - left) / cropW);
            float fy0 = Clamp01((focus.Top * sh - top) / cropH);
            float fy1 = Clamp01((focus.Bottom * sh - top) / cropH);
            float baseF = Math.Min(tw, th);
            fL = EdgeFeather(fx0) * baseF;
            fR = EdgeFeather(1f - fx1) * baseF;
            fT = EdgeFeather(fy0) * baseF;
            fB = EdgeFeather(1f - fy1) * baseF;
        }
        private static float EdgeFeather(float gap) => 0.06f + 0.14f * Clamp01(gap / 0.18f);
        private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        private static void DrawAtmosphere(SKCanvas canvas, SKBitmap src, int w, int h)
        {
            using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) return;
            var srcRect = CoverSrcRect(src.Width, src.Height, w, h);
            using (var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
                surface.Canvas.DrawBitmap(src, srcRect, new SKRect(0, 0, w, h), p);
            using var img = surface.Snapshot();

            float sigma = Math.Max(20f, w / 45f);
            using var blur = SKImageFilter.CreateBlur(sigma, sigma);
            // Dim to ~0.52 brightness and desaturate to ~0.65, matching the preview.
            using var dim = SKColorFilter.CreateColorMatrix(BrightnessMatrix(0.52f));
            using var desat = SKColorFilter.CreateColorMatrix(SaturationMatrix(0.65f));
            using var grade = SKColorFilter.CreateCompose(dim, desat);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                ImageFilter = blur,
                ColorFilter = grade,
                Color = SKColors.White.WithAlpha(224),   // ~0.88 over the gradient base
            };
            canvas.DrawImage(img, 0, 0, paint);
        }

        // ---------------------------------------------------------------- fills

        /// <summary>Under-layer shared by Static and Dynamic: lifted gradient base + blurred atmosphere.</summary>
        internal static void DrawBackdrop(SKCanvas canvas, int w, int h, SKColor background, SKBitmap? atmosphere)
        {
            DrawVerticalGradient(canvas, w, h, Lift(background, 0.10f), background);
            if (atmosphere != null) DrawAtmosphere(canvas, atmosphere, w, h);
        }

        /// <summary>Over-layer shared by Static and Dynamic: the mod-accent grade, then the vignette.</summary>
        internal static void DrawFinish(SKCanvas canvas, int w, int h, SKColor accent)
        {
            DrawGrade(canvas, w, h, accent, 0.10f);
            DrawVignette(canvas, w, h, 0.55f);
        }

        /// <summary>The hero's light sharpen (it is blown up the most).</summary>
        internal static SKImageFilter HeroSharpen() => SKImageFilter.CreateMatrixConvolution(
            new SKSizeI(3, 3),
            new float[] { 0, -0.4f, 0, -0.4f, 2.6f, -0.4f, 0, -0.4f, 0 },
            1f, 0f, new SKPointI(1, 1), SKShaderTileMode.Clamp, false);

        private static void DrawVerticalGradient(SKCanvas canvas, int w, int h, SKColor top, SKColor bottom)
        {
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(0, h),
                new[] { top, bottom }, null, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, w, h, paint);
        }

        private static void DrawGrade(SKCanvas canvas, int w, int h, SKColor tint, float amount)
        {
            using var paint = new SKPaint { Color = tint.WithAlpha((byte)(amount * 255)) };
            canvas.DrawRect(0, 0, w, h, paint);
        }

        private static void DrawVignette(SKCanvas canvas, int w, int h, float strength)
        {
            var center = new SKPoint(w / 2f, h / 2f);
            float radius = (float)Math.Sqrt(w * (double)w + h * (double)h) / 2f;
            var colors = new[]
            {
                SKColors.Black.WithAlpha(0),
                SKColors.Black.WithAlpha(0),
                SKColors.Black.WithAlpha((byte)(strength * 255)),
            };
            var stops = new[] { 0f, 0.45f, 1f };
            using var shader = SKShader.CreateRadialGradient(center, radius, colors, stops, SKShaderTileMode.Clamp);
            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, w, h, paint);
        }

        // ---------------------------------------------------------------- decode helpers

        /// <summary>The picture at no more than <paramref name="maxSide"/> on its longest side, with the
        /// source's real pixel count: keeps a compose's working set small, and the hero is still the sharpest.</summary>
        internal static SKBitmap? DecodeCapped(string path, int maxSide, out long sourcePixels)
        {
            sourcePixels = 0;
            var full = DecodeRepresentativeFrame(path);
            if (full == null) return null;
            sourcePixels = full.Width * (long)full.Height;
            int side = Math.Max(full.Width, full.Height);
            if (side <= maxSide) return full;

            float k = maxSide / (float)side;
            var info = new SKImageInfo(Math.Max(1, (int)(full.Width * k)), Math.Max(1, (int)(full.Height * k)),
                SKColorType.Bgra8888, SKAlphaType.Premul);
            var small = new SKBitmap(info);
            bool ok = full.ScalePixels(small.PeekPixels(), SKFilterQuality.Medium);
            if (ok) { full.Dispose(); return small; }
            small.Dispose();
            return full;
        }

        internal static SKBitmap? DecodeRepresentativeFrame(string path)
        {
            try
            {
                using var codec = SKCodec.Create(path);
                if (codec == null) return SafeDecode(path);
                if (codec.Info.Width <= 0 || codec.Info.Height <= 0) return SafeDecode(path);

                var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
                int frames = 0;
                try { frames = codec.FrameCount; } catch { frames = 0; }

                if (frames > 1)
                {
                    // Decode sequentially up to the middle frame, reusing the buffer, so gif frames
                    // that only store a delta over the previous frame compose correctly.
                    int target = Math.Clamp(frames / 2, 0, frames - 1);
                    var bmp = new SKBitmap(info);
                    bool ok = false;
                    for (int i = 0; i <= target; i++)
                    {
                        var opts = new SKCodecOptions(i, i == 0 ? -1 : i - 1);
                        var r = codec.GetPixels(info, bmp.GetPixels(), opts);
                        ok = r == SKCodecResult.Success || r == SKCodecResult.IncompleteInput;
                        if (!ok) break;
                    }
                    if (ok) return bmp;
                    bmp.Dispose();
                    return SafeDecode(path);
                }

                var still = new SKBitmap(info);
                var res = codec.GetPixels(info, still.GetPixels());
                if (res == SKCodecResult.Success || res == SKCodecResult.IncompleteInput) return still;
                still.Dispose();
                return SafeDecode(path);
            }
            catch
            {
                return SafeDecode(path);
            }
        }

        private static SKBitmap? SafeDecode(string path)
        {
            try { return SKBitmap.Decode(path); } catch { return null; }
        }

        // ---------------------------------------------------------------- math helpers

        internal static SKRect CoverSrcRect(int sw, int sh, int tw, int th)
        {
            float scale = Math.Max((float)tw / sw, (float)th / sh);
            float cropW = tw / scale, cropH = th / scale;
            float left = (sw - cropW) / 2f, top = (sh - cropH) / 2f;
            return new SKRect(left, top, left + cropW, top + cropH);
        }

        /// <summary>A tile's aspect, kept between portrait-ish and landscape-ish so no tile is a sliver.</summary>
        internal static float ClampAspect(int width, int height)
        {
            float a = height <= 0 ? 1f : (float)width / height;
            return Math.Clamp(a, 0.62f, 1.62f);
        }

        private static float Range(Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        internal static SKColor HexToColor(string? hex, SKColor fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var h = hex.Trim().TrimStart('#');
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            if (h.Length >= 6 &&
                uint.TryParse(h.Substring(0, 6), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                return new SKColor((byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
            }
            return fallback;
        }

        private static SKColor Lift(SKColor c, float amt) => new SKColor(
            (byte)(c.Red + (255 - c.Red) * amt),
            (byte)(c.Green + (255 - c.Green) * amt),
            (byte)(c.Blue + (255 - c.Blue) * amt));

        private static float[] BrightnessMatrix(float b) => new float[]
        {
            b, 0, 0, 0, 0,
            0, b, 0, 0, 0,
            0, 0, b, 0, 0,
            0, 0, 0, 1, 0,
        };

        private static float[] SaturationMatrix(float s)
        {
            const float lr = 0.213f, lg = 0.715f, lb = 0.072f;
            return new float[]
            {
                lr * (1 - s) + s, lg * (1 - s),     lb * (1 - s),     0, 0,
                lr * (1 - s),     lg * (1 - s) + s, lb * (1 - s),     0, 0,
                lr * (1 - s),     lg * (1 - s),     lb * (1 - s) + s, 0, 0,
                0,                0,                0,                1, 0,
            };
        }
    }
}
