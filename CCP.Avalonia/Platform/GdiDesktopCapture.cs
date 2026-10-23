using System;
using System.Runtime.InteropServices;
using Avalonia;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Platform;

// Adapted from parity/trunk 11c20f4ef3d96b06386a69e655d4307406a5f7fe (GdiGrab).
// Compile-only on Linux. Affinity is required, independent of the screenshot preference:
// SRCCOPY alone is not a sufficient self-exclusion guarantee on every Windows compositor.
internal sealed class GdiDesktopCapture : IDesktopCapture
{
    public SKImage Capture(PixelRect b, int downscale, IntPtr[] excluded)
    {
        foreach (var hwnd in excluded)
            if (hwnd == IntPtr.Zero || !SetWindowDisplayAffinity(hwnd, 0x11))
                throw new InvalidOperationException("Windows refused capture exclusion");
        int w = Math.Max(1, b.Width / downscale), h = Math.Max(1, b.Height / downscale);
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) throw new InvalidOperationException("Windows returned no desktop DC");
        IntPtr mem = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            mem = CreateCompatibleDC(dc);
            if (mem == IntPtr.Zero) throw new InvalidOperationException("Windows returned no capture DC");
            var header = new Header { Size = Marshal.SizeOf<Header>(), Width = w, Height = -h, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(mem, ref header, 0, out var pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero) throw new InvalidOperationException("Windows returned no capture bitmap");
            previous = SelectObject(mem, bitmap);
            SetStretchBltMode(mem, 4);
            if (!StretchBlt(mem, 0, 0, w, h, dc, b.X, b.Y, b.Width, b.Height, 0x00CC0020))
                throw new InvalidOperationException("Windows desktop capture failed");
            GdiFlush();
            // BI_RGB's fourth byte is undefined. Force opaque while preserving BGR channel order.
            return DesktopCapture.OpaqueBgr(pixels, w, h, w * 4);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(mem, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (mem != IntPtr.Zero) DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, dc);
        }
    }
    public void Dispose() { } // per-frame handles, owned exclusively by the worker
    [StructLayout(LayoutKind.Sequential)] private struct Header
    {
        public int Size, Width, Height;
        public short Planes, Bits;
        public int Compression, ImageSize, XPels, YPels, Used, Important;
    }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int sw, int sh, int rop);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref Header header, uint usage, out IntPtr pixels, IntPtr section, uint offset);
}
