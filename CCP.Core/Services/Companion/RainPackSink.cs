using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Companion.Rain;

/// <summary>Frames read back from a strip: one tightly packed W x H x 4 buffer per frame.</summary>
internal sealed record RainStripFrames(int Width, int Height, SKColorType ColorType, SKAlphaType AlphaType, IReadOnlyList<byte[]> Frames);

/// <summary>
/// A baked pack's clip strips (RAIN-V2-PLAN 2.3): one vertical PNG per clip, frame 0 on top,
/// alpha kept (straight, unpremultiplied RGBA). <see cref="ReadStrip"/> is the way back for the
/// tube's pack player (lane R8).
/// </summary>
internal static class RainPackSink
{
    /// <summary>Writes PNG frames (each W x H) as one vertical strip.</summary>
    public static void WriteStrip(string path, int width, int height, IReadOnlyList<byte[]> pngFrames) =>
        WriteStrip(path, width, height, pngFrames.Select(b => (b, false)).ToList());

    /// <summary>Writes straight-alpha RGBA frames (each W x H x 4) as one vertical strip.</summary>
    public static void WriteRgbaStrip(string path, int width, int height, IReadOnlyList<byte[]> rgbaFrames) =>
        WriteStrip(path, width, height, rgbaFrames.Select(b => (b, true)).ToList());

    private static void WriteStrip(string path, int width, int height, IReadOnlyList<(byte[] bytes, bool rgba)> frames)
    {
        int frameBytes = width * height * 4;
        var buffer = new byte[(long)frameBytes * frames.Count];
        var frameInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            for (int i = 0; i < frames.Count; i++)
            {
                var (bytes, rgba) = frames[i];
                if (rgba)
                {
                    if (bytes.Length != frameBytes) throw new ArgumentException($"Frame {i} has the wrong size.");
                    Buffer.BlockCopy(bytes, 0, buffer, i * frameBytes, frameBytes);
                    continue;
                }
                using var codec = SKCodec.Create(new SKMemoryStream(bytes))
                                  ?? throw new ArgumentException($"Frame {i} is not an image.");
                if (codec.Info.Width != width || codec.Info.Height != height)
                    throw new ArgumentException($"Frame {i} is {codec.Info.Width}x{codec.Info.Height}, expected {width}x{height}.");
                var result = codec.GetPixels(frameInfo, handle.AddrOfPinnedObject() + i * frameBytes);
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                    throw new ArgumentException($"Frame {i} did not decode ({result}).");
            }
            using var strip = new SKBitmap();
            strip.InstallPixels(new SKImageInfo(width, height * frames.Count, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                handle.AddrOfPinnedObject(), width * 4);
            using var data = strip.Encode(SKEncodedImageFormat.Png, 100)
                             ?? throw new InvalidOperationException("PNG encode failed.");
            using var file = File.Create(path);
            data.SaveTo(file);
        }
        finally { handle.Free(); }
    }

    /// <summary>
    /// Reads a strip back as <paramref name="frames"/> frames. Default: straight-alpha RGBA; the WPF
    /// player can ask for <see cref="SKColorType.Bgra8888"/> + <see cref="SKAlphaType.Premul"/> (Pbgra32).
    /// </summary>
    public static RainStripFrames ReadStrip(string path, int frames,
        SKColorType colorType = SKColorType.Rgba8888, SKAlphaType alphaType = SKAlphaType.Unpremul)
    {
        if (frames <= 0) throw new ArgumentOutOfRangeException(nameof(frames));
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidDataException("Not an image.");
        int width = codec.Info.Width, total = codec.Info.Height;
        if (total % frames != 0) throw new InvalidDataException($"Strip height {total} does not split into {frames} frames.");
        int height = total / frames, frameBytes = width * height * 4;
        var buffer = new byte[(long)frameBytes * frames];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var result = codec.GetPixels(new SKImageInfo(width, total, colorType, alphaType), handle.AddrOfPinnedObject());
            if (result != SKCodecResult.Success) throw new InvalidDataException($"Strip did not decode ({result}).");
        }
        finally { handle.Free(); }
        var list = new List<byte[]>(frames);
        for (int i = 0; i < frames; i++)
        {
            var f = new byte[frameBytes];
            Buffer.BlockCopy(buffer, i * frameBytes, f, 0, frameBytes);
            list.Add(f);
        }
        return new RainStripFrames(width, height, colorType, alphaType, list);
    }
}
