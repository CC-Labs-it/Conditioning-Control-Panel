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
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using SkiaSharp;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FlashAnimationTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now = 100_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(int ms) => Now += ms * TimeSpan.TicksPerMillisecond;
    }

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
        var root = Directory.CreateTempSubdirectory("ccp-flash-").FullName;
        var path = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "images")).FullName, "flash.gif");
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
            Assert.Equal(8, frames.Count);
            Assert.Equal(100, delay.TotalMilliseconds);
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
            var (frames, delay) = FlashOverlay.DecodePicture(path, 4);
            try
            {
                Assert.Equal(2, frames.Count);
                Assert.Equal(100, delay.TotalMilliseconds);
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
        var oldClock = FlashOverlay.Clock;
        var oldPresent = FlashOverlay.Present;
        var oldAssets = CorePaths.EffectiveAssetsProvider;
        var oldPacks = ConditioningControlPanel.Services.ContentPackStore.Current;
        var clock = new Clock();
        List<Bitmap>? frames = null;
        bool transferred = false;
        try
        {
            s.FlashGifSpeedMultiplier = 2;
            s.GentleVisuals = true;
            s.GentleSpeed = 0.5; // 100 ms / 2 / 0.5 = 100 ms
            s.FlashGlowEnabled = false;
            s.MotionLevel = MotionLevel.Off;
            FlashOverlay.Clock = clock;
            FlashOverlay.Present = (_, _, _) => true;
            CorePaths.EffectiveAssetsProvider = () => Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
            ConditioningControlPanel.Services.ContentPackStore.Current = null;
            var screen = new Window().Screens.All[0];
            var picked = FlashOverlay.LoadPictures(1, new[] { screen }, new[] { 0 }, s, new(), null);
            var (chosenFrames, delay, rect, chosenPath, chosenScreen) = Assert.Single(picked);
            frames = chosenFrames;
            Assert.Equal(path, chosenPath);
            Assert.Equal(8, frames.Count);
            Assert.True(FlashOverlay.Show(frames[0], rect, chosenPath, chosenScreen,
                1, TimeSpan.Zero, TimeSpan.FromSeconds(5), false, false, 0, frames: frames, frameDelay: delay));
            transferred = true;
            var window = FlashOverlay.Active[^1].Window;
            Assert.True(FlashOverlay.AnimationRunning);
            clock.Advance(99); FlashOverlay.AnimationTick();
            Assert.Equal(0, window.FrameIndex);
            clock.Advance(1); FlashOverlay.AnimationTick();
            Assert.Equal(1, window.FrameIndex);
            clock.Advance(300); FlashOverlay.AnimationTick();
            Assert.Equal(4, window.FrameIndex); // delayed ticks skip to the right frame
            FlashOverlay.CloseAll(final: false);
            Assert.Empty(FlashOverlay.Animated);
            Assert.False(FlashOverlay.AnimationRunning);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            if (!transferred && frames != null) FlashOverlay.DisposeFrames(frames);
            FlashOverlay.Clock = oldClock;
            FlashOverlay.Present = oldPresent;
            CorePaths.EffectiveAssetsProvider = oldAssets;
            ConditioningControlPanel.Services.ContentPackStore.Current = oldPacks;
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
            var present = FlashOverlay.Present;
            var shown = 0;
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
    public async Task A_scheduled_flash_keeps_its_animation_frames()
    {
        await OnUi(() => { });
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"ccp-flash-{Guid.NewGuid():N}.gif");
            File.WriteAllBytes(path, Gif(8));
            var (frames, delay) = FlashOverlay.DecodePicture(path, 16);
            var present = FlashOverlay.Present;
            try
            {
                FlashOverlay.Present = (_, _, _) => true;
                var screen = new Window().Screens.All[0];
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                FlashOverlay.Schedule(new() { (frames, delay, new PixelRect(0, 0, 16, 16), path, screen) },
                    1, 0, FlashOverlay.Generation, 30, 1, TimeSpan.Zero, TimeSpan.FromSeconds(5),
                    false, false, 0, () => done.SetResult(true));
                await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Single(FlashOverlay.Active);
                Assert.True(FlashOverlay.AnimationRunning);
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
    public void Speed_has_the_wpf_floor_and_rejects_invalid_values()
    {
        Assert.Equal(10, FlashOverlayWindow.ScaleFrameDelay(TimeSpan.FromMilliseconds(20), 4).TotalMilliseconds);
        Assert.Equal(100, FlashOverlayWindow.ScaleFrameDelay(TimeSpan.Zero, double.NaN).TotalMilliseconds);
        Assert.Equal(400, FlashOverlayWindow.ScaleFrameDelay(TimeSpan.FromMilliseconds(100), 0.1).TotalMilliseconds);
    }

    private static SKColor Pixel(Bitmap bitmap, int x, int y)
    {
        using var bytes = new MemoryStream();
        bitmap.Save(bytes, PngBitmapEncoderOptions.Default);
        bytes.Position = 0;
        using var decoded = SKBitmap.Decode(bytes);
        return decoded.GetPixel(x, y);
    }

    // The same 16x16 background and restore-previous overlays as WPF's
    // AnimatedWebpGifDisposalTests. Each frame after the first owns a moving gold square.
    private static int SquareX(int frame) => 2 * (frame - 1) % 14;
    private static byte[] Gif(int count)
    {
        using var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)v); ms.WriteByte((byte)(v >> 8)); }
        ms.Write(Encoding.ASCII.GetBytes("GIF89a"));
        U16(16); U16(16);
        ms.WriteByte(0xF1); ms.WriteByte(0); ms.WriteByte(0);
        ms.Write(new byte[] { 40, 10, 60, 255, 105, 180, 255, 215, 0, 0, 0, 0 });
        for (int f = 0; f < count; f++)
        {
            int x = f == 0 ? 0 : SquareX(f), y = f == 0 ? 0 : 6;
            int w = f == 0 ? 16 : 2, h = f == 0 ? 16 : 2;
            ms.Write(new byte[] { 0x21, 0xF9, 4, (byte)((f == 0 ? 1 : 3) << 2), 10, 0, 0, 0, 0x2C });
            U16(x); U16(y); U16(w); U16(h);
            ms.WriteByte(0); ms.WriteByte(2);
            var data = Lzw(Enumerable.Repeat((byte)(f == 0 ? 0 : 2), w * h).ToArray());
            for (int i = 0; i < data.Length; i += 255)
            {
                int n = Math.Min(255, data.Length - i);
                ms.WriteByte((byte)n); ms.Write(data, i, n);
            }
            ms.WriteByte(0);
        }
        ms.WriteByte(0x3B);
        return ms.ToArray();
    }

    private static byte[] Lzw(byte[] indices)
    {
        var output = new List<byte>();
        int acc = 0, bits = 0;
        void Code(int c)
        {
            acc |= c << bits; bits += 3;
            while (bits >= 8) { output.Add((byte)acc); acc >>= 8; bits -= 8; }
        }
        for (int i = 0; i < indices.Length; i += 2)
        {
            Code(4); Code(indices[i]);
            if (i + 1 < indices.Length) Code(indices[i + 1]);
        }
        Code(5);
        if (bits > 0) output.Add((byte)acc);
        return output.ToArray();
    }
    [Theory, InlineData(true), InlineData(false)]
    public async Task BurstShowsBeforeLengthAndUsesLateOrBoundedFallback(bool reportLength)
    {
        await OnUi(() => { });
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var path = Path.GetTempFileName(); var clock = new Clock(); var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var (present, oldClock, play, duck, unduck, delay, voice, busy) = (FlashOverlay.Present, FlashOverlay.Clock, CoreAudio.PlayStoppableProvider,
                CoreAudio.DuckProvider, CoreAudio.UnduckProvider, FlashOverlay.UnduckDelay, FlashOverlay.ShowVoice, FlashOverlay.WhisperBusyUntil);
            Action<TimeSpan>? report = null; var releases = 0; var shows = 0;
            try
            {
                FlashOverlay.Clock = clock; FlashOverlay.ShowVoice = _ => { }; FlashOverlay.Present = (_, _, _) => { if (++shows == 1) shown.TrySetResult(); else later.TrySetResult(); return true; };
                CoreAudio.PlayStoppableProvider = (_, _, _, started, _) => { report = started; started!(TimeSpan.Zero); return () => { }; };
                CoreAudio.DuckProvider = _ => { }; CoreAudio.UnduckProvider = _ => releases++; FlashOverlay.UnduckDelay = _ => Task.CompletedTask;
                var picture = new Bitmap(global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png"))); var picture2 = new Bitmap(global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png")));
                var screen = new Window().Screens.All[0]; var settings = new AppSettings { FlashAudioEnabled = true, MasterVolume = 50, AudioDuckingEnabled = true };
                var pending = FlashOverlay.ShowBurst(new() { (new List<Bitmap> { picture }, TimeSpan.Zero, new PixelRect(10, 10, 100, 100), path, screen), (new List<Bitmap> { picture2 }, TimeSpan.Zero, new PixelRect(120, 10, 100, 100), path, screen) }, settings, FlashOverlay.Generation, null, false, path);
                await shown.Task.WaitAsync(TimeSpan.FromSeconds(2)); var window = Assert.Single(FlashOverlay.Active).Window;
                Assert.False(pending.IsCompleted); Assert.Equal(TimeSpan.FromSeconds(6), window.Lifetime); var born = window.ExpiresAt - 6 * TimeSpan.TicksPerSecond; clock.Advance(2000);
                if (reportLength) report!(TimeSpan.FromSeconds(8));
                await pending.WaitAsync(TimeSpan.FromSeconds(5)); await later.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.Equal(2, FlashOverlay.Active.Count); Assert.All(FlashOverlay.Active, x => Assert.Equal(TimeSpan.FromSeconds(reportLength ? 9 : 6), x.Window.Lifetime));
                Assert.Equal(born + (reportLength ? 9 : 6) * TimeSpan.TicksPerSecond, window.ExpiresAt);
                Assert.Equal(1, releases);
            }
            finally
            {
                FlashOverlay.CloseAll(false); (FlashOverlay.Present, FlashOverlay.Clock, CoreAudio.PlayStoppableProvider, CoreAudio.DuckProvider,
                    CoreAudio.UnduckProvider, FlashOverlay.UnduckDelay, FlashOverlay.ShowVoice, FlashOverlay.WhisperBusyUntil) = (present, oldClock, play, duck, unduck, delay, voice, busy);
                File.Delete(path);
            }
        });
    }

}
