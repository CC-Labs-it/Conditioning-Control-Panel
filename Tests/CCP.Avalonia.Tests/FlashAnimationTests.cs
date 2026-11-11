using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using SkiaSharp;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class FlashAnimationTests
{
    private static Task OnUi(Action action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        action();
        return Task.CompletedTask;
    });
    private static void WithGif(int count, Action<string> body)
    {
        var root = Directory.CreateTempSubdirectory("ccp-flash-").FullName; var path = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "images")).FullName, "flash.gif");
        File.WriteAllBytes(path, Gif(count));
        try { body(path); }
        finally { Directory.Delete(root, recursive: true); }
    }
    [Fact]
    public Task Restore_previous_composes_every_frame_and_restores_the_background() => OnUi(() => WithGif(8, path =>
    {
        var (frames, delay) = FlashOverlay.DecodePicture(path, 16);
        try
        {
            Assert.Equal(8, frames.Count); Assert.Equal(100, delay.TotalMilliseconds);
            for (int f = 2; f < frames.Count; f++)
            {
                Assert.Equal(new SKColor(255, 215, 0), Pixel(frames[f], SquareX(f), 6));
                Assert.Equal(new SKColor(40, 10, 60), Pixel(frames[f], SquareX(f - 1), 6));
            }
        }
        finally { FlashOverlay.DisposeFrames(frames); }
    }));
    [Fact]
    public Task Webp_animates_and_a_single_frame_gif_falls_back_to_a_still() => OnUi(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccp-flash-{Guid.NewGuid():N}.webp");
        // Two lossless 4x4 frames, red then blue, at 100 ms each.
        File.WriteAllBytes(path, Convert.FromBase64String("UklGRoQAAABXRUJQVlA4WAoAAAACAAAAAwAAAwAAQU5JTQYAAAAAAAAAAABBTk1GKAAAAAAAAAAAAAMAAAMAAGQAAAJWUDhMDwAAAC8DwAAABxD9j/4HIqL/AQBBTk1GKAAAAAAAAAAAAAMAAAMAAGQAAABWUDhMDwAAAC8DwAAABxDR//4HIqL/AQA="));
        try
        {
            var (frames, delay) = FlashOverlay.DecodePicture(path, 8);
            try
            {
                var (shared, _) = FlashOverlay.DecodePicture(path, 16); try { Assert.Same(frames, shared); } finally { FlashOverlay.DisposeFrames(shared); }
                Assert.Equal(2, frames.Count); Assert.Equal(100, delay.TotalMilliseconds);
                Assert.Equal(new SKColor(255, 0, 0), Pixel(frames[0], 1, 1));
                Assert.Equal(new SKColor(0, 0, 255), Pixel(frames[1], 1, 1));
            }
            finally { FlashOverlay.DisposeFrames(frames); }
        }
        finally { File.Delete(path); }
        WithGif(1, still =>
        {
            var (frames, _) = FlashOverlay.DecodePicture(still, 100);
            try { Assert.Single(frames); Assert.Equal(new PixelSize(16, 16), frames[0].PixelSize); }
            finally { FlashOverlay.DisposeFrames(frames); }
        });
    });
    [Fact]
    public Task Long_clips_keep_the_tail_and_scale_the_delay_by_the_stride() => OnUi(() => WithGif(130, path =>
    {
        var (frames, delay) = FlashOverlay.DecodePicture(path, 8);
        try
        {
            Assert.Equal(44, frames.Count); // ceil(130 / 60) = 3, frames 0..129
            Assert.Equal(300, delay.TotalMilliseconds);
            Assert.Equal(new PixelSize(8, 8), frames[0].PixelSize);
        }
        finally { FlashOverlay.DisposeFrames(frames); }
    }));
    [Fact]
    public Task Shown_flash_uses_speed_and_gentle_delay_and_stops_the_shared_clock() => OnUi(() => WithGif(8, path =>
    {
        var oldProvider = CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = CoreSettings.Current;
        var saved = (s.FlashGifSpeedMultiplier, s.GentleVisuals, s.GentleSpeed, s.FlashGlowEnabled, s.MotionLevel);
        var old = (FlashOverlay.Clock, FlashOverlay.Present, CorePaths.EffectiveAssetsProvider, ConditioningControlPanel.Services.ContentPackStore.Current);
        var clock = new SteppedTime();
        List<Bitmap>? frames = null;
        bool transferred = false;
        try
        {
            // 100 ms / speed 2 / gentle 0.5 = 100 ms
            (s.FlashGifSpeedMultiplier, s.GentleVisuals, s.GentleSpeed, s.FlashGlowEnabled, s.MotionLevel) = (2, true, 0.5, false, MotionLevel.Off);
            (FlashOverlay.Clock, FlashOverlay.Present, CorePaths.EffectiveAssetsProvider, ConditioningControlPanel.Services.ContentPackStore.Current) =
                (clock, (_, _, _) => true, () => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, null);
            var screen = new Window().Screens.All[0]; var picked = FlashOverlay.LoadPictures(1, new[] { screen }, new[] { 0 }, s, new(), null, FlashOverlay.DecodeMax(s, PerformanceTier.Quality));
            var (chosenFrames, delay, rect, chosenPath, chosenScreen) = Assert.Single(picked);
            frames = chosenFrames; Assert.Equal(path, chosenPath);
            Assert.Equal(8, frames.Count);
            Assert.True(FlashOverlay.Show(frames[0], rect, chosenPath, chosenScreen,
                1, TimeSpan.Zero, TimeSpan.FromSeconds(5), false, false, 0, frames: frames, frameDelay: delay));
            transferred = true; var window = FlashOverlay.Active[^1].Window;
            Assert.True(FlashOverlay.AnimationRunning);
            clock.Advance(TimeSpan.FromMilliseconds(99)); FlashOverlay.AnimationTick();
            Assert.Equal(0, window.FrameIndex);
            clock.Advance(TimeSpan.FromMilliseconds(1)); FlashOverlay.AnimationTick();
            Assert.Equal(1, window.FrameIndex);
            clock.Advance(TimeSpan.FromMilliseconds(300)); FlashOverlay.AnimationTick();
            Assert.Equal(4, window.FrameIndex); // delayed ticks skip to the right frame
            s.GentleVisuals = false;
            var (fastFrames, _) = FlashOverlay.DecodePicture(path, 16);
            Assert.True(FlashOverlay.Show(fastFrames[0], rect, chosenPath, chosenScreen,
                1, TimeSpan.Zero, TimeSpan.FromSeconds(5), false, false, 0, frames: fastFrames, frameDelay: TimeSpan.FromMilliseconds(20)));
            var fast = FlashOverlay.Active[^1].Window;
            Assert.Equal(1, TopLevelFrameSource.For(window).SubscriberCount);
            void Frame(int ms)
            {
                clock.Advance(TimeSpan.FromMilliseconds(ms));
                typeof(TopLevelFrameSource).GetMethod("OnFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(TopLevelFrameSource.For(FlashOverlay.Animated[0]), new object[] { TimeSpan.FromTicks(clock.GetTimestamp()) });
            }
            Frame(7); Frame(7);
            Assert.Equal(1, fast.FrameIndex); // 10 ms frame delay on a 7 ms render cadence
            window.Close();
            Assert.Equal(0, TopLevelFrameSource.For(window).SubscriberCount);
            Assert.Equal(1, TopLevelFrameSource.For(fast).SubscriberCount);
            Frame(7); Frame(7);
            Assert.Equal(2, fast.FrameIndex);
            FlashOverlay.CloseAll(final: false);
            Assert.Empty(FlashOverlay.Animated);
            Assert.False(FlashOverlay.AnimationRunning);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            if (!transferred && frames != null) FlashOverlay.DisposeFrames(frames);
            (FlashOverlay.Clock, FlashOverlay.Present, CorePaths.EffectiveAssetsProvider, ConditioningControlPanel.Services.ContentPackStore.Current) = old;
            (s.FlashGifSpeedMultiplier, s.GentleVisuals, s.GentleSpeed, s.FlashGlowEnabled, s.MotionLevel) = saved;
            CoreSettings.ServiceProvider = oldProvider;
        }
    }));
    [Fact]
    public async Task Animated_decodes_wait_when_both_native_decode_slots_are_in_use()
    {
        await OnUi(() => { });
        var path = Path.Combine(Path.GetTempPath(), $"ccp-flash-{Guid.NewGuid():N}.gif");
        File.WriteAllBytes(path, Gif(8));
        var gate = FlashOverlay.DecodeGate;
        gate.Wait(); gate.Wait();
        bool oneReleased = false;
        Task<(List<Bitmap> Frames, TimeSpan Delay)>? pending = null;
        try
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending = Task.Run(() => { started.SetResult(true); return FlashOverlay.DecodePicture(path, 16); });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(150);
            bool waited = !pending.IsCompleted;
            gate.Release(); oneReleased = true;
            var (frames, _) = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            FlashOverlay.DisposeFrames(frames);
            Assert.True(waited);
        }
        finally
        {
            if (!oneReleased) gate.Release();
            gate.Release();
            if (pending != null && !pending.IsCompleted) await pending.WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(path);
        }
    }
    [Fact]
    public async Task Retired_queued_flashes_dispose_every_decoded_frame_without_showing()
    {
        await OnUi(() => { }); // Set up the same dispatcher and headless platform first.
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"ccp-flash-{Guid.NewGuid():N}.gif");
            File.WriteAllBytes(path, Gif(8));
            var (frames, delay) = FlashOverlay.DecodePicture(path, 16);
            var (present, shown) = (FlashOverlay.Present, 0);
            try
            {
                FlashOverlay.Present = (_, _, _) => { shown++; return true; };
                var screen = new Window().Screens.All[0];
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                FlashOverlay.Schedule(new() { (frames, delay, new PixelRect(0, 0, 16, 16), path, screen) },
                    50, 0, FlashOverlay.Generation, 30, 1, TimeSpan.Zero, TimeSpan.FromSeconds(1),
                    false, false, 0, () => done.SetResult(true));
                FlashOverlay.CloseAll(final: false);
                await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(0, shown);
                foreach (var frame in frames)
                {
                    using var stream = new MemoryStream();
                    Assert.ThrowsAny<Exception>(() => frame.Save(stream, PngBitmapEncoderOptions.Default));
                }
            }
            finally
            {
                FlashOverlay.CloseAll(final: false);
                FlashOverlay.Present = present;
                File.Delete(path);
            }
        });
    }
    [Fact]
    public Task Large_burst_uses_tier_cap_and_shares_frames_until_last_release() => OnUi(() => WithGif(10, path =>
    {
        var root = Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
        File.WriteAllBytes(path, Gif(Enumerable.Range(0, 10).Select(i => i == 0 ? 1 : 3).ToArray(), 2200, 1300));
        var old = (CorePaths.EffectiveAssetsProvider, ConditioningControlPanel.Services.ContentPackStore.Current);
        var s = new AppSettings { ImageScale = 250 };
        var flashes = new List<(List<Bitmap> Frames, TimeSpan Delay, PixelRect Rect, string Path, Screen Screen)>();
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            ConditioningControlPanel.Services.ContentPackStore.Current = null;
            var screen = MultiMonitorResultTests.MakeScreen(2.5, new PixelRect(0, 0, 3840, 2160), new PixelRect(0, 0, 3840, 2160), true);
            flashes = FlashOverlay.LoadPictures(5, new[] { screen }, new[] { 0 }, s, new(), null, FlashOverlay.DecodeMax(s, PerformanceTier.Quality));
            Assert.Equal(5, flashes.Count);
            Assert.True(flashes[0].Rect.Width > 2048);
            Assert.Equal(2048, flashes[0].Frames[0].PixelSize.Width);
            Assert.All(flashes, item => Assert.Same(flashes[0].Frames, item.Frames));
            var bitmap = flashes[0].Frames[0];
            while (flashes.Count > 1) { FlashOverlay.DisposeFrames(flashes[^1].Frames); flashes.RemoveAt(flashes.Count - 1); }
            using (var alive = new MemoryStream()) bitmap.Save(alive, PngBitmapEncoderOptions.Default);
            FlashOverlay.DisposeFrames(flashes[0].Frames); flashes.Clear();
            using var disposed = new MemoryStream();
            Assert.ThrowsAny<Exception>(() => bitmap.Save(disposed, PngBitmapEncoderOptions.Default));
        }
        finally
        {
            foreach (var flash in flashes) FlashOverlay.DisposeFrames(flash.Frames);
            (CorePaths.EffectiveAssetsProvider, ConditioningControlPanel.Services.ContentPackStore.Current) = old;
        }
    }));
    private static SKColor Pixel(Bitmap bitmap, int x, int y)
    {
        using var bytes = new MemoryStream();
        bitmap.Save(bytes, PngBitmapEncoderOptions.Default);
        bytes.Position = 0;
        using var decoded = SKBitmap.Decode(bytes);
        return decoded.GetPixel(x, y);
    }
    private const int Size = 16, SquareY = 6;
    private static byte[] Gif(int count) => Gif(Enumerable.Range(0, count).Select(i => i == 0 ? 1 : 3).ToArray());
    // Frame 0 is the whole background (index 0); frame f >= 1 is a 2x2 gold square (index 2).
    private static int SquareX(int frame) => 2 * (frame - 1) % (Size - 2);
    // A tiny GIF89a writer: 4-colour global table, looping, one image per disposal entry.
    private static byte[] Gif(int[] disposals, int width = Size, int height = Size)
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)v); ms.WriteByte((byte)(v >> 8)); }
        ms.Write(Encoding.ASCII.GetBytes("GIF89a")); U16(width); U16(height);
        ms.Write(new byte[] { 0xF1, 0, 0, 40, 10, 60, 255, 105, 180, 255, 215, 0, 0, 0, 0, 0x21, 0xFF, 0x0B });
        ms.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0")); ms.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        for (int f = 0; f < disposals.Length; f++)
        {
            int x = f == 0 ? 0 : SquareX(f), y = f == 0 ? 0 : SquareY, w = f == 0 ? width : 2, h = f == 0 ? height : 2;
            ms.Write(new byte[] { 0x21, 0xF9, 0x04, (byte)(disposals[f] << 2), 10, 0, 0, 0, 0x2C });
            U16(x); U16(y); U16(w); U16(h);
            ms.Write(new byte[] { 0, 2 }); // no local table, LZW minimum code size 2
            var data = Lzw(Enumerable.Repeat((byte)(f == 0 ? 0 : 2), w * h).ToArray());
            for (int i = 0; i < data.Length; i += 255) { int n = Math.Min(255, data.Length - i); ms.WriteByte((byte)n); ms.Write(data, i, n); }
            ms.WriteByte(0);
        }
        ms.WriteByte(0x3B);
        return ms.ToArray();
    }
    // Uncompressed LZW: a clear code before every two pixels keeps every code 3 bits wide (clear = 4, end = 5).
    private static byte[] Lzw(byte[] indices)
    {
        var output = new List<byte>();
        int acc = 0, bits = 0;
        void Code(int c) { acc |= c << bits; bits += 3; while (bits >= 8) { output.Add((byte)acc); acc >>= 8; bits -= 8; } }
        for (int i = 0; i < indices.Length; i += 2) { Code(4); Code(indices[i]); if (i + 1 < indices.Length) Code(indices[i + 1]); }
        Code(5);
        if (bits > 0) output.Add((byte)acc);
        return output.ToArray();
    }
}
