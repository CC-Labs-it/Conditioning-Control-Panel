using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Frames read back from a strip: one tightly packed W x H x 4 buffer per frame.</summary>
internal sealed record RainStripFrames(int Width, int Height, SKColorType ColorType, SKAlphaType AlphaType, IReadOnlyList<byte[]> Frames);

/// <summary>
/// Turns baked frames into a pack's clip strips (RAIN-V2-PLAN 2.3). The wardrobe streams
/// <c>rain-bake-frame {clip, index, png}</c>; frames are held per clip (as PNG or RGBA bytes,
/// never as bitmaps) and <see cref="FinishClip"/> writes one vertical PNG strip, frame 0 on top,
/// alpha kept (straight, unpremultiplied RGBA), then records it on the writer.
/// <see cref="ReadStrip"/> is the way back for the tube's pack player (lane R8).
/// </summary>
internal sealed class RainPackSink
{
    private readonly RainPackWriter _writer;
    private readonly Dictionary<string, SortedDictionary<int, (byte[] bytes, bool rgba)>> _pending = new(StringComparer.Ordinal);

    public RainPackSink(RainPackWriter writer, int fps, int width, int height)
    {
        if (fps <= 0 || width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(fps));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        Fps = fps;
        Width = width;
        Height = height;
    }

    public int Fps { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>One encoded PNG frame (any colour type; it must be W x H).</summary>
    public void AddFrame(string clip, int index, byte[] png) => Add(clip, index, png, rgba: false);

    /// <summary>One raw frame: W x H x 4 bytes of straight-alpha RGBA.</summary>
    public void AddRgbaFrame(string clip, int index, byte[] rgba)
    {
        if (rgba == null || rgba.Length != Width * Height * 4) throw new ArgumentException("RGBA frame has the wrong size.", nameof(rgba));
        Add(clip, index, rgba, rgba: true);
    }

    private void Add(string clip, int index, byte[] bytes, bool rgba)
    {
        if (!RainPackStore.IsValidClipName(clip)) throw new ArgumentException("Bad clip name.", nameof(clip));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (!_pending.TryGetValue(clip, out var frames)) _pending[clip] = frames = new SortedDictionary<int, (byte[], bool)>();
        frames[index] = (bytes ?? throw new ArgumentNullException(nameof(bytes)), rgba);
    }

    /// <summary>Clips with frames still waiting for <see cref="FinishClip"/>.</summary>
    public IReadOnlyCollection<string> PendingClips => _pending.Keys;

    /// <summary>Writes the clip's strip into the bake folder and records it. Frames must run 0..n-1.</summary>
    public void FinishClip(string clip)
    {
        if (!_pending.TryGetValue(clip, out var frames) || frames.Count == 0)
            throw new InvalidOperationException($"No frames for {clip}.");
        if (frames.Keys.Last() != frames.Count - 1)
            throw new InvalidOperationException($"Clip {clip} is missing frames.");
        WriteStrip(_writer.StripPath(clip), Width, Height, frames.Values.ToList());
        _writer.AddClip(clip, frames.Count, Fps, Width, Height);
        _pending.Remove(clip);
    }

    public void FinishAll()
    {
        foreach (var clip in _pending.Keys.ToList()) FinishClip(clip);
    }

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
        var info = new SKImageInfo(width, total, colorType, alphaType);
        // Row by row straight into each frame (a 384x618 x 90 strip is ~85 MB decoded): no second
        // whole-strip buffer. A codec without scanline support takes the one-buffer path below.
        if (codec.StartScanlineDecode(info) == SKCodecResult.Success)
        {
            var rows = new List<byte[]>(frames);
            for (int i = 0; i < frames; i++)
            {
                var f = new byte[frameBytes];
                var h = GCHandle.Alloc(f, GCHandleType.Pinned);
                int got;
                try { got = codec.GetScanlines(h.AddrOfPinnedObject(), height, width * 4); }
                finally { h.Free(); }
                if (got != height) throw new InvalidDataException($"Strip frame {i} did not decode ({got}/{height} rows).");
                rows.Add(f);
            }
            return new RainStripFrames(width, height, colorType, alphaType, rows);
        }
        var buffer = new byte[(long)frameBytes * frames];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var result = codec.GetPixels(info, handle.AddrOfPinnedObject());
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
