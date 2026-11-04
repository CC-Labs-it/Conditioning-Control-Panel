using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF Services/Compositor/BrainDrainCapturePump.cs:73-112. The desktop blit can stall forever.
// The worker owns every GDI handle and shutdown never waits for it.
internal sealed class BrainDrainCapturePump
{
    internal sealed record Frame(int Width, int Height, byte[] Pixels);
    internal const uint DesktopCopy = 0x00CC0020; // SRCCOPY, without CAPTUREBLT.
    private static readonly Frame Stopped = new(0, 0, Array.Empty<byte>());
    private readonly Func<Frame?> _capture;
    private readonly TimeSpan _interval;
    private Frame? _frame;

    internal BrainDrainCapturePump(PixelRect bounds, TimeSpan interval)
        : this(() => CaptureDesktop(bounds), interval) { }

    internal BrainDrainCapturePump(Func<Frame?> capture, TimeSpan interval)
    {
        _capture = capture;
        _interval = interval > TimeSpan.Zero ? interval : TimeSpan.FromMilliseconds(33);
        new Thread(Loop) { IsBackground = true, Name = "BrainDrain-Capture" }.Start();
    }

    internal Frame? Take()
    {
        while (true)
        {
            var frame = Volatile.Read(ref _frame);
            if (frame == null || ReferenceEquals(frame, Stopped)) return null;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _frame, null, frame), frame)) return frame;
        }
    }

    internal void Shutdown() => Interlocked.Exchange(ref _frame, Stopped);

    private void Loop()
    {
        while (!ReferenceEquals(Volatile.Read(ref _frame), Stopped))
        {
            Frame? frame = null;
            try { frame = _capture(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Brain Drain capture failed"); }
            if (frame != null)
            {
                while (true)
                {
                    var old = Volatile.Read(ref _frame);
                    if (ReferenceEquals(old, Stopped)) return;
                    if (ReferenceEquals(Interlocked.CompareExchange(ref _frame, frame, old), old)) break;
                }
            }
            if (ReferenceEquals(Volatile.Read(ref _frame), Stopped)) return;
            Thread.Sleep(_interval);
        }
    }

    // WPF OverlayService.cs:2674-2726: shrink the physical monitor bounds while copying.
    // SRCCOPY without CAPTUREBLT excludes layered windows, including the overlay itself.
    private static Frame? CaptureDesktop(PixelRect bounds)
    {
        if (!OperatingSystem.IsWindows() || bounds.Width <= 0 || bounds.Height <= 0) return null;
        int width = Math.Max(2, bounds.Width / 4), height = Math.Max(2, bounds.Height / 4);
        var info = new BitmapInfo { Header = new BitmapHeader { Size = (uint)Marshal.SizeOf<BitmapHeader>(), Width = width,
            Height = -height, Planes = 1, BitCount = 32 } };
        IntPtr dc = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            memory = CreateCompatibleDC(dc);
            if (memory == IntPtr.Zero) return null;
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            previous = SelectObject(memory, bitmap);
            if (previous == IntPtr.Zero || previous == new IntPtr(-1)) { previous = IntPtr.Zero; return null; }
            SetStretchBltMode(memory, 3);
            if (!StretchBlt(memory, 0, 0, width, height, dc, bounds.X, bounds.Y, bounds.Width, bounds.Height, DesktopCopy)) return null;
            var pixels = new byte[checked(width * height * 4)];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // GDI's fourth byte is undefined. Frames are opaque BGRA.
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return new Frame(width, height, pixels);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Color; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dest, int x, int y, int width, int height, IntPtr src, int sx, int sy, int sw, int sh, uint rop);
}
