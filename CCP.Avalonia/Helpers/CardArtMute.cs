using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using System.Runtime.CompilerServices;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Helpers;

// WPF Services/CardMute.cs: a cached, alpha-preserving gray twin, no per-frame Effect.
internal static class CardArtMute
{
    private static readonly ConditionalWeakTable<IImageBrushSource, Bitmap> Cache = new();

    internal static Bitmap? Gray(IImageBrushSource? source)
    {
        if (source is not Bitmap bitmap) return null;
        try { return Cache.GetValue(source, _ =>
        {
            // WPF CardMute shrinks to 320 px before converting; copy pixels, never re-encode.
            double ratio = System.Math.Min(1, 320.0 / System.Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height));
            using var scaled = ratio < 1
                ? bitmap.CreateScaledBitmap(new PixelSize(System.Math.Max(1, (int)(bitmap.PixelSize.Width * ratio)),
                    System.Math.Max(1, (int)(bitmap.PixelSize.Height * ratio))))
                : null;
            var src = scaled ?? bitmap;
            // CopyPixels(framebuffer) transcodes any source format into this Bgra8888 twin.
            var gray = new WriteableBitmap(src.PixelSize, src.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = gray.Lock())
            {
                src.CopyPixels(fb);
                var px = new byte[fb.RowBytes * fb.Size.Height];
                Marshal.Copy(fb.Address, px, 0, px.Length);
                // Channel-linear, so premultiplied stays premultiplied.
                for (int i = 0; i < px.Length; i += 4)
                    px[i] = px[i + 1] = px[i + 2] = (byte)System.Math.Min(255,
                        (int)(.114 * px[i] + .587 * px[i + 1] + .299 * px[i + 2] + 0.5));
                Marshal.Copy(px, 0, fb.Address, px.Length);
            }
            return (Bitmap)gray;
        }); }
        catch (System.Exception ex)
        {
            Serilog.Log.Debug(ex, "Card grayscale unavailable; retaining opacity dim");
            return null;
        }
    }

    internal static void Fade(Border layer, bool mute)
    {
        layer.Transitions = Env.AllowTransitions && layer.IsLoaded
            ? new Transitions { new DoubleTransition { Property = Border.OpacityProperty,
                Duration = System.TimeSpan.FromMilliseconds(200), Easing = new QuadraticEaseOut() } } : null;
        layer.Opacity = mute ? 1 : 0;
    }
}
