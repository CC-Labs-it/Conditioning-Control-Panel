using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    // WPF FlashService.cs:1399,1420: a bounded, composited GIF or WebP frame sequence.
    internal static class FlashAnimation
    {
        // WPF AnimatedWebp.cs:62 bounds concurrent full-size composition canvases.
        private static readonly SemaphoreSlim DecodeGate = new(2, 2);

        internal static (List<Bitmap> Frames, TimeSpan Delay) Decode(string path, int maxDimension, CancellationToken cancellation = default)
        {
            try { DecodeGate.Wait(cancellation); }
            catch (OperationCanceledException) { return (new List<Bitmap>(), TimeSpan.Zero); }
            try { return cancellation.IsCancellationRequested ? (new List<Bitmap>(), TimeSpan.Zero) : DecodeCore(path, maxDimension); }
            finally { DecodeGate.Release(); }
        }

        private static (List<Bitmap> Frames, TimeSpan Delay) DecodeCore(string path, int maxDimension)
        {
            var frames = new List<Bitmap>();
            try
            {
                using var codec = SKCodec.Create(path);
                if (codec == null || codec.FrameCount < 2) return (frames, TimeSpan.Zero);
                int width = codec.Info.Width, height = codec.Info.Height;
                if (width <= 0 || height <= 0 || (long)width * height > 4096L * 4096) return (frames, TimeSpan.Zero);
                double scale = Math.Min(1, Math.Max(1, maxDimension) / (double)Math.Max(width, height));
                int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));
                int count = Math.Min(codec.FrameCount, 600);
                int keep = (int)Math.Min(60, (30L * 1024 * 1024) / ((long)w * h * 4));
                if (keep < 2) return (frames, TimeSpan.Zero);
                int step = Math.Max(1, (int)Math.Ceiling(codec.FrameCount / (double)keep));
                var info = codec.FrameInfo;
                var full = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var canvas = new SKBitmap(full);
                using var scaled = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                byte[]? restore = null;
                int restoreIndex = -1, samples = 0;
                long total = 0;
                for (int i = 0; i < count; i++)
                {
                    int prior = -1;
                    if (i > 0 && info[i].RequiredFrame >= 0)
                    {
                        if (info[i - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious) prior = i - 1;
                        else if (restore != null && restoreIndex >= info[i].RequiredFrame)
                        {
                            Marshal.Copy(restore, 0, canvas.GetPixels(), restore.Length);
                            prior = restoreIndex;
                        }
                    }
                    var result = codec.GetPixels(full, canvas.GetPixels(), prior >= 0 ? new SKCodecOptions(i, prior) : new SKCodecOptions(i));
                    if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) break;
                    if (i + 1 < count && info[i].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious && info[i + 1].DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious)
                    {
                        restore ??= new byte[canvas.ByteCount];
                        canvas.GetPixelSpan().CopyTo(restore);
                        restoreIndex = i;
                    }
                    if (i % step != 0) continue;
                    canvas.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                    frames.Add(new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(), new PixelSize(w, h), new Vector(96, 96), scaled.RowBytes));
                    if (info[i].Duration > 0) { total += info[i].Duration; samples++; }
                }
                if (frames.Count >= 2)
                {
                    double average = samples > 0 ? total / (double)samples : 100;
                    if (average < 20) average = 100;
                    return (frames, TimeSpan.FromMilliseconds(Math.Clamp(average * step, 20, 2000)));
                }
            }
            catch (Exception) { /* Keep the already loaded still. */ }
            foreach (var frame in frames) frame.Dispose();
            frames.Clear();
            return (frames, TimeSpan.Zero);
        }

        internal static TimeSpan Delay(TimeSpan delay, double speed)
        {
            var ms = delay.TotalMilliseconds;
            if (!double.IsFinite(ms) || ms <= 0) ms = 100;
            if (!double.IsFinite(speed) || speed <= 0) speed = 1;
            return TimeSpan.FromMilliseconds(Math.Max(10, ms / Math.Clamp(speed, 0.25, 4)));
        }
    }
}
