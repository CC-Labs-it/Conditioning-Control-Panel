using System;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview
{
    /// <summary>
    /// The static slice of a plain desktop under a live preview (owner note 2026-11-24: "just a SLICE of a
    /// real screen mockup, windows like, basic desktop bg, folders"). Drawn once per size into a cached
    /// bitmap by <see cref="EffectPreview"/>. Generic shapes only: no real Windows artwork, no logos and no
    /// words (icon labels are pale bars, like the text lines in the windows; the clock is digits).
    /// Units are screen DIPs. The help loops' desktop (<c>LoopFrame.Desktop</c>) is a WPF painter for a
    /// fixed 480x270 stage with one window; this one fills a tall slice of any height, so it is its own.
    /// </summary>
    public static class PreviewDesktop
    {
        /// <summary>Taskbar strip at the bottom of the slice.</summary>
        public const float TaskbarHeight = 34;
        /// <summary>Bezel geometry of the frame around the screen (host DIPs).</summary>
        public const float Bezel = 11, OuterRadius = 12, ScreenRadius = 10;

        /// <summary>The height the mockup was laid out for: windows sit at the same share of a taller slice.</summary>
        private const float DesignHeight = 1100;

        public static void Paint(SKCanvas c, float w, float h)
        {
            using var p = new SKPaint { IsAntialias = true };
            Wallpaper(c, p, w, h);
            float k = Math.Max(1f, h / DesignHeight);
            Window(c, p, w * .34f, 70 * k, w * .92f, 230, false, new[] { .46f, .60f, .38f, .54f, .30f }, .08f);
            Window(c, p, -w * .34f, 440 * k, w * .92f, 210, true, new[] { .44f, .30f, .48f }, .44f);
            Window(c, p, w * .42f, 760 * k, w * .92f, 250, false, new[] { .40f, .52f, .34f }, .08f);
            Icons(c, p);
            Taskbar(c, p, w, h);
        }

        private static void Wallpaper(SKCanvas c, SKPaint p, float w, float h)
        {
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(w * .46f, 0), new SKPoint(w * .54f, h),
                new[] { Css(0x0d1a40), Css(0x161040), Css(0x0e1f4a) }, new[] { 0f, .6f, 1f }, SKShaderTileMode.Clamp);
            c.DrawRect(0, 0, w, h, p);
            Blob(c, p, w * .85f, h * .30f, w * .90f, h * .34f, Css(0x3f7bff), .62f);
            Blob(c, p, w * .30f, h * .58f, w * 1.10f, h * .30f, Css(0x7c5cf6), .64f);
            Blob(c, p, w * .90f, h * .88f, w * .90f, h * .26f, Css(0x22c7ee), .60f);
            p.Shader = null;
        }

        /// <summary>A CSS-like elliptical radial gradient: colour at the centre, transparent at <paramref name="stop"/>.</summary>
        private static void Blob(SKCanvas c, SKPaint p, float cx, float cy, float rx, float ry, SKColor colour, float stop)
        {
            var m = SKMatrix.CreateScale(1, ry / rx, cx, cy);
            p.Shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), rx,
                new[] { colour, colour.WithAlpha(0) }, new[] { 0f, stop }, SKShaderTileMode.Clamp, m);
            c.DrawRect(cx - rx, cy - ry, rx * 2, ry * 2, p);
        }

        private static void Window(SKCanvas c, SKPaint p, float x, float y, float w, float h, bool dark, float[] lines, float lineLeft)
        {
            var r = new SKRect(x, y, x + w, y + h);
            using (var shadow = SKImageFilter.CreateDropShadowOnly(0, 4, 7, 7, new SKColor(0, 0, 0, 102)))
            {
                p.ImageFilter = shadow;
                p.Color = SKColors.Black;
                c.DrawRoundRect(r, 7, 7, p);
                p.ImageFilter = null;
            }
            c.Save();
            c.ClipRoundRect(new SKRoundRect(r, 7, 7), antialias: true);
            p.Color = dark ? Css(0x23263a) : Css(0xf3f4f8);
            c.DrawRect(r, p);
            p.Color = dark ? Css(0x2f3350) : Css(0xdfe2ec);
            c.DrawRect(x, y, w, 26, p);
            p.Color = dark ? Css(0x474c6e) : Css(0xc3c8d8);
            for (int i = 0; i < lines.Length; i++)
            {
                var top = y + 52 + i * 24;
                if (top + 9 > y + h - 8) break;
                c.DrawRoundRect(new SKRect(x + w * lineLeft, top, x + w * (lineLeft + lines[i]), top + 9), 3, 3, p);
            }
            c.Restore();
        }

        private static void Icons(SKCanvas c, SKPaint p)
        {
            // left 8, 66 wide, centred column; tops as the mockup (14, 88, 162, 240, 314).
            const float cx = 8 + 33;
            Folder(c, p, cx, 14 + 6);
            Label(c, p, cx, 14 + 6 + 23 + 7, 44);
            Folder(c, p, cx, 88 + 6);
            Label(c, p, cx, 88 + 6 + 23 + 7, 36);
            File(c, p, cx, 162);
            Label(c, p, cx, 162 + 30 + 7, 46);
            Shot(c, p, cx, 240);
            Label(c, p, cx, 240 + 26 + 7, 48);
            Bin(c, p, cx, 314 + 5);
            Label(c, p, cx, 314 + 5 + 26 + 7, 54);
        }

        private static void Folder(SKCanvas c, SKPaint p, float cx, float top)
        {
            p.Color = Css(0xeab23a);
            c.DrawRoundRect(new SKRect(cx - 17, top - 5, cx - 17 + 15, top + 3), 3, 3, p);
            p.Color = Css(0xffcf4d);
            c.DrawRoundRect(new SKRect(cx - 17, top, cx + 17, top + 23), 3, 3, p);
        }

        private static void File(SKCanvas c, SKPaint p, float cx, float top)
        {
            using var path = new SKPath();
            float l = cx - 12, r = cx + 12, b = top + 30;
            path.MoveTo(l, top); path.LineTo(l + 24 * .66f, top); path.LineTo(r, top + 30 * .26f);
            path.LineTo(r, b); path.LineTo(l, b); path.Close();
            p.Color = Css(0xeef1f8);
            c.DrawPath(path, p);
            p.Color = Css(0xc3c8d8);
            for (int i = 0; i < 3; i++) c.DrawRect(l + 4, top + 13 + i * 5, 14 - i * 3, 2, p);
        }

        private static void Shot(SKCanvas c, SKPaint p, float cx, float top)
        {
            var r = new SKRect(cx - 16, top, cx + 16, top + 26);
            p.Color = SKColors.White;
            c.DrawRoundRect(new SKRect(r.Left - 2, r.Top - 2, r.Right + 2, r.Bottom + 2), 4, 4, p);
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left, r.Top), new SKPoint(r.Right, r.Bottom),
                new[] { Css(0x5ce0a5), Css(0x3f7bff) }, null, SKShaderTileMode.Clamp);
            c.DrawRoundRect(r, 3, 3, p);
            p.Shader = null;
        }

        private static void Bin(SKCanvas c, SKPaint p, float cx, float top)
        {
            p.Color = Css(0xdfe4f0);
            c.DrawRoundRect(new SKRect(cx - 14.5f, top - 5, cx + 14.5f, top - 1), 2, 2, p);
            p.Color = Css(0xb9c2d6);
            using var rr = new SKRoundRect();
            rr.SetRectRadii(new SKRect(cx - 11.5f, top, cx + 11.5f, top + 26),
                new[] { new SKPoint(2, 2), new SKPoint(2, 2), new SKPoint(6, 6), new SKPoint(6, 6) });
            c.DrawRoundRect(rr, p);
        }

        /// <summary>The name under an icon: a pale bar with a soft shadow, the slice carries no words.</summary>
        private static void Label(SKCanvas c, SKPaint p, float cx, float top, float width)
        {
            p.Color = new SKColor(0, 0, 0, 110);
            c.DrawRoundRect(new SKRect(cx - width / 2, top + 1, cx + width / 2, top + 6), 2.5f, 2.5f, p);
            p.Color = new SKColor(255, 255, 255, 225);
            c.DrawRoundRect(new SKRect(cx - width / 2, top, cx + width / 2, top + 5), 2.5f, 2.5f, p);
        }

        private static void Taskbar(SKCanvas c, SKPaint p, float w, float h)
        {
            float top = h - TaskbarHeight, mid = top + TaskbarHeight / 2;
            p.Color = new SKColor(16, 18, 36, 230);
            c.DrawRect(0, top, w, TaskbarHeight, p);
            // Start mark: four small squares in two blues (generic, not a logo).
            float sx = 10, s = 17 * .46f, gap = 17 - 2 * s;
            var blues = new[] { Css(0x5ea8ff), Css(0x7cc0ff), Css(0x4a93f0), Css(0x6bb3ff) };
            for (int i = 0; i < 4; i++)
            {
                p.Color = blues[i];
                float x = sx + (i % 2) * (s + gap), y = mid - 8.5f + (i / 2) * (s + gap);
                c.DrawRoundRect(new SKRect(x, y, x + s, y + s), 1.5f, 1.5f, p);
            }
            float at = sx + 17 + 9;
            p.Color = Css(0x2c3054);
            c.DrawRoundRect(new SKRect(at, mid - 10, at + 78, mid + 10), 10, 10, p);
            at += 78 + 9;
            foreach (var col in new[] { 0xffd35c, 0x5ce0a5, 0xff69b4, 0xb58cff })
            {
                if (at + 18 > w - 52) break;
                p.Color = Css((uint)col);
                c.DrawRoundRect(new SKRect(at, mid - 9, at + 18, mid + 9), 5, 5, p);
                at += 18 + 9;
            }
            p.Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
            p.TextSize = 11;
            p.TextAlign = SKTextAlign.Right;
            p.Color = Css(0xdfe2ec);
            c.DrawText("21:04", w - 10, mid + 4, p);
        }

        /// <summary>The CSS hex colour as an opaque SKColor.</summary>
        public static SKColor Css(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
