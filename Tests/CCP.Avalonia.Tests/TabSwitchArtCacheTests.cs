using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TabSwitchArtCacheTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CachedPicturesRepaintAfterArtChangesAndResize(bool split) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        ConditioningControlPanel.CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
        using var red = Picture(Brushes.Red);
        using var blue = Picture(Brushes.Blue);
        Control card = split
            ? new SplitFeatureCard { IconA = red, IconB = blue, TitleA = "", TitleB = "", IsActiveA = true, IsActiveB = true }
            : new FeatureCard { Icon = red, Title = "" };
        var art = card.FindControl<Border>(split ? "HalfHostA" : "ImgIconHost")!;
        Assert.IsType<BitmapCache>(art.CacheMode); // repeated tab shows must reuse the resampled plate
        if (split) Assert.IsType<BitmapCache>(card.FindControl<Border>("HalfHostB")!.CacheMode);
        var window = new Window { Width = 240, Height = 160, Content = card };
        try
        {
            window.Show();
            AssertRed(Pixel(window));
            card.IsVisible = false;
            if (card is SplitFeatureCard pair) pair.IconA = blue;
            else ((FeatureCard)card).Icon = blue;
            card.IsVisible = true;
            AssertBlue(Pixel(window)); // a mod/art change while hidden must invalidate the cache
            window.Width = 360;
            window.Height = 240;
            AssertBlue(Pixel(window, 0.7, 0.15)); // resizing must rebuild at the new bounds, preserving the split clip
            if (card is SplitFeatureCard resized) resized.IconA = red;
            else ((FeatureCard)card).Icon = red;
            AssertRed(Pixel(window));
            if (split)
            {
                window.MouseMove(new Point(window.Width - 12, window.Height - 12));
                var hovered = Pixel(window); // the hover wash deliberately tints the blue plate
                Assert.True(hovered.B > hovered.R + 200, $"{hovered}");
                var otherArt = card.FindControl<Border>("HalfHostB")!;
                var caches = (art.CacheMode, otherArt.CacheMode);
                art.CacheMode = otherArt.CacheMode = null;
                Assert.Equal(Pixel(window), hovered); // cached clip must match the uncached reference
                (art.CacheMode, otherArt.CacheMode) = caches;
                window.MouseMove(new Point(-20, -20));
                AssertRed(Pixel(window)); // leaving returns to the resting diagonal
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static RenderTargetBitmap Picture(IBrush color)
    {
        var plate = new Border { Background = color };
        plate.Measure(new Size(8, 8));
        plate.Arrange(new Rect(0, 0, 8, 8));
        var bitmap = new RenderTargetBitmap(new PixelSize(8, 8));
        bitmap.Render(plate);
        return bitmap;
    }

    private static (byte R, byte B) Pixel(Window window, double x = 0.25, double y = 0.25)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame()!;
        var pixel = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect((int)(bitmap.PixelSize.Width * x), (int)(bitmap.PixelSize.Height * y), 1, 1), pixel, 4, 4);
            var first = Marshal.ReadByte(pixel);
            var third = Marshal.ReadByte(pixel, 2);
            return bitmap.Format == global::Avalonia.Platform.PixelFormats.Rgba8888 ? (first, third) : (third, first);
        }
        finally { Marshal.FreeHGlobal(pixel); }
    }

    private static void AssertRed((byte R, byte B) pixel) => Assert.True(pixel.R > 200 && pixel.B < 30, $"{pixel}");
    private static void AssertBlue((byte R, byte B) pixel) => Assert.True(pixel.B > 200 && pixel.R < 30, $"{pixel}");
}
