using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF ShellThumbnailHelper, with a portable bitmap copy instead of WPF Imaging.</summary>
internal static class ShellVideoThumbnail
{
    // Keep Windows shell frame selection on Windows; other desktops use their portable decoder.
    internal static Bitmap? Load(string path, Func<string, Bitmap?> portable) =>
        OperatingSystem.IsWindows() ? LoadWindows(path) : portable(path);

    [SupportedOSPlatform("windows")]
    private static Bitmap? LoadWindows(string path)
    {
        IntPtr handle = IntPtr.Zero;
        IShellItemImageFactory? factory = null;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            var hr = factory.GetImage(new Size(100, 100), 0x09, out handle); // BIGGERSIZEOK | THUMBNAILONLY
            if (hr != 0 || handle == IntPtr.Zero)
            {
                if (handle != IntPtr.Zero) DeleteObject(handle);
                handle = IntPtr.Zero;
                hr = factory.GetImage(new Size(100, 100), 0x01, out handle); // Shell icon fallback, like WPF.
                if (hr != 0 || handle == IntPtr.Zero) return null;
            }
            if (GetObject(handle, Marshal.SizeOf<NativeBitmap>(), out var source) == 0) return null;
            var header = new BitmapInfo
            {
                Size = 40, Width = source.Width, Height = -Math.Abs(source.Height),
                Planes = 1, BitCount = 32
            };
            var pixels = new byte[checked(source.Width * Math.Abs(source.Height) * 4)];
            var dc = GetDC(IntPtr.Zero);
            try
            {
                if (GetDIBits(dc, handle, 0, (uint)Math.Abs(source.Height), pixels, ref header, 0) == 0) return null;
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            // BI_RGB's fourth byte is reserved, not alpha. Publish an opaque, owned bitmap.
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pinned.AddrOfPinnedObject(),
                    new PixelSize(source.Width, Math.Abs(source.Height)), new Vector(96, 96), source.Width * 4);
            }
            finally { pinned.Free(); }
        }
        catch { return null; } // WPF leaves the placeholder when the shell cannot supply an image.
        finally
        {
            if (handle != IntPtr.Zero) DeleteObject(handle);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap);
    }
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Size(int width, int height) { public readonly int Width = width, Height = height; }
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
        public uint ClrUsed, ClrImportant;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context,
        [MarshalAs(UnmanagedType.LPStruct)] Guid id, out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines,
        [Out] byte[] pixels, ref BitmapInfo info, uint usage);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
