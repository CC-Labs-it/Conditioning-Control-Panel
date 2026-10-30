using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;
using SkiaSharp;

namespace CCP.Avalonia.Tests;

// WPF Services/Media/AnimatedWebp.cs:178-249, called by BlinkTrainerService.cs:560.
public sealed class BlinkTrainerDecodeTests
{
    // Real lossless WebP red/blue subframes. Expand the canvas and repeat ANMF chunks to
    // exercise SKCodec's actual metadata and decode path without large binary fixtures.
    private const string Webp = "UklGRoQAAABXRUJQVlA4WAoAAAACAAAAAAAAAAAAQU5JTQYAAAAAAAAAAABBTk1GKAAAAAAAAAAAAAAAAAAAAGQAAAJWUDhMDwAAAC8AAAAABxD9j/4HIqL/AQBBTk1GKAAAAAAAAAAAAAAAAAAAAGQAAABWUDhMDwAAAC8AAAAABxDR//4HIqL/AQA=";

    private static byte[] Clip(int size, int[] delays)
    {
        var source = Convert.FromBase64String(Webp);
        using var body = new MemoryStream();
        using var writer = new BinaryWriter(body);
        writer.Write(Encoding.ASCII.GetBytes("WEBP"));
        void Chunk(string name, byte[] bytes)
        {
            writer.Write(Encoding.ASCII.GetBytes(name)); writer.Write(bytes.Length);
            writer.Write(bytes);
            if (bytes.Length % 2 != 0) writer.Write((byte)0);
        }
        void U24(byte[] bytes, int offset, int value)
        {
            for (int b = 0; b < 3; b++) bytes[offset + b] = (byte)(value >> (8 * b));
        }
        var canvas = source[20..30];
        U24(canvas, 4, size - 1); U24(canvas, 7, size - 1);
        Chunk("VP8X", canvas);
        Chunk("ANIM", source[38..44]);
        for (int i = 0; i < delays.Length; i++)
        {
            var frame = i % 2 == 0 ? source[52..92] : source[100..140];
            U24(frame, 12, delays[i]);
            Chunk("ANMF", frame);
        }
        using var result = new MemoryStream();
        using var riff = new BinaryWriter(result);
        riff.Write(Encoding.ASCII.GetBytes("RIFF")); riff.Write((int)body.Length);
        riff.Write(body.ToArray());
        return result.ToArray();
    }

    private static void Decode(int size, int[] delays, int expectedCount, int expectedDelay, int dim = 640) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            LabWebcamStatusTests.Setup();
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".webp");
            File.WriteAllBytes(path, Clip(size, delays));
            System.Collections.Generic.List<Bitmap>? frames = null;
            try
            {
                using (var codec = SKCodec.Create(path))
                {
                    Assert.NotNull(codec);
                    Assert.Equal(delays.Length, codec.FrameCount);
                    Assert.Equal(size, codec.Info.Width);
                }
                var decoded = BlinkTrainerSession.DecodeAnimation(path, dim);
                frames = decoded.Frames;
                Assert.Equal(expectedCount, frames.Count);
                Assert.Equal(expectedDelay, decoded.Delay.TotalMilliseconds);
                Assert.All(frames, f => Assert.Equal(Math.Min(size, dim), f.PixelSize.Width));
            }
            finally
            {
                if (frames != null) foreach (var frame in frames) frame.Dispose();
                File.Delete(path);
            }
        });

    [Theory]
    [InlineData(100, 300, 200)]
    [InlineData(1200, 1600, 1400)]
    [InlineData(2500, 3500, 2000)]
    [InlineData(10, 10, 100)]
    public void Timing_AveragesRetainedDelays_AndUsesWpfBounds(int first, int second, int expected) =>
        Decode(1, new[] { first, second }, 2, expected);

    [Fact]
    public void LargeWebp_Uses24MbBudget_AfterScaling_AndAveragesOnlyRetainedFrames()
    {
        // 60 * 640 * 640 * 4 = 93.75 MiB. WPF keeps 15, stride 4.
        var delays = Enumerable.Range(0, 60).Select(i => i % 4 == 0 ? 100 : 300).ToArray();
        Decode(640, delays, 15, 400);
        // Scaling to 320 first makes all 60 fit the same 24 MiB budget.
        Decode(640, delays, 60, 250, dim: 320);
    }

    [Fact]
    public void LongWebp_UsesCeilingStride_AndNeverDecodesPast600()
    {
        // Stride comes from the complete clip: ceil(1000 / 96) = 11.
        // Only indices 0..594 are kept before the 600-decode ceiling: 55 frames.
        Decode(1, Enumerable.Repeat(100, 1000).ToArray(), 55, 1100);
        // Ceiling division covers the tail rather than silently truncating at frame 95.
        Decode(1, Enumerable.Repeat(100, 100).ToArray(), 50, 200);
    }
}
