using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.ScreenCapture;
using SkiaSharp;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Platform;

// Native Wayland must not fall back to X11: the XWayland root contains only XWayland windows.
// Flatpak reaches the Screenshot portal by default, so it gets the portal source like any Wayland session.
internal static class ScreenFrameSourceFactory
{
    internal static IScreenFrameSource Create()
    {
        if (OperatingSystem.IsWindows()) return new DesktopScreenFrames(Win32ScreenCapture.Read);
        if (!OperatingSystem.IsLinux()) return new UnavailableScreenFrames();
        // The app always runs through XWayland, and on KWin 6 (live 2026-11-15) an XWayland root grab is all
        // black over native windows, so a Wayland session reads through the Screenshot portal instead.
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
               Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland"
            ? new PortalScreenFrames()
            : new DesktopScreenFrames(X11ScreenCapture.Read);
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
        _monitors = monitors ?? AvaloniaMonitors;
    }

    internal static Task<IReadOnlyList<CaptureMonitor>> AvaloniaMonitors() =>
        Dispatcher.UIThread.InvokeAsync<IReadOnlyList<CaptureMonitor>>(() =>
            WebcamScreens.Enumerate().Select(s => new CaptureMonitor(WebcamScreens.Name(s),
                new Rectangle(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height), s.Scaling)).ToArray()).GetTask();

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

// org.freedesktop.portal.Screenshot, non-interactive (spec: interactive=false is a hint; the frontend checks the
// "screenshot" permission and may ask once: https://github.com/flatpak/xdg-desktop-portal/blob/main/data/org.freedesktop.portal.Screenshot.xml).
// KDE answers without a dialog (https://github.com/KDE/xdg-desktop-portal-kde/blob/master/src/screenshot.cpp).
// The portal writes a file of the user's whole screen: decode it, delete it at once, never log where it was.
internal sealed class PortalScreenFrames : IScreenFrameSource
{
    internal delegate Task<(uint Code, Dictionary<string, VariantValue> Results)> Requester(CancellationToken cancellation);

    // Response 1 (the user cancelled) ends capture for the session: a scan loop must never re-prompt.
    internal static volatile bool Refused;
    // Response 2 (other) is a generic failure, like a failed WPF capture (ScreenOcrService.cs:215-221) it costs scans,
    // not the session. A denied permission also answers 2, but it is stored, so a retry answers 2 again without a dialog.
    private readonly TimeSpan _retryAfter;
    private long _retryAt;

    private readonly Requester _request;
    private readonly Func<Task<IReadOnlyList<CaptureMonitor>>> _monitors;
    private readonly TimeSpan _timeout;
    private readonly object _gate = new();
    private Task<(int Width, int Height, byte[] Pixels)?>? _pending;
    private CancellationToken _pendingStop;

    internal PortalScreenFrames(Requester? request = null, Func<Task<IReadOnlyList<CaptureMonitor>>>? monitors = null, TimeSpan? timeout = null, TimeSpan? retryAfter = null)
    {
        _retryAfter = retryAfter ?? TimeSpan.FromMinutes(1);
        _request = request ?? ScreenshotAsync;
        _monitors = monitors ?? DesktopScreenFrames.AvaloniaMonitors;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<ScreenFrameResult> CaptureAsync(CancellationToken cancellationToken)
    {
        if (Refused || Environment.TickCount64 < Interlocked.Read(ref _retryAt)) return ScreenFrameResult.Unavailable;
        Task<(int Width, int Height, byte[] Pixels)?> call;
        lock (_gate)
        {
            // A live call (even past an earlier scan's timeout) skips this scan rather than stacking requests. Once its
            // scan stopped or its 2-minute bound passed it is only observed for cleanup (Close may have failed), so it no longer blocks.
            if (_pending is { IsCompleted: false } && !_pendingStop.IsCancellationRequested) return ScreenFrameResult.Unavailable;
            var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bound.CancelAfter(TimeSpan.FromMinutes(2));
            _pendingStop = bound.Token;
            call = _pending = ShootAsync(bound);
        }
        try
        {
            var image = await call.WaitAsync(_timeout, cancellationToken).ConfigureAwait(false);
            if (image is not { } shot) return ScreenFrameResult.Unavailable;
            return Split(shot.Width, shot.Height, shot.Pixels, await _monitors().ConfigureAwait(false));
        }
        catch (TimeoutException) { return ScreenFrameResult.Unavailable; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Screen capture unavailable"); return ScreenFrameResult.Unavailable; }
    }

    // Stopping the scan closes the portal request (WPF captures only inside the active scan, ScreenOcrService.cs:128,212);
    // the per-scan timeout does not, and any answer that still arrives has its file deleted here.
    private async Task<(int, int, byte[])?> ShootAsync(CancellationTokenSource bound)
    {
        using var _ = bound;
        try
        {
            var (code, results) = await _request(bound.Token).ConfigureAwait(false);
            var path = FilePath(results);
            try
            {
                if (code == 1)
                {
                    Refused = true;
                    Serilog.Log.Warning("Screen OCR: the screenshot portal request was cancelled; screen capture stays off until restart");
                    return null;
                }
                if (code != 0)
                {
                    Interlocked.Exchange(ref _retryAt, Environment.TickCount64 + (long)_retryAfter.TotalMilliseconds);
                    Serilog.Log.Debug("Screen OCR: the screenshot portal answered {Code}; retrying after {Delay}", code, _retryAfter);
                    return null;
                }
                return path == null ? null : Decode(path);
            }
            finally { if (path != null) try { File.Delete(path); } catch { } }
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Serilog.Log.Debug("Screen OCR: screenshot portal call failed ({Type})", ex.GetType().Name);
            Interlocked.Exchange(ref _bus, null)?.Dispose();
            return null;
        }
    }

    internal static string? FilePath(Dictionary<string, VariantValue> results)
    {
        if (!results.TryGetValue("uri", out var v)) return null;
        if (v.Type == VariantValueType.Variant) v = v.GetVariantValue();
        return v.Type == VariantValueType.String && Uri.TryCreate(v.GetString(), UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : null;
    }

    // SKCodec.GetPixels(SKImageInfo, out byte[]) has the same body in SkiaSharp 2.88.8 and 4.156.1 (ilspy, 2026-11-15).
    private static (int, int, byte[])? Decode(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return codec.GetPixels(info, out byte[] pixels) == SKCodecResult.Success ? (info.Width, info.Height, pixels) : null;
    }

    // The shot covers the union of the monitors; crop each by its physical bounds, scaled if the image size differs.
    internal static ScreenFrameResult Split(int width, int height, byte[] pixels, IReadOnlyList<CaptureMonitor> monitors)
    {
        if (monitors.Count == 0 || pixels.LongLength != (long)width * height * 4) return ScreenFrameResult.Unavailable;
        int left = monitors.Min(m => m.Bounds.Left), top = monitors.Min(m => m.Bounds.Top);
        int unionW = monitors.Max(m => m.Bounds.Right) - left, unionH = monitors.Max(m => m.Bounds.Bottom) - top;
        if (unionW <= 0 || unionH <= 0) return ScreenFrameResult.Unavailable;
        double sx = (double)width / unionW, sy = (double)height / unionH;
        var frames = new List<ScreenFrame>(monitors.Count);
        foreach (var m in monitors)
        {
            var b = m.Bounds;
            int x0 = (int)Math.Round((b.Left - left) * sx), x1 = Math.Min(width, (int)Math.Round((b.Right - left) * sx));
            int y0 = (int)Math.Round((b.Top - top) * sy), y1 = Math.Min(height, (int)Math.Round((b.Bottom - top) * sy));
            int w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0) return ScreenFrameResult.Unavailable;
            var crop = new byte[checked(w * h * 4)];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(pixels, ((y0 + y) * width + x0) * 4, crop, y * w * 4, w * 4);
            frames.Add(new ScreenFrame(m.Name, b, m.Scale, w, h, w * 4, ScreenPixelFormat.Bgra32, crop));
        }
        return new ScreenFrameResult(ScreenFrameStatus.Available, frames);
    }

    // Own connection (UniqueName throws on the shared autoconnect one); one call at a time, so lazy init is safe.
    private static DBusConnection? _bus;
    // Waits for the Response however late (the shared 30 s helper timeout lost late files); after the 2-minute
    // bound in CaptureAsync the request is closed instead, so no Response and no file can follow.
    private static async Task<(uint, Dictionary<string, VariantValue>)> ScreenshotAsync(CancellationToken cancellation)
    {
        var bus = _bus;
        if (bus == null)
        {
            bus = new DBusConnection(DBusAddress.Session!);
            await bus.ConnectAsync().ConfigureAwait(false);
            await PortalPanicShortcut.RegisterAsync(bus).ConfigureAwait(false);
            _bus = bus;
        }
        return await PortalPanicShortcut.RequestAsync(bus, "Screenshot", "sa{sv}", (ref MessageWriter w, string token) =>
        {
            w.WriteString(""); // parent_window: none
            w.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token, ["interactive"] = false });
        }, cancellation, "org.freedesktop.portal.Screenshot", untilAnswered: true).ConfigureAwait(false);
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
