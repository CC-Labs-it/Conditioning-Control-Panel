using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Rebuild the desktop from XComposite window pixmaps, bottom to top, omitting our
/// overlays. A root screenshot includes the compositor's output and would recursively blur us.
/// Requires a fully covered desktop (wallpaper pixmap or opaque windows); gaps fail honestly.</summary>
internal sealed class X11DesktopCapture : IDesktopCapture
{
    private readonly IntPtr _display, _root;
    private static readonly ConcurrentDictionary<IntPtr, bool> Errors = new();
    internal static void OnError(IntPtr display) { if (Errors.ContainsKey(display)) Errors[display] = true; }

    internal X11DesktopCapture()
    {
        if (IntPtr.Size != 8) throw new InvalidOperationException("X11 capture requires a 64-bit ABI");
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero) throw new InvalidOperationException("No X11 display");
        Errors[_display] = false;
        _root = XDefaultRootWindow(_display);
        try
        {
            var visual = XDefaultVisual(_display, XDefaultScreen(_display));
            if (Marshal.ReadInt32(visual, 16) != 4 || Marshal.ReadInt64(visual, 24) != 0xff0000 ||
                Marshal.ReadInt64(visual, 32) != 0xff00 || Marshal.ReadInt64(visual, 40) != 0xff)
                throw new InvalidOperationException("Unsupported X11 visual");
            if (XCompositeQueryExtension(_display, out _, out _) == 0)
                throw new InvalidOperationException("XComposite is unavailable");
            // Automatic redirection coexists with a compositor's manual redirection. It also makes
            // pixmaps available on the isolated, uncomposited X11 test server. No windows are hidden.
            XCompositeRedirectSubwindows(_display, _root, 0);
            XSync(_display, false);
            Check();
        }
        catch { Dispose(); throw; }
    }

    public SKImage Capture(PixelRect bounds, int downscale, IntPtr[] excluded)
    {
        Errors[_display] = false;
        using var surface = SKSurface.Create(new SKImageInfo(Math.Max(1, bounds.Width / downscale),
            Math.Max(1, bounds.Height / downscale), SKColorType.Bgra8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale(1f / downscale);
        surface.Canvas.Translate(-bounds.X, -bounds.Y);
        using var missing = new SKRegion(new SKRectI(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
        var wallpaper = Property(_root, "_XROOTPMAP_ID");
        if (wallpaper is { } wall && wall != IntPtr.Zero)
        {
            using var image = Read(wall, out var width, out var height, out _);
            surface.Canvas.DrawImage(image, 0, 0);
            missing.Op(new SKRectI(0, 0, width, height), SKRegionOperation.Difference);
        }
        if (XQueryTree(_display, _root, out _, out _, out var children, out var count) == 0)
            throw new InvalidOperationException("X11 window enumeration failed");
        var attrs = Marshal.AllocHGlobal(136); // XWindowAttributes LP64; backend refuses other ABIs.
        try
        {
            for (var i = 0; i < count; i++)
            {
                var window = Marshal.ReadIntPtr(children, i * IntPtr.Size);
                if (Array.IndexOf(excluded, window) >= 0) continue;
                if (XGetWindowAttributes(_display, window, attrs) == 0) continue;
                if (Marshal.ReadInt32(attrs, 92) != 2 || Marshal.ReadInt32(attrs, 40) == 2) continue; // viewable, InputOutput
                int x = Marshal.ReadInt32(attrs), y = Marshal.ReadInt32(attrs, 4);
                int w = Marshal.ReadInt32(attrs, 8), h = Marshal.ReadInt32(attrs, 12);
                if (!bounds.Intersects(new PixelRect(x, y, w, h))) continue;
                // Shaped frames need a shape-aware compositor. Never claim a rectangular reconstruction is correct.
                XShapeQueryExtents(_display, window, out var shaped, out _, out _, out _, out _, out _, out _, out _, out _, out _);
                if (shaped != 0) throw new InvalidOperationException("Shaped X11 windows need capture support");
                var pixmap = XCompositeNameWindowPixmap(_display, window);
                try
                {
                    using var image = Read(pixmap, out w, out h, out var depth);
                    var opacity = Property(window, "_NET_WM_WINDOW_OPACITY");
                    using var paint = new SKPaint { Color = SKColors.White.WithAlpha(opacity == null ? (byte)255 : (byte)((ulong)opacity.Value >> 24)) };
                    surface.Canvas.DrawImage(image, x, y, paint);
                    using var pixels = image.PeekPixels();
                    if ((depth == 24 || pixels.ComputeIsOpaque()) && paint.Color.Alpha == 255)
                        missing.Op(new SKRectI(x, y, x + w, y + h), SKRegionOperation.Difference);
                }
                finally { if (pixmap != IntPtr.Zero) XFreePixmap(_display, pixmap); }
            }
            XSync(_display, false);
            Check();
            if (!missing.IsEmpty) throw new InvalidOperationException("X11 desktop background is not capturable");
            return surface.Snapshot();
        }
        finally { Marshal.FreeHGlobal(attrs); if (children != IntPtr.Zero) XFree(children); }
    }

    private void Check() { if (Errors[_display]) throw new InvalidOperationException("X11 capture changed or was refused"); }
    private IntPtr? Property(IntPtr window, string name)
    {
        XGetWindowProperty(_display, window, XInternAtom(_display, name, false), 0, 1, false, IntPtr.Zero,
            out _, out var format, out var count, out _, out var data);
        try { return format == 32 && count != 0 && data != IntPtr.Zero ? Marshal.ReadIntPtr(data) : null; }
        finally { if (data != IntPtr.Zero) XFree(data); }
    }
    private SKImage Read(IntPtr drawable, out int w, out int h, out uint depth)
    {
        if (IntPtr.Size != 8 || XGetGeometry(_display, drawable, out _, out _, out _, out var width, out var height, out _, out depth) == 0)
            throw new InvalidOperationException("Unsupported X11 drawable");
        w = checked((int)width); h = checked((int)height);
        var ptr = XGetImage(_display, drawable, 0, 0, width, height, nuint.MaxValue, 2);
        if (ptr == IntPtr.Zero) throw new InvalidOperationException("X11 returned no pixels");
        try
        {
            // Pixmap XImages have no visual: their masks are zero. Validate the display visual
            // above, then the actual storage here (XImage LP64 offsets).
            if (depth is not (24 or 32) || Marshal.ReadInt32(ptr, 48) != 32 || Marshal.ReadInt32(ptr, 24) != 0)
                throw new InvalidOperationException("Unsupported X11 pixel format");
            var data = Marshal.ReadIntPtr(ptr, 16); int stride = Marshal.ReadInt32(ptr, 44);
            return depth == 24 ? DesktopCapture.OpaqueBgr(data, w, h, stride)
                : SKImage.FromPixelCopy(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul), data, stride);
        }
        finally { XDestroyImage(ptr); }
    }
    public void Dispose()
    {
        if (!Errors.ContainsKey(_display)) return;
        XCompositeUnredirectSubwindows(_display, _root, 0);
        XCloseDisplay(_display);
        Errors.TryRemove(_display, out _);
    }
    private const string X = "libX11.so.6", C = "libXcomposite.so.1";
    [DllImport(X)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X)] private static extern int XCloseDisplay(IntPtr d);
    [DllImport(X)] private static extern int XDefaultScreen(IntPtr d);
    [DllImport(X)] private static extern IntPtr XDefaultVisual(IntPtr d, int screen);
    [DllImport(X)] private static extern IntPtr XDefaultRootWindow(IntPtr d);
    [DllImport(X)] private static extern int XSync(IntPtr d, bool discard);
    [DllImport(X)] private static extern int XQueryTree(IntPtr d, IntPtr w, out IntPtr root, out IntPtr parent, out IntPtr children, out int count);
    [DllImport(X)] private static extern int XGetWindowAttributes(IntPtr d, IntPtr w, IntPtr attrs);
    [DllImport(X)] private static extern int XGetGeometry(IntPtr d, IntPtr w, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
    [DllImport(X)] private static extern IntPtr XGetImage(IntPtr d, IntPtr w, int x, int y, uint width, uint height, nuint mask, int format);
    [DllImport(X)] private static extern int XDestroyImage(IntPtr image);
    [DllImport(X)] private static extern int XFree(IntPtr data);
    [DllImport(X)] private static extern int XFreePixmap(IntPtr d, IntPtr pixmap);
    [DllImport(X)] private static extern IntPtr XInternAtom(IntPtr d, string name, bool only);
    [DllImport(X)] private static extern int XGetWindowProperty(IntPtr d, IntPtr w, IntPtr property, nint offset, nint length, bool delete, IntPtr type, out IntPtr actual, out int format, out nuint count, out nuint remaining, out IntPtr data);
    [DllImport(C)] private static extern int XCompositeQueryExtension(IntPtr d, out int e, out int error);
    [DllImport(C)] private static extern void XCompositeRedirectSubwindows(IntPtr d, IntPtr w, int update);
    [DllImport(C)] private static extern void XCompositeUnredirectSubwindows(IntPtr d, IntPtr w, int update);
    [DllImport(C)] private static extern IntPtr XCompositeNameWindowPixmap(IntPtr d, IntPtr w);
    [DllImport("libXext.so.6")] private static extern int XShapeQueryExtents(IntPtr d, IntPtr w, out int shaped, out int x, out int y, out uint width, out uint height, out int clip, out int cx, out int cy, out uint cw, out uint ch);
}
