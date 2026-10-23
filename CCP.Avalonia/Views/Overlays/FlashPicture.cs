using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF AnimatedWebp.DecodeFrames + FlashService.ScaleFrameDelay, adapted to Avalonia.
/// Decode on the worker; publish copied Bitmap frames, never a mutable SKBitmap.</summary>
internal sealed class FlashPicture : IDisposable
{
    internal readonly List<Bitmap> Frames;
    internal readonly TimeSpan Delay;
    internal FlashPicture(Bitmap still) : this(new List<Bitmap> { still }, TimeSpan.FromMilliseconds(100)) { }
    private FlashPicture(List<Bitmap> frames, TimeSpan delay) => (Frames, Delay) = (frames, delay);
    public void Dispose() { foreach (var frame in Frames) frame.Dispose(); }

    internal static TimeSpan ScaleDelay(TimeSpan source, double speed)
    {
        var ms = source.TotalMilliseconds;
        if (!double.IsFinite(ms) || ms <= 0) ms = 100;
        if (!double.IsFinite(speed) || speed <= 0) speed = 1;
        return TimeSpan.FromMilliseconds(Math.Max(10, ms / Math.Clamp(speed, 0.25, 4)));
    }

    internal static FlashPicture Decode(string path, int maxDim, double speed)
    {
        var frames = new List<Bitmap>();
        maxDim = Math.Clamp(maxDim, 1, 2048); // WPF ComputeDecodeMaxDim ceiling.
        try
        {
            using var codec = SKCodec.Create(path) ?? throw new InvalidOperationException("Unreadable flash image");
            var src = codec.Info;
            if (src.Width <= 0 || src.Height <= 0) throw new InvalidOperationException("Empty flash image");
            var scale = Math.Min(1, Math.Max(1, maxDim) / (double)Math.Max(src.Width, src.Height));
            var w = Math.Max(1, (int)Math.Round(src.Width * scale));
            var h = Math.Max(1, (int)Math.Round(src.Height * scale));
            var count = Math.Max(1, codec.FrameCount);
            if (count == 1)
            {
                using var file = File.OpenRead(path);
                return new FlashPicture(Bitmap.DecodeToWidth(file, w));
            }
            if ((long)src.Width * src.Height > 4096L * 4096) throw new InvalidOperationException("Flash animation exceeds decode budget");
            // WPF: 60 kept frames, 30 MiB (at least 10), 600 decoded frames, ceiling stride.
            var keep = Math.Min(60, Math.Max(10, (int)(30L * 1024 * 1024 / ((long)w * h * 4))));
            var step = Math.Max(1, (int)Math.Ceiling(count / (double)keep));
            var info = new SKImageInfo(src.Width, src.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKBitmap(info);
            using var scaled = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
            var fi = codec.FrameInfo;
            byte[]? restore = null;
            int restoreIndex = -1, samples = 0;
            long duration = 0;
            for (var i = 0; i < Math.Min(count, 600) && frames.Count < keep; i++)
            {
                var prior = -1;
                if (i > 0 && fi[i].RequiredFrame >= 0)
                {
                    if (fi[i - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious) prior = i - 1;
                    else if (restore != null && restoreIndex >= fi[i].RequiredFrame)
                    {
                        Marshal.Copy(restore, 0, canvas.GetPixels(), restore.Length);
                        prior = restoreIndex;
                    }
                }
                var result = codec.GetPixels(info, canvas.GetPixels(), new SKCodecOptions(i, prior));
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) break;
                if (i + 1 < count && fi[i].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious
                    && fi[i + 1].DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious)
                {
                    restore ??= new byte[canvas.ByteCount];
                    canvas.GetPixelSpan().CopyTo(restore);
                    restoreIndex = i;
                }
                if (i % step != 0) continue;
                canvas.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                frames.Add(new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(),
                    new PixelSize(w, h), new Vector(96, 96), scaled.RowBytes));
                if (fi.Length > i && fi[i].Duration > 0) { duration += fi[i].Duration; samples++; }
            }
            if (frames.Count == 0) throw new InvalidOperationException("No flash frames decoded");
            var avg = samples > 0 ? duration / (double)samples : 100;
            if (avg < 20) avg = 100;
            return new FlashPicture(frames, ScaleDelay(TimeSpan.FromMilliseconds(Math.Clamp(avg * step, 20, 2000)), speed));
        }
        catch
        {
            foreach (var frame in frames) frame.Dispose();
            // WPF LoadGifFrames: a single/failed animation falls back to a bounded still.
            using var file = File.OpenRead(path);
            return new FlashPicture(Bitmap.DecodeToWidth(file, Math.Max(1, maxDim)));
        }
    }
}
