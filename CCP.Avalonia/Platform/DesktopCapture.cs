using System;
using System.Runtime.InteropServices;
using Avalonia;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Platform;

// Native resources are created, used and disposed ONLY by the capture worker.
internal interface IDesktopCapture : IDisposable
{
    SKImage Capture(PixelRect bounds, int downscale, IntPtr[] excluded);
}

internal static class DesktopCapture
{
    // GDI BI_RGB and X11 depth-24 buffers leave X undefined. Materialize opaque alpha;
    // declaring BGRA Opaque alone is insufficient on Skia's pixel-copy/draw path.
    internal static SKImage OpaqueBgr(IntPtr pixels, int width, int height, int stride)
    {
        var bytes = new byte[checked(stride * height)];
        Marshal.Copy(pixels, bytes, 0, bytes.Length);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) bytes[y * stride + x * 4 + 3] = 255;
        return SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), bytes, stride);
    }
    // Backend identity, not DISPLAY: native Wayland must never attempt XWayland capture.
    internal static Func<IDesktopCapture>? Factory(OverlayBackend backend) => backend switch
    {
        OverlayBackend.X11 => () => new X11DesktopCapture(),
        OverlayBackend.Win32 => () => new GdiDesktopCapture(),
        _ => null,
    };
}
