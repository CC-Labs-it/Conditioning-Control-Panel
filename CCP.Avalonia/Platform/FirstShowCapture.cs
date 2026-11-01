using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Services/Compositor/BrainDrainCapturePump.cs:73-112, 377-420, 426-529. The GDI
// call and Skia work stay on this worker: a stalled desktop blit must never block the UI.
internal sealed class FirstShowCapture
{
    private readonly PixelRect _bounds;
    private readonly bool _melt;
    private volatile bool _stop;
    private byte[]? _pending;

    internal FirstShowCapture(PixelRect bounds, bool melt)
    {
        _bounds = bounds; _melt = melt;
        new Thread(Run) { IsBackground = true, Name = "FirstShow-Capture", Priority = ThreadPriority.BelowNormal }.Start();
    }
    internal byte[]? Take() => Interlocked.Exchange(ref _pending, null);
    internal void Stop() { _stop = true; Interlocked.Exchange(ref _pending, null); }

    // WPF BrainDrainCapturePump.cs:106-112: SRCCOPY skips layered windows. The existing
    // Win32Overlay seam keeps that style on the stage, including when Avalonia rebuilds styles.
    // Otherwise use the veil rather than capture the stage's own previous frame.
    internal static bool CanCapture(Window stage)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var handle = stage.TryGetPlatformHandle();
        return handle?.HandleDescriptor == "HWND" && handle.Handle != IntPtr.Zero && CanCaptureStyle(Win32Overlay.GetWindowLong(handle.Handle, -20));
    }
    internal static bool CanCaptureStyle(uint style) => (style & Win32Overlay.WsExLayered) != 0;

    private void Run()
    {
        IntPtr dc = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            int w = Math.Max(2, (_bounds.Width / 4) & ~1), h = Math.Max(2, (_bounds.Height / 4) & ~1);
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return;
            var info = new BitmapInfo { Size = Marshal.SizeOf<BitmapInfo>(), Width = w, Height = -h, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return;
            old = SelectObject(dc, bitmap);
            if (old == IntPtr.Zero || old == new IntPtr(-1)) { old = IntPtr.Zero; return; }
            SetStretchBltMode(dc, 4); // HALFTONE
            using var tile = _melt ? MakeNoise() : null;
            var clock = Stopwatch.StartNew();
            while (!_stop)
            {
                long start = clock.ElapsedMilliseconds;
                var screen = GetDC(IntPtr.Zero);
                if (screen != IntPtr.Zero)
                {
                    try
                    {
                        // Plain SRCCOPY excludes layered windows; CAPTUREBLT would feed overlays back.
                        if (StretchBlt(dc, 0, 0, w, h, screen, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, 0x00CC0020))
                        {
                            GdiFlush();
                            // The unused fourth GDI byte is not reliably opaque. Rgb888x ignores it.
                            using var raw = WrapPixels(w, h, bits);
                            if (raw != null)
                            {
                                var png = RenderFrame(raw, tile, (float)clock.Elapsed.TotalSeconds);
                                if (!_stop && png != null) Interlocked.Exchange(ref _pending, png);
                            }
                        }
                    }
                    finally { ReleaseDC(IntPtr.Zero, screen); }
                }
                int wait = (int)Math.Clamp(40 - (clock.ElapsedMilliseconds - start), 0, 40);
                if (wait > 0) Thread.Sleep(wait);
            }
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "First show capture unavailable"); }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(dc, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            Interlocked.Exchange(ref _pending, null);
        }
    }

    // WPF BrainDrainCapturePump.cs:116-148, 500-505: GDI leaves the fourth byte undefined.
    internal static SKImage? WrapPixels(int width, int height, IntPtr bits) =>
        SKImage.FromPixels(new SKImageInfo(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque), bits, width * 4);

    // WPF BrainDrainCapturePump.cs:228-238, 431-477, 510-527 and 656-676.
    internal static SKImage? MakeNoise()
    {
        using var shader = SKShader.CreatePerlinNoiseTurbulence(12f / 256, 12f / 256 * .65f, 2, 7, new SKSizeI(256, 256));
        using var surface = SKSurface.Create(new SKImageInfo(256, 256, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface == null) return null;
        using var paint = new SKPaint { Shader = shader };
        surface.Canvas.DrawRect(0, 0, 256, 256, paint);
        return surface.Snapshot();
    }
    internal static byte[]? RenderFrame(SKImage raw, SKImage? tile, float seconds)
    {
        int w = raw.Width, h = raw.Height;
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (surface == null) return null;
        using var swap = SKColorFilter.CreateColorMatrix(new float[] { 0,0,1,0,0, 0,1,0,0,0, 1,0,0,0,0, 0,0,0,1,0 });
        using var blur = SKImageFilter.CreateBlur(70 * .14f / 4 / 3, 70 * .14f / 4 / 3);
        float coverage = Math.Max(8f, Math.Min(w, h) * .20f) * 12;
        using var drift = tile == null ? null : SKShader.CreateImage(tile, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat,
            SKMatrix.CreateScaleTranslation(coverage / 256, coverage / 256, 2.5f * MathF.Sin(seconds * .55f), 6 * seconds % coverage));
        using var noise = drift == null ? null : SKImageFilter.CreateShader(drift);
        const float amplitude = 1 + 70 * .045f;
        using var melt = noise == null ? null : SKImageFilter.CreateDisplacementMapEffect(SKColorChannel.R, SKColorChannel.G, amplitude, noise, blur);
        using var paint = new SKPaint { ColorFilter = swap, ImageFilter = melt ?? blur, FilterQuality = SKFilterQuality.Low };
        if (melt != null)
        {
            float gutter = amplitude * .5f + 2;
            surface.Canvas.Translate(w * .5f, h * .5f);
            surface.Canvas.Scale((w + 2 * gutter) / w, (h + 2 * gutter) / h);
            surface.Canvas.Translate(-w * .5f, -h * .5f);
        }
        surface.Canvas.DrawImage(raw, 0, 0, paint);
        using var frame = surface.Snapshot();
        using var png = frame.Encode(SKEncodedImageFormat.Png, 100);
        return png?.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        internal int Size, Width, Height; internal short Planes, BitCount;
        internal int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
}
