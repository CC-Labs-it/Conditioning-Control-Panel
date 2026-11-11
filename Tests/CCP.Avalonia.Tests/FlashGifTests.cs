using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashGifTests
{
    private static readonly byte[] TwoFrameGif =
    {
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF,
        0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00,
        0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00,
        0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x4C, 0x01, 0x00,
        0x3B,
    };

    [Fact]
    public async Task ShownGifLoopsAndStopsWhenClosed()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-flash-gif-").FullName;
        try
        {
            var path = Path.Combine(dir, "loop.gif");
            File.WriteAllBytes(path, TwoFrameGif);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var old = FlashOverlay.Present;
                try
                {
                    FlashOverlay.Present = (_, _, _) => true;
                    var screen = new Window().Screens.All[0];
                    Assert.True(FlashOverlay.Show(new Bitmap(path), new PixelRect(100, 100, 100, 100), path, screen,
                        1, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, true, 0));
                    var w = FlashOverlay.Active[^1].Window;
                    Assert.NotNull(w.GifDecoding);
                    await w.GifDecoding!;
                    Assert.Equal(2, w.GifFrameCount);
                    Assert.True(w.GifAnimating);
                    var first = w.PictureSource;
                    w.NextGifFrame();
                    Assert.NotSame(first, w.PictureSource);
                    w.NextGifFrame();
                    Assert.Same(first, w.PictureSource);
                    w.Close();
                    Assert.False(w.GifAnimating);
                    Assert.Equal(0, w.GifFrameCount);
                    Assert.Null(w.PictureSource);
                }
                finally { FlashOverlay.CloseAll(final: false); FlashOverlay.Present = old; }
            });
        }
        finally { Directory.Delete(dir, true); }
    }
}
