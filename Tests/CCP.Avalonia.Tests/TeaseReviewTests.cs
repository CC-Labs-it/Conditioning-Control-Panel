using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using SkiaSharp;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class TeaseReviewTests
{
    [Theory]
    [InlineData("flash"), InlineData("sub"), InlineData("pink"), InlineData("spiral"), InlineData("wash"), InlineData("rain")]
    public Task Tease_payloads_reach_the_production_dispatcher(string kind) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (firer, flash, show, pool) = (EffectPayload.Firer, AvApp.FireChaosFlash, CoreSubliminal.ShowProvider, CoreSettings.Current.SubliminalPool);
        var (pinkChecks, spiralChecks, washChecks) = (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests, ChaosFlashOverlay.SkipPlatformChecksForTests);
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            EffectPayload.Firer = AvApp.FireChaosPayload; ChaosRunHost.State!.Shields = 0;
            void Touch(EffectPayload payload) => BubbleOverlay.Press(BubbleOverlay.SpawnChaos(
                new EffectBubbleSpec { IsTease = true, SizePx = 200, Payload = payload }), 0, 0);
            (int?, int?, int?) sent = default;
            AvApp.FireChaosFlash = (_, amount, duration, size, _) => sent = (amount, duration, size);
            var p = new FlashPayload { Strength = 50 }; if (kind == "flash") { Touch(p); var expected = p.ResolveBurst(CoreSettings.Current);
                Assert.Equal(((int?)expected.Amount, (int?)expected.DurationMs, (int?)expected.Size), sent); }
            string? phrase = null; CoreSettings.Current.SubliminalPool = new Dictionary<string, bool> { ["test phrase"] = true };
            CoreSubliminal.ShowProvider = value => phrase = value;
            if (kind == "sub") { Touch(new SubliminalPayload()); Assert.Equal("test phrase", phrase); }
            PinkFilterOverlay.SkipPlatformChecksForTests = true; SpiralOverlay.SkipPlatformChecksForTests = true;
            if (kind == "pink") { Touch(new OverlayPayload("pink_filter") { Strength = 50 });
                Assert.Equal(.47, PinkFilterOverlay.PaintedOpacity, 3); }
            if (kind == "spiral") { Touch(new OverlayPayload("spiral") { Strength = 50 });
                Assert.Equal(SpiralFrames.Paint(.47), SpiralOverlay.PaintedOpacity, 3); }
            ChaosFlashOverlay.SkipPlatformChecksForTests = true;
            var oldWash = ChaosFlashOverlay.LastShow;
            if (kind == "wash") { Touch(new OverlayPayload("braindrain") { Strength = 50 });
                Assert.NotSame(oldWash, ChaosFlashOverlay.LastShow); }
            var oldRain = ChaosGifCascadeOverlay.LastPick; if (kind == "rain") { Touch(new GifCascadePayload()); Assert.NotSame(oldRain, ChaosGifCascadeOverlay.LastPick); }
        }
        finally
        {
            ChaosRunHost.ForceShutdown(); PinkFilterOverlay.Stop(); SpiralOverlay.Stop();
            (EffectPayload.Firer, AvApp.FireChaosFlash, CoreSubliminal.ShowProvider, CoreSettings.Current.SubliminalPool) = (firer, flash, show, pool);
            (PinkFilterOverlay.SkipPlatformChecksForTests, SpiralOverlay.SkipPlatformChecksForTests, ChaosFlashOverlay.SkipPlatformChecksForTests) = (pinkChecks, spiralChecks, washChecks);
        }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(0), InlineData(1), InlineData(2), InlineData(3)]
    public Task Tease_sprite_and_oversized_face_use_bounded_still(int stage) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (stage == 0) { Assert.NotNull(ChaosArt.Resolve("bubbles", "tease")); Assert.Equal((2, 6, 10), (BubbleOverlay.MaxAnimatedTease(PerformanceTier.Performance), BubbleOverlay.MaxAnimatedTease(PerformanceTier.Balanced), BubbleOverlay.MaxAnimatedTease(PerformanceTier.Quality))); return; }
        var dir = Path.Combine(Path.GetTempPath(), "ccp-tease-" + Guid.NewGuid().ToString("N")); var pool = Path.Combine(dir, "teasebubble");
        Directory.CreateDirectory(pool);
        try
        {
            var path = Path.Combine(pool, "face.png");
            using (var pixels = new SKBitmap(4100, 10))
            using (var image = SKImage.FromBitmap(pixels))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) File.WriteAllBytes(path, data.ToArray());
            Assert.Equal(path, BubbleOverlay.PickTeaseFace(dir, new Random(1)));
            if (stage == 1)
            {
                var (frames, _, animated) = BubbleOverlay.DecodeTeaseFace(path, 64, true);
                try { Assert.False(animated); Assert.Equal(64, Assert.Single(frames).PixelSize.Width); }
                finally { foreach (var frame in frames) frame.Dispose(); }
                return;
            }
            var assets = CorePaths.EffectiveAssetsProvider;
            var window = new BubbleOverlayWindow(0, new PixelRect(0, 0, 200, 200), 1) { Width = 200, Height = 200 }; window.Show();
            try
            {
                CorePaths.EffectiveAssetsProvider = () => dir;
                var bubble = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsTease = true, SizePx = 200 }); bubble.X = bubble.Y = 0; bubble.Angle = 90; bubble.Scale = stage == 2 ? .65 : 1;
                for (int i = 0; i < 200 && !BubbleOverlay.TeaseFaces.ContainsKey(bubble); i++) await Task.Delay(10);
                Assert.True(BubbleOverlay.TeaseFaces.ContainsKey(bubble)); Strip(path); BubbleOverlay.TeaseFaces[bubble].Frames.Insert(0, new global::Avalonia.Media.Imaging.Bitmap(path)); window.Sync(); Assert.True(Pixel(window, .5, .2).R > Pixel(window, .5, .2).B + 10, $"top {Pixel(window, .5, .2)}");
                Assert.True(Pixel(window, .5, .8).B > Pixel(window, .5, .8).R + 10, $"bottom {Pixel(window, .5, .8)}"); BubbleOverlay.EndChaos();
                Assert.DoesNotContain(bubble, BubbleOverlay.TeaseFaces.Keys);
            }
            finally { window.Close(); BubbleOverlay.EndChaos(); CorePaths.EffectiveAssetsProvider = assets; }
        }
        finally { Directory.Delete(dir, true); }
    });
    [Theory]
    [InlineData("R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAZAAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQBCgABACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7", 550)]
    [InlineData("R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAQAAACwAAAAAAgACAAAIBgABCAQQEAAh+QQBAQABACwAAAAAAgACAIEAAP8AAAAAAAAAAAAIBgABCAQQEAA7", 100)]
    [InlineData("R0lGODlhAgACAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAsAAAAAAIAAgAACAYAAQgEEBAAIfkEAQAAAQAsAAAAAAIAAgCBAAD/AAAAAAAAAAAACAYAAQgEEBAAOw==", 100)]
    public Task Tease_animation_uses_average_sampled_delay(string gif, double expected) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var path = Path.GetTempFileName(); File.WriteAllBytes(path, Convert.FromBase64String(gif));
        try { var (frames, delay, animated) = BubbleOverlay.DecodeTeaseFace(path, 64, true); try { Assert.True(animated); Assert.Equal(2, frames.Count); Assert.Equal(expected, delay); } finally { foreach (var frame in frames) frame.Dispose(); } }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task Animated_face_plays_with_motion_off() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = CoreSettings.Current.MotionLevel; CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var window = new BubbleOverlayWindow(0, new PixelRect(0, 0, 200, 200), 1) { Width = 200, Height = 200 }; window.Show();
        var bubble = AmbientBubble.SpawnChaos(new Random(1), 0, 0, 0, 200, 200, 1, new EffectBubbleSpec { IsTease = true, SizePx = 200 });
        bubble.X = bubble.Y = bubble.Angle = 0; BubbleOverlay.Field.Bubbles.Add(bubble);
        global::Avalonia.Media.Imaging.Bitmap Frame(SKColor color) { using var pixels = new SKBitmap(2, 2); pixels.Erase(color); using var data = pixels.Encode(SKEncodedImageFormat.Png, 100); return new(data.AsStream()); }
        var frames = new List<global::Avalonia.Media.Imaging.Bitmap> { Frame(SKColors.Red), Frame(SKColors.Blue) };
        try
        {
            BubbleOverlay.TeaseFaces[bubble] = (frames, 500000, Environment.TickCount64 - 500000, true);
            window.Sync(); var pixel = Pixel(window, .5, .35); Assert.True(pixel.B > pixel.R + 10, $"{pixel}");
        }
        finally { BubbleOverlay.TeaseFaces.Remove(bubble); foreach (var frame in frames) frame.Dispose(); BubbleOverlay.Field.Bubbles.Remove(bubble); window.Close(); CoreSettings.Current.MotionLevel = old; }
        return Task.CompletedTask;
    });
    private static void Strip(string path)
    {
        using var bitmap = new SKBitmap(1, 2); bitmap.SetPixel(0, 0, SKColors.Red); bitmap.SetPixel(0, 1, SKColors.Blue);
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100); File.WriteAllBytes(path, png.ToArray());
    }
    private static (byte R, byte B) Pixel(Window window, double x = 0.25, double y = 0.25)
    {
        Dispatcher.UIThread.RunJobs(); using var bitmap = window.CaptureRenderedFrame()!;
        var pixel = Marshal.AllocHGlobal(4);
        try
        {
            bitmap.CopyPixels(new PixelRect((int)(bitmap.PixelSize.Width * x), (int)(bitmap.PixelSize.Height * y), 1, 1), pixel, 4, 4);
            var first = Marshal.ReadByte(pixel); var third = Marshal.ReadByte(pixel, 2);
            return bitmap.Format == global::Avalonia.Platform.PixelFormats.Rgba8888 ? (first, third) : (third, first);
        }
        finally { Marshal.FreeHGlobal(pixel); }
    }
}
