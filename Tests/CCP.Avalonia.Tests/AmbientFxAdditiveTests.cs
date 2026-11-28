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

    [Fact]
    public void BankTokenCoreAndHaloAddOnTransparentSurface()
    {
        var sprites = new System.Collections.Generic.List<AmbientFxCanvas.FogSprite>();
        AmbientFxCanvas.AddTokenSprites(sprites, 32, 32, 24, 0.3f, 0.5f);
        Assert.Equal(2, sprites.Count);
        Assert.True(sprites[0].Size > sprites[1].Size);
        using var surface = SKSurface.Create(new SKImageInfo(64, 64));
        surface.Canvas.Clear(SKColors.Transparent);
        AmbientFxCanvas.PaintFog(surface.Canvas, sprites.ToArray(), Colors.White, new SKRect(0, 0, 64, 64));
        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        var combined = bitmap.GetPixel(32, 32).Alpha;
        using var single = SKSurface.Create(new SKImageInfo(64, 64));
        single.Canvas.Clear(SKColors.Transparent);
        AmbientFxCanvas.PaintFog(single.Canvas, new[] { sprites[1] }, Colors.White, new SKRect(0, 0, 64, 64));
        using var singleImage = single.Snapshot();
        using var singleBitmap = SKBitmap.FromImage(singleImage);
        var coreAlpha = singleBitmap.GetPixel(32, 32).Alpha;
        using var halo = SKSurface.Create(new SKImageInfo(64, 64));
        halo.Canvas.Clear(SKColors.Transparent);
        AmbientFxCanvas.PaintFog(halo.Canvas, new[] { sprites[0] }, Colors.White, new SKRect(0, 0, 64, 64));
        using var haloImage = halo.Snapshot();
        using var haloBitmap = SKBitmap.FromImage(haloImage);
        var haloAlpha = haloBitmap.GetPixel(32, 32).Alpha;
        Assert.InRange(combined, coreAlpha + haloAlpha - 1, coreAlpha + haloAlpha + 1);
    }

}
