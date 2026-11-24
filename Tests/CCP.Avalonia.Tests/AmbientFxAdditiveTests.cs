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
            AmbientFxCanvas.PaintFog(surface.Canvas, sprites, Colors.White, new SKRect(0, 0, 64, 64));
            using var image = surface.Snapshot();
            using var bitmap = SKBitmap.FromImage(image);
            return bitmap.GetPixel(32, 32);
        }
        var one = Pixel(puff);
        var two = Pixel(puff, puff);
        Assert.InRange(two.Alpha, one.Alpha * 2 - 1, one.Alpha * 2 + 1);
        Assert.True(two.Alpha > one.Alpha + one.Alpha * (255 - one.Alpha) / 255 + 4);
    }
    [Fact]
    public void FogCompositesOverOpaqueBackgroundRatherThanAddingToIt()
    {
        var puff = new AmbientFxCanvas.FogSprite(32, 32, 48, 0.25f);
        var bounds = new SKRect(0, 0, 64, 64);
        var background = new SKColor(30, 80, 140);
        using var layer = SKSurface.Create(new SKImageInfo(64, 64));
        layer.Canvas.Clear(SKColors.Transparent);
        AmbientFxCanvas.PaintFog(layer.Canvas, new[] { puff, puff }, Colors.White, bounds);
        using var fog = layer.Snapshot();
        using var expected = SKSurface.Create(new SKImageInfo(64, 64));
        expected.Canvas.Clear(background);
        expected.Canvas.DrawImage(fog, 0, 0);
        using var actual = SKSurface.Create(new SKImageInfo(64, 64));
        actual.Canvas.Clear(background);
        AmbientFxCanvas.PaintFog(actual.Canvas, new[] { puff, puff }, Colors.White, bounds);
        using var expectedImage = expected.Snapshot();
        using var actualImage = actual.Snapshot();
        using var expectedBitmap = SKBitmap.FromImage(expectedImage);
        using var actualBitmap = SKBitmap.FromImage(actualImage);
        Assert.Equal(expectedBitmap.GetPixel(32, 32), actualBitmap.GetPixel(32, 32));
        Assert.NotEqual(background, actualBitmap.GetPixel(32, 32));
    }

}
