using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
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
            var uneven = (byte[])TwoFrameGif.Clone();
            // Second frame delay: 300 ms; first is 100 ms.
            var second = Array.LastIndexOf(uneven, (byte)0xF9);
            uneven[second + 3] = 30;
            File.WriteAllBytes(path, uneven);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var old = FlashOverlay.Present;
                var speed = CoreSettings.Current.FlashGifSpeedMultiplier;
                try
                {
                    FlashOverlay.Present = (_, _, _) => true;
                    CoreSettings.Current.FlashGifSpeedMultiplier = 2;
                    var screen = new Window().Screens.All[0];
                    Assert.True(FlashOverlay.Show(new Bitmap(path), new PixelRect(100, 100, 100, 100), path, screen,
                        1, TimeSpan.Zero, TimeSpan.FromMinutes(1), false, true, 0));
                    var w = FlashOverlay.Active[^1].Window;
                    Assert.NotNull(w.GifDecoding);
                    await w.GifDecoding!;
                    Assert.Equal(2, w.GifFrameCount);
                    Assert.True(w.GifAnimating);
                    Assert.Equal(TimeSpan.FromMilliseconds(100), w.GifDelay);
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
                finally { FlashOverlay.CloseAll(final: false); FlashOverlay.Present = old; CoreSettings.Current.FlashGifSpeedMultiplier = speed; }
            });
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task DecoderStopsAtSixHundredFrames()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-flash-limits-").FullName;
        try
        {
            int start = Array.IndexOf(TwoFrameGif, (byte)0xF9) - 1;
            int next = Array.LastIndexOf(TwoFrameGif, (byte)0xF9) - 1;
            var longPath = Path.Combine(dir, "long.gif");
            using (var file = File.Create(longPath))
            {
                file.Write(TwoFrameGif, 0, start);
                for (var i = 0; i < 1200; i++) file.Write(TwoFrameGif, start, next - start);
                file.WriteByte(0x3B);
            }
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var (frames, delay) = FlashOverlayWindow.DecodeFrames(longPath);
                try
                {
                    Assert.Equal(30, frames.Count);
                    Assert.Equal(TimeSpan.FromMilliseconds(2000), delay);
                }
                finally { foreach (var frame in frames) frame.Dispose(); }
                return Task.CompletedTask;
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DecoderRejectsOversizedSource()
    {
        var path = Path.GetTempFileName();
        try
        {
            var big = (byte[])TwoFrameGif.Clone();
            big[6] = big[8] = 1; big[7] = big[9] = 16; // 4097 x 4097
            File.WriteAllBytes(path, big);
            var (frames, _) = FlashOverlayWindow.DecodeFrames(path);
            try { Assert.Empty(frames); }
            finally { foreach (var frame in frames) frame.Dispose(); }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DecoderAveragesNonuniformFrameDelays()
    {
        var path = Path.GetTempFileName();
        try
        {
            var gif = (byte[])TwoFrameGif.Clone();
            gif[Array.LastIndexOf(gif, (byte)0xF9) + 3] = 30;
            File.WriteAllBytes(path, gif);
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var (frames, delay) = FlashOverlayWindow.DecodeFrames(path);
                try { Assert.Equal(TimeSpan.FromMilliseconds(200), delay); }
                finally { foreach (var frame in frames) frame.Dispose(); }
                return Task.CompletedTask;
            });
        }
        finally { File.Delete(path); }
    }

}
