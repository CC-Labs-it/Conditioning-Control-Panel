using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF ShellThumbnailHelper on Windows; the already shared VLC decoder elsewhere.
/// Never opens a window, plays audio, or publishes a decoder-owned mutable bitmap.</summary>
internal static class AssetVideoThumbnail
{
    internal static async Task<Bitmap?> LoadAsync(string path)
    {
        if (OperatingSystem.IsWindows()) return WindowsThumbnail(path);
        var vlc = LibVlcAudio.Shared;
        if (vlc == null) return null;
        using var media = new Media(vlc, path, FromType.FromPath);
        media.AddOption(LibVlcSilence.NoAudioOption);
        media.AddOption(":no-spu");
        using var player = new MediaPlayer(media);
        var ready = new TaskCompletionSource<Bitmap?>(TaskCreationOptions.RunContinuationsAsynchronously);
        VlcFrameSink? sink = null;
        sink = new VlcFrameSink(player, () => media, _ => { }, () =>
        {
            if (ready.Task.IsCompleted || sink?.Bitmap == null) return;
            // Copy on the UI thread before Free disposes the sink's mutable frame.
            try
            {
                using var png = new MemoryStream();
                sink.Bitmap.Save(png, new PngBitmapEncoderOptions());
                png.Position = 0;
                var copy = new Bitmap(png);
                if (!ready.TrySetResult(copy)) copy.Dispose();
            }
            catch { ready.TrySetResult(null); }
        }, maxDimension: 100);
        player.EncounteredError += (_, _) => ready.TrySetResult(null);
        try
        {
            if (!player.Play()) return null;
            // ponytail: a broken codec must not hold a folder's four decode slots forever.
            await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            ready.TrySetResult(null);
            return await ready.Task.ConfigureAwait(false);
        }
        finally
        {
            ready.TrySetResult(null);
            // Stop joins VLC's callback threads; only then may the sink release its buffer.
            try { await Task.Run(player.Stop).ConfigureAwait(false); }
            finally { await Dispatcher.UIThread.InvokeAsync(sink.Free); }
        }
    }

    // Adapted from WPF Helpers/ShellThumbnailHelper.cs at f73c51fdd80d3c1a2e79e5ddf0357eec77e1ba5d.
    [SupportedOSPlatform("windows")]
    private static Bitmap? WindowsThumbnail(string path)
    {
        IShellItemImageFactory? factory = null;
        var handle = IntPtr.Zero;
        var dc = IntPtr.Zero;
        var pixels = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out factory);
            var size = new NativeSize { Width = 100, Height = 100 };
            if (factory.GetImage(size, 0x01 | 0x08, out handle) != 0 || handle == IntPtr.Zero)
            {
                if (handle != IntPtr.Zero) DeleteObject(handle);
                handle = IntPtr.Zero;
                if (factory.GetImage(size, 0x01, out handle) != 0 || handle == IntPtr.Zero) return null;
            }
            if (GetObject(handle, Marshal.SizeOf<NativeBitmap>(), out var bitmap) == 0) return null;
            var info = new BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height,
                Planes = 1, BitCount = 32 };
            var stride = checked(bitmap.Width * 4);
            pixels = Marshal.AllocHGlobal(checked(stride * bitmap.Height));
            dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero || GetDIBits(dc, handle, 0, (uint)bitmap.Height, pixels, ref info, 0) == 0) return null;
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Opaque, pixels,
                new PixelSize(bitmap.Width, bitmap.Height), new Vector(96, 96), stride);
        }
        catch { return null; } // missing shell codec: retain the video placeholder, as WPF does
        finally
        {
            if (pixels != IntPtr.Zero) Marshal.FreeHGlobal(pixels);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (handle != IntPtr.Zero) DeleteObject(handle);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }
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
        public uint ClrUsed, ClrImportant, Colors;
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
