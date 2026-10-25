using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

// Adapted from WPF Helpers/ShellThumbnailHelper.cs. No shell handler exists on other OSes;
// ponytail: retain the existing Fluent video placeholder there and when a codec is unavailable.
internal static class ShellThumbnail
{
    internal static Bitmap? Get(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        IntPtr handle = IntPtr.Zero, dc = IntPtr.Zero, pixels = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            var size = new Size(100, 100);
            var hr = factory.GetImage(size, 0x09, out handle); // BIGGERSIZEOK | THUMBNAILONLY
            if (hr != 0 || handle == IntPtr.Zero)
            {
                if (handle != IntPtr.Zero) DeleteObject(handle);
                handle = IntPtr.Zero;
                if (factory.GetImage(size, 0x01, out handle) != 0 || handle == IntPtr.Zero) return null;
            }
            if (GetObject(handle, Marshal.SizeOf<NativeBitmap>(), out var native) == 0) return null;
            var width = native.Width;
            var height = Math.Abs(native.Height);
            var stride = checked(width * 4);
            pixels = Marshal.AllocHGlobal(checked(stride * height));
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (GetDIBits(dc, handle, 0, (uint)height, pixels, ref info, 0) != height) return null;
            // Bitmap copies the pixels; no mutable native buffer crosses the worker/UI boundary.
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pixels,
                new PixelSize(width, height), new Vector(96, 96), stride);
        }
        catch { return null; } // unreadable/unsupported: WPF keeps the placeholder
        finally
        {
            if (pixels != IntPtr.Zero) Marshal.FreeHGlobal(pixels);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (handle != IntPtr.Zero) DeleteObject(handle);
            if (factory != null && OperatingSystem.IsWindows()) Marshal.ReleaseComObject(factory);
        }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap);
    }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Size(int width, int height)
    {
        public readonly int Width = width;
        public readonly int Height = height;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type, Width, Height, WidthBytes;
        public ushort Planes, BitsPixel;
        public IntPtr Bits;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context,
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start,
        uint lines, IntPtr pixels, ref BitmapInfo info, uint usage);
}
