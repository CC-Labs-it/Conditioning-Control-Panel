using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
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
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            using var original = SKBitmap.Decode(stream.ToArray());
            double ratio = System.Math.Min(1, 320.0 / System.Math.Max(original.Width, original.Height));
            using var surface = SKSurface.Create(new SKImageInfo(System.Math.Max(1, (int)(original.Width * ratio)),
                System.Math.Max(1, (int)(original.Height * ratio))));
            using var filter = SKColorFilter.CreateColorMatrix(new float[]
            {
                .299f, .587f, .114f, 0, 0, .299f, .587f, .114f, 0, 0,
                .299f, .587f, .114f, 0, 0, 0, 0, 0, 1, 0,
            });
            using var paint = new SKPaint { ColorFilter = filter };
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawBitmap(original, new SKRect(0, 0, surface.Canvas.DeviceClipBounds.Width,
                surface.Canvas.DeviceClipBounds.Height), paint);
            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var result = encoded.AsStream();
            return new Bitmap(result);
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
