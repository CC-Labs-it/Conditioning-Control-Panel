using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AmbientFxAdditiveTests
{
    [Fact]
    public void OverlappingFogPuffsAddTheirPixels()
    {
        var puff = new AmbientFxCanvas.FogSprite(32, 32, 48, 0.25f);
        SKColor Pixel(params AmbientFxCanvas.FogSprite[] sprites)
        {
            using var surface = SKSurface.Create(new SKImageInfo(64, 64));
            surface.Canvas.Clear(SKColors.Transparent);
            AmbientFxCanvas.PaintFog(surface.Canvas, sprites, Colors.White);
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            return bitmap.GetPixel(32, 32);
        }
        var one = Pixel(puff);
        var two = Pixel(puff, puff);
        Assert.InRange(two.Alpha, one.Alpha * 2 - 1, one.Alpha * 2 + 1);
        Assert.True(two.Alpha > one.Alpha + one.Alpha * (255 - one.Alpha) / 255 + 4);
    }
}
