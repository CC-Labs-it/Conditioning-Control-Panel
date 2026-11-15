using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosRabbitSpriteTests
{
    private static void WriteSprite(string path, SKColor color)
    {
        using var bitmap = new SKBitmap(16, 16);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static SKColor Sample(BubbleOverlayWindow window)
    {
        using var rendered = new RenderTargetBitmap(new PixelSize(240, 240));
        rendered.Render((Control)window.Content!);
        using var png = new MemoryStream();
        rendered.Save(png, PngBitmapEncoderOptions.Default);
        using var image = SKBitmap.Decode(png.ToArray());
        return image.GetPixel(120, 120);
    }

    // WPF BubbleService.cs:3423-3434: variant sprites replace the tint; sweepers fall back to darter.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Start_loads_and_renders_rabbit_sprites_with_fallbacks(bool darter, bool sweeper) => AvaloniaTestDispatcher.Run(() =>
    {
        var root = Directory.CreateTempSubdirectory("ccp-rabbit-sprites-");
        var bubbles = Directory.CreateDirectory(Path.Combine(root.FullName, "bubbles"));
        string generic = Path.Combine(root.FullName, "generic.png");
        WriteSprite(generic, SKColors.Blue);
        if (darter) WriteSprite(Path.Combine(bubbles.FullName, "darter.png"), SKColors.Red);
        if (sweeper) WriteSprite(Path.Combine(bubbles.FullName, "sweeper.png"), SKColors.Lime);
        var oldRoots = ChaosArt.RootsOverride;
        var oldImages = (BubbleOverlay.Image, BubbleOverlay.TeaseImage, BubbleOverlay.BrainDrainImage, BubbleOverlay.DarterImage, BubbleOverlay.SweeperImage);
        bool oldChecks = BubbleOverlay.SkipPlatformChecksForTests, oldSkia = CoreSettings.Current.ChaosSkiaFxEnabled;
        var host = new Window();
        var renderWindow = new BubbleOverlayWindow(0, new PixelRect(0, 0, 240, 240), 1) { Width = 240, Height = 240 };
        try
        {
            ChaosArt.ResetForTest();
            ChaosArt.RootsOverride = () => new[] { root.FullName };
            BubbleOverlay.SkipPlatformChecksForTests = true;
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            host.Show();
            BubbleOverlay.BeginChaos(host, _ => { });
            Assert.True(BubbleOverlay.WindowCount > 0);
            BubbleOverlay.Image = ChaosArt.TryLoad(generic);
            Assert.NotNull(BubbleOverlay.Image);
            BubbleOverlay.Field.Bubbles.Clear();
            var spec = new EffectBubbleSpec { IsDarter = true, VariantId = "darter", SizePx = 100, DarterSpeed = 9, LifetimeMs = 8000 };
            var rabbit = BubbleOverlay.SpawnChaos(spec, new Random(7));
            rabbit.Screen = 0; rabbit.X = rabbit.Y = 70; rabbit.Size = 100; rabbit.Angle = 0; rabbit.Scale = 1;
            renderWindow.Show(); renderWindow.UpdateLayout();
            var normalPixel = Sample(renderWindow);
            Assert.Equal(darter ? SKColors.Red : SKColors.Blue, normalPixel);
            rabbit.Spec = new EffectBubbleSpec { IsDarter = true, IsSweeper = true, VariantId = "darter", SizePx = 100 };
            var sweeperPixel = Sample(renderWindow);
            Assert.Equal(sweeper ? SKColors.Lime : darter ? SKColors.Red : SKColors.Blue, sweeperPixel);
        }
        finally
        {
            BubbleOverlay.EndChaos(); BubbleOverlay.Stop();
            renderWindow.Close(); host.Close();
            BubbleOverlay.SkipPlatformChecksForTests = oldChecks;
            CoreSettings.Current.ChaosSkiaFxEnabled = oldSkia;
            (BubbleOverlay.Image, BubbleOverlay.TeaseImage, BubbleOverlay.BrainDrainImage, BubbleOverlay.DarterImage, BubbleOverlay.SweeperImage) = oldImages;
            ChaosArt.ResetForTest(); ChaosArt.RootsOverride = oldRoots;
            root.Delete(true);
        }
    });

    [Fact]
    public void Both_rabbit_sprites_ship_with_the_head() => AvaloniaTestDispatcher.Run(() =>
    {
        var root = Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", "bubbles");
        using var darter = new Bitmap(Path.Combine(root, "darter.png"));
        using var sweeper = new Bitmap(Path.Combine(root, "sweeper.png"));
        Assert.True(darter.PixelSize.Width > 0);
        Assert.True(sweeper.PixelSize.Width > 0);
    });
}
