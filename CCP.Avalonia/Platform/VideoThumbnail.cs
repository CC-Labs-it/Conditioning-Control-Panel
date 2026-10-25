using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

// Adapted from WPF ShellThumbnailHelper at the pinned base. No WPF/System.Drawing dependency.
internal static class VideoThumbnail
{
    // Null off Windows: no thumbnailer, so callers must not decrypt pack videos to disk for nothing.
    internal static Func<string, Bitmap?>? Load = OperatingSystem.IsWindows() ? LoadWindows : null;

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory
    {
        [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap
    {
        public int Type, Width, Height, Stride;
        public ushort Planes, BitsPerPixel;
        public IntPtr Bits;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPels, YPels;
        public uint Used, Important;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context,
        [MarshalAs(UnmanagedType.LPStruct)] Guid id, out IImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("gdi32.dll")] private static extern int GetObjectW(IntPtr bitmap, int size, out NativeBitmap value);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start,
        uint lines, IntPtr data, ref BitmapInfo info, uint usage);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Bitmap? LoadWindows(string path)
    {
        IImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero, dc = IntPtr.Zero, data = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IImageFactory).GUID, out factory);
            var size = new Size { Width = 100, Height = 100 };
            if (factory.GetImage(size, 0x09, out bitmap) != 0 || bitmap == IntPtr.Zero)
            {
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                bitmap = IntPtr.Zero;
                if (factory.GetImage(size, 0x01, out bitmap) != 0 || bitmap == IntPtr.Zero) return null;
            }
            if (GetObjectW(bitmap, Marshal.SizeOf<NativeBitmap>(), out var value) == 0 || value.Width <= 0 || value.Height <= 0)
                return null;
            var stride = checked(value.Width * 4);
            data = Marshal.AllocHGlobal(checked(stride * value.Height));
            dc = CreateCompatibleDC(IntPtr.Zero);
            var info = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = value.Width,
                Height = -value.Height, Planes = 1, BitCount = 32 };
            if (GetDIBits(dc, bitmap, 0, (uint)value.Height, data, ref info, 0) != value.Height) return null;
            // Bitmap copies the pixels; no mutable native buffer crosses the worker/UI boundary.
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, data,
                new PixelSize(value.Width, value.Height), new Vector(96, 96), stride);
        }
        catch { return null; } // WPF: missing codecs/handlers leave the placeholder.
        finally
        {
            if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }
}
