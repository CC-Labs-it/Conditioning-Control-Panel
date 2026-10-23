using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF FlashLayer.DrawGlitch's two modulated copies, behind the seven displaced slices.
/// The picture is immutable; each recorded frame owns its native references across render threads.</summary>
internal sealed class FlashGlitchGhosts : Control, IDisposable
{
    private readonly SKImage _picture;
    internal double Amount, Alpha;

    // --render-all sample, with the same immutable image as a live flash.
    internal FlashGlitchGhosts()
    {
        Name = "FlashGlitchGhosts";
        IsHitTestVisible = false;
        using var stream = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png"));
        using var data = SKData.Create(stream);
        _picture = SKImage.FromEncodedData(data);
        Amount = Alpha = 1;
    }

    internal FlashGlitchGhosts(Bitmap picture)
    {
        Name = "FlashGlitchGhosts";
        IsHitTestVisible = false;
        using var encoded = new MemoryStream();
        picture.Save(encoded);
        _picture = SKImage.FromEncodedData(encoded.ToArray());
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var fit = Stretch.Uniform.CalculateSize(Bounds.Size, new Size(_picture.Width, _picture.Height));
        var x = (float)((Bounds.Width - fit.Width) / 2);
        var y = (float)((Bounds.Height - fit.Height) / 2);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
        Draw(canvas, _picture, new SKRect(x, y, x + (float)fit.Width, y + (float)fit.Height), Amount, Alpha);
        context.Custom(new PictureOp(recorder.EndRecording(), new Rect(Bounds.Size)));
    }

    internal static void Draw(SKCanvas canvas, SKImage image, SKRect fit, double amount, double alpha)
    {
        var split = (float)(amount * 0.04 * fit.Width);
        if (split <= 0.5f) return;
        var a = (byte)Math.Clamp(alpha * 255 / 2, 0, 255);
        using var paint = new SKPaint();
        using var red = SKColorFilter.CreateBlendMode(new SKColor(255, 40, 90, a), SKBlendMode.Modulate);
        using var cyan = SKColorFilter.CreateBlendMode(new SKColor(40, 230, 255, a), SKBlendMode.Modulate);
        paint.ColorFilter = red;
        canvas.DrawImage(image, new SKRect(fit.Left - split, fit.Top, fit.Right - split, fit.Bottom), paint);
        paint.ColorFilter = cyan;
        canvas.DrawImage(image, new SKRect(fit.Left + split, fit.Top, fit.Right + split, fit.Bottom), paint);
    }

    public void Dispose() => _picture.Dispose();

    private sealed class PictureOp(SKPicture picture, Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() => picture.Dispose();
        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            lease.SkCanvas.DrawPicture(picture);
        }
    }
}
