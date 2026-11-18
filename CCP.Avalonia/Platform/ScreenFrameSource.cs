using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.ScreenCapture;

namespace ConditioningControlPanel.Avalonia.Platform;

// Created only when an OCR engine is available and the user enables scanning. Native Wayland must
// not fall back to X11: the XWayland root contains only XWayland windows.
internal static class ScreenFrameSourceFactory
{
    internal static IScreenFrameSource Create()
    {
        if (SandboxNet.Active) return new UnavailableScreenFrames();
        if (OperatingSystem.IsWindows()) return new DesktopScreenFrames(Win32ScreenCapture.Read);
        if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            return new DesktopScreenFrames(X11ScreenCapture.Read);
        return new UnavailableScreenFrames(); // Native Wayland portal capture remains to be implemented.
    }
}

internal sealed class UnavailableScreenFrames : IScreenFrameSource
{
    public Task<ScreenFrameResult> CaptureAsync(CancellationToken cancellationToken) => Task.FromResult(ScreenFrameResult.Unavailable);
}

internal readonly record struct CaptureMonitor(string Name, Rectangle Bounds, double Scale);

// Screen enumeration is UI-thread-owned; pixel reading can run off-thread. The injected reader also
// allows tests to exercise negative origins and scale without touching the user's desktop.
internal sealed class DesktopScreenFrames : IScreenFrameSource
{
    private readonly Func<CaptureMonitor, byte[]?> _read;
    private readonly Func<Task<IReadOnlyList<CaptureMonitor>>> _monitors;
    internal DesktopScreenFrames(Func<CaptureMonitor, byte[]?> read, Func<Task<IReadOnlyList<CaptureMonitor>>>? monitors = null)
    {
        _read = read;
        _monitors = monitors ?? (() => Dispatcher.UIThread.InvokeAsync<IReadOnlyList<CaptureMonitor>>(() =>
            WebcamScreens.Enumerate().Select(s => new CaptureMonitor(WebcamScreens.Name(s),
                new Rectangle(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Scaling)).ToArray()).GetTask());
    }

    public async Task<ScreenFrameResult> CaptureAsync(CancellationToken cancellationToken)
    {
        try
        {
            var monitors = await _monitors().ConfigureAwait(false);
            if (monitors.Count == 0) return ScreenFrameResult.Unavailable;
            var frames = new List<ScreenFrame>(monitors.Count);
            foreach (var monitor in monitors)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bounds = monitor.Bounds;
                if (bounds.Width <= 0 || bounds.Height <= 0) return ScreenFrameResult.Unavailable;
                var pixels = _read(monitor);
                if (pixels == null || pixels.LongLength != (long)bounds.Width * bounds.Height * 4) return ScreenFrameResult.Unavailable;
                frames.Add(new ScreenFrame(monitor.Name, bounds, monitor.Scale, bounds.Width, bounds.Height,
                    checked(bounds.Width * 4), ScreenPixelFormat.Bgra32, pixels));
            }
            return new ScreenFrameResult(ScreenFrameStatus.Available, frames);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Screen capture unavailable"); return ScreenFrameResult.Unavailable; }
    }
}

internal static class X11ScreenCapture
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        internal int Width, Height, XOffset, Format;
        internal IntPtr Data;
        internal int ByteOrder, BitmapUnit, BitmapBitOrder, BitmapPad, Depth, BytesPerLine, BitsPerPixel;
        internal nuint RedMask, GreenMask, BlueMask;
    }
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XDefaultScreen(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XDisplayWidth(IntPtr display, int screen);
    [DllImport("libX11.so.6")] private static extern int XDisplayHeight(IntPtr display, int screen);
    [DllImport("libX11.so.6")] private static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, nuint mask, int format);
    [DllImport("libX11.so.6")] private static extern int XDestroyImage(IntPtr image);

    internal static byte[]? Read(CaptureMonitor monitor)
    {
        IntPtr display = IntPtr.Zero, image = IntPtr.Zero;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) return null;
            var b = monitor.Bounds;
            var screen = XDefaultScreen(display);
            if (b.X < 0 || b.Y < 0 || (long)b.Right > XDisplayWidth(display, screen) || (long)b.Bottom > XDisplayHeight(display, screen)) return null;
            image = XGetImage(display, XDefaultRootWindow(display), b.X, b.Y, (uint)b.Width, (uint)b.Height, nuint.MaxValue, 2);
            if (image == IntPtr.Zero) return null;
            var x = Marshal.PtrToStructure<XImage>(image);
            // Only the common 32-bit little-endian TrueColor layout is accepted, never mislabel another pixel format.
            if (x.Data == IntPtr.Zero || x.Width != b.Width || x.Height != b.Height || x.BitsPerPixel != 32 ||
                x.ByteOrder != 0 || x.RedMask != 0xff0000 || x.GreenMask != 0xff00 || x.BlueMask != 0xff || x.BytesPerLine < b.Width * 4) return null;
            var bytes = new byte[checked(b.Width * b.Height * 4)];
            for (int y = 0; y < b.Height; y++) Marshal.Copy(IntPtr.Add(x.Data, y * x.BytesPerLine), bytes, y * b.Width * 4, b.Width * 4);
            for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
            return bytes;
        }
        finally { if (image != IntPtr.Zero) XDestroyImage(image); if (display != IntPtr.Zero) XCloseDisplay(display); }
    }
}

internal static class Win32ScreenCapture
{
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
        internal uint Compression, SizeImage; internal int XPelsPerMeter, YPelsPerMeter; internal uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { internal BitmapInfoHeader Header; internal uint Colors; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    internal static byte[]? Read(CaptureMonitor monitor)
    {
        IntPtr dc = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            var b = monitor.Bounds;
            dc = GetDC(IntPtr.Zero); if (dc == IntPtr.Zero) return null;
            memory = CreateCompatibleDC(dc); if (memory == IntPtr.Zero) return null;
            var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = b.Width, Height = -b.Height, Planes = 1, BitCount = 32 } };
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            old = SelectObject(memory, bitmap); if (old == IntPtr.Zero || old == new IntPtr(-1)) { old = IntPtr.Zero; return null; }
            if (!BitBlt(memory, 0, 0, b.Width, b.Height, dc, b.X, b.Y, 0x40CC0020)) return null; // SRCCOPY | CAPTUREBLT
            var pixels = new byte[checked(b.Width * b.Height * 4)]; Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return pixels;
        }
        finally
        {
            if (old != IntPtr.Zero) SelectObject(memory, old);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
        }
    }
}
