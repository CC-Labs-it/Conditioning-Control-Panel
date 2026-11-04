using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashAnimationTests
{
    private const string Gif = "R0lGODlhEAAIAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACgAAACwAAAAAEAAIAAAIFQABCBxIsKDBgwgTKlzIsKHDhwgDAgAh+QQBFAABACwAAAAAEAAIAIEAAP8AAAAAAAAAAAAIFQABCBxIsKDBgwgTKlzIsKHDhwgDAgA7";
    private const string Webp = "UklGRoQAAABXRUJQVlA4WAoAAAACAAAADwAABwAAQU5JTQYAAAAAAAAAAABBTk1GKAAAAAAAAAAAAA8AAAcAAGQAAAJWUDhMDwAAAC8PwAEABxD9j/4HIqL/AQBBTk1GKAAAAAAAAAAAAA8AAAcAAMgAAABWUDhMDwAAAC8PwAEABxDR//4HIqL/AQA=";

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Theory]
    [InlineData(Gif, ".gif")]
    [InlineData(Webp, ".webp")]
    public Task Frames_scale_time_loop_and_release_on_close(string data, string extension) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        File.WriteAllBytes(path, Convert.FromBase64String(data));
        try
        {
            var (frames, delay) = FlashAnimation.Decode(path, 8);
            Assert.Equal(2, frames.Count);
            Assert.Equal(8, frames[0].PixelSize.Width);
            Assert.Equal(4, frames[0].PixelSize.Height);
            Assert.Equal(150, delay.TotalMilliseconds);
            foreach (var frame in frames) frame.Dispose();
            Assert.Equal(75, FlashAnimation.Delay(delay, 2).TotalMilliseconds);
            Assert.Equal(10, FlashAnimation.Delay(TimeSpan.FromMilliseconds(20), 4).TotalMilliseconds);
            var w = new FlashOverlayWindow();
            w.Show();
            await w.StartAnimation(path, 8, 1);
            Assert.Equal(2, w.AnimationFrameCount);
            w.AdvanceAnimation();
            Assert.Equal(1, w.AnimationIndex);
            w.AdvanceAnimation();
            Assert.Equal(0, w.AnimationIndex);
            w.Close();
            Assert.Equal(0, w.AnimationFrameCount);
        }
        finally { File.Delete(path); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Expiry_and_stop_release_frames(bool stop) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        File.WriteAllBytes(path, Convert.FromBase64String(Gif));
        try
        {
            var w = new FlashOverlayWindow();
            w.Show();
            FlashOverlay.Active.Add((w, new PixelRect(0, 0, 8, 4)));
            w.Closed += (_, _) => FlashOverlay.Active.RemoveAll(e => e.Window == w);
            await w.StartAnimation(path, 8, 1);
            Assert.Equal(2, w.AnimationFrameCount);
            var frame = Assert.IsType<Bitmap>(w.PictureSource);
            w.AdvanceAnimation();
            var next = Assert.IsType<Bitmap>(w.PictureSource);
            Assert.NotSame(frame, next);
            if (stop) FlashOverlay.CloseAll(final: false);
            else { w.Expire(); await Task.Delay(100); }
            Assert.Equal(0, w.AnimationFrameCount);
            Assert.ThrowsAny<Exception>(() => frame.PixelSize);
            Assert.ThrowsAny<Exception>(() => next.PixelSize);
        }
        finally { FlashOverlay.CloseAll(final: false); File.Delete(path); }
    });

    [Fact]
    public Task Closing_during_decode_discards_the_result() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var w = new FlashOverlayWindow();
        w.Show();
        var pending = w.StartAnimation(Path.Combine(AppContext.BaseDirectory, "Assets", "flash-70.gif"), 8, 1);
        w.Close();
        await pending;
        Assert.Equal(0, w.AnimationFrameCount);
    });

    [Fact]
    public Task Cancelled_decode_does_not_start() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = FlashAnimation.Decode(Path.Combine(AppContext.BaseDirectory, "Assets", "flash-70.gif"), 8, cts.Token);
        Assert.Empty(result.Frames);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Decode_limits_frame_count_and_rejects_oversized_source() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var (frames, delay) = FlashAnimation.Decode(Path.Combine(AppContext.BaseDirectory, "Assets", "flash-70.gif"), 8);
        Assert.Equal(35, frames.Count);
        Assert.Equal(200, delay.TotalMilliseconds);
        foreach (var frame in frames) frame.Dispose();
        var bytes = Convert.FromBase64String(Gif);
        bytes[6] = 1; bytes[7] = 0x10; bytes[8] = 1; bytes[9] = 0x10;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        File.WriteAllBytes(path, bytes);
        try { Assert.Empty(FlashAnimation.Decode(path, 8).Frames); }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 2, false, 75)]
    [InlineData(true, 2, false, 150)]
    [InlineData(false, 4, true, 10)]
    [InlineData(true, 4, true, 20)]
    public Task Window_applies_gentle_speed_after_gif_speed(bool gentle, double gifSpeed, bool shortDelay, double expectedMs) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        service.Current.GentleVisuals = gentle;
        service.Current.GentleSpeed = 0.5;
        CoreSettings.ServiceProvider = () => service;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        var bytes = Convert.FromBase64String(Gif);
        if (shortDelay)
            for (int i = 0; i < bytes.Length - 5; i++)
                if (bytes[i] == 0x21 && bytes[i + 1] == 0xf9 && bytes[i + 2] == 4)
                    { bytes[i + 4] = 2; bytes[i + 5] = 0; }
        File.WriteAllBytes(path, bytes);
        var w = new FlashOverlayWindow();
        try
        {
            w.Show();
            await w.StartAnimation(path, 8, gifSpeed);
            Assert.Equal(expectedMs, w.AnimationInterval!.Value.TotalMilliseconds);
        }
        finally { w.Close(); CoreSettings.ServiceProvider = oldProvider; File.Delete(path); }
    });

    [Fact]
    public Task Long_animation_strides_by_total_frames_and_caps_decoding() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "flash-700.gif");
        var (frames, delay) = FlashAnimation.Decode(path, 2);
        try
        {
            Assert.Equal(50, frames.Count); // 0, 12, ... 588: decode at most 600 of 700
            Assert.Equal(1200, delay.TotalMilliseconds);
            using var png = new MemoryStream();
            frames[49].Save(png);
            using var image = SKBitmap.Decode(png.ToArray());
            var pixel = image.GetPixel(0, 0);
            Assert.Equal((byte)76, pixel.Red);   // frame 588
            Assert.Equal((byte)2, pixel.Green);
            Assert.Equal((byte)17, pixel.Blue);
        }
        finally { foreach (var frame in frames) frame.Dispose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Invalid_or_still_media_leaves_the_still_picture() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsureApp();
        var w = new FlashOverlayWindow();
        w.Show();
        var original = w.PictureSource;
        await w.StartAnimation(Path.Combine(AppContext.BaseDirectory, "Assets", "flash-still.webp"), 8, 1);
        Assert.Same(original, w.PictureSource);
        Assert.Equal(0, w.AnimationFrameCount);
        await w.StartAnimation(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".webp"), 8, 1);
        Assert.Equal(0, w.AnimationFrameCount);
        Assert.Same(original, w.PictureSource);
        w.Close();
    });
}
