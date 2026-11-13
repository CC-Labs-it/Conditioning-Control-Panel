using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>An animated flash decodes under WPF's flash rules (Core FlashFrames), not the spiral's:
/// a 2048 px cap rather than 1280, at least 10 frames kept, 600 decoded, and the kept frames'
/// average delay (WPF AnimatedWebp.cs:89,182-188,242-244, FlashService.cs:1489-1494).</summary>
public sealed class FlashDecodeBudgetTests
{
    [Fact]
    public void A_big_clip_decodes_at_2048_and_keeps_ten_frames() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        // 3000x1000 at 2048 px: 5.3 MiB a frame, so 30 MB would hold 5 of 12; WPF keeps 10 (stride 2).
        var path = WriteGif(3000, 1000, 12, _ => 5, out var dir);
        var (frames, delay) = SpiralOverlay.Decode(path, 4000, flash: true);
        try
        {
            Assert.Equal(new PixelSize(2048, 683), frames[0].PixelSize);
            Assert.Equal(6, frames.Count);
            Assert.Equal(TimeSpan.FromMilliseconds(100), delay);
        }
        finally { foreach (var f in frames) f.Dispose(); Directory.Delete(dir, true); }
    });

    [Fact]
    public void A_long_clip_stops_at_600_and_plays_the_kept_frames_average_delay() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        // 700 frames: stride 12 over all 700, but only 0..588 are decoded -> 50 kept.
        // The kept frames alternate 20 and 80 ms: average 50 x 12 = 600 ms (the spiral rule says 20 x 12).
        var path = WriteGif(1, 1, 700, i => i % 24 == 0 ? 2 : 8, out var dir);
        var (frames, delay) = SpiralOverlay.Decode(path, 100, flash: true);
        try
        {
            Assert.Equal(50, frames.Count);
            Assert.Equal(TimeSpan.FromMilliseconds(600), delay);
        }
        finally { foreach (var f in frames) f.Dispose(); Directory.Delete(dir, true); }
    });

    /// <summary>A looping GIF with a <paramref name="width"/> x <paramref name="height"/> screen and
    /// <paramref name="count"/> one-pixel frames, frame i lasting <paramref name="delayCs"/>(i) centiseconds.</summary>
    private static string WriteGif(int width, int height, int count, Func<int, int> delayCs, out string dir)
    {
        var b = new List<byte>();
        b.AddRange("GIF89a"u8.ToArray());
        b.AddRange(new byte[] { (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0x80, 0, 0 });
        b.AddRange(new byte[] { 255, 0, 0, 0, 255, 0 });   // two-colour global table
        b.AddRange(new byte[] { 0x21, 0xFF, 0x0B });
        b.AddRange("NETSCAPE2.0"u8.ToArray());
        b.AddRange(new byte[] { 3, 1, 0, 0, 0 });
        for (var i = 0; i < count; i++)
        {
            var d = delayCs(i);
            b.AddRange(new byte[] { 0x21, 0xF9, 4, 0, (byte)d, (byte)(d >> 8), 0, 0 });
            b.AddRange(new byte[] { 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0 });
            b.AddRange(new byte[] { 2, 2, 0x44, 0x01, 0 });   // LZW: clear, pixel 0, end
        }
        b.Add(0x3B);
        dir = Directory.CreateTempSubdirectory("ccp-flash-decode-").FullName;
        var path = Path.Combine(dir, "clip.gif");
        File.WriteAllBytes(path, b.ToArray());
        return path;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
