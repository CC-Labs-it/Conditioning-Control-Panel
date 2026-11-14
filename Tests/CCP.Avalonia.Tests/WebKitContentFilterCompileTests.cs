using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WebKitContentFilterCompileTests
{
    private const string WebKit = "libwebkit2gtk-4.1.so.0";
    private const string Glib = "libglib-2.0.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private delegate void AsyncReady(IntPtr source, IntPtr result, IntPtr data);
    [DllImport(WebKit)] private static extern IntPtr webkit_user_content_filter_store_new([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(WebKit)] private static extern void webkit_user_content_filter_store_save(IntPtr store, [MarshalAs(UnmanagedType.LPUTF8Str)] string id, IntPtr bytes, IntPtr cancel, AsyncReady callback, IntPtr data);
    [DllImport(WebKit)] private static extern IntPtr webkit_user_content_filter_store_save_finish(IntPtr store, IntPtr result, out IntPtr error);
    [DllImport(WebKit)] private static extern void webkit_user_content_filter_unref(IntPtr filter);
    [DllImport(Glib)] private static extern IntPtr g_bytes_new(byte[] data, UIntPtr size);
    [DllImport(Glib)] private static extern void g_bytes_unref(IntPtr bytes);
    [DllImport(Glib)] private static extern bool g_main_context_iteration(IntPtr context, bool mayBlock);
    [DllImport(Glib)] private static extern void g_error_free(IntPtr error);
    [DllImport(GObject)] private static extern void g_object_unref(IntPtr obj);

    [Fact]
    public void HomeFilterCompilesWithNativeWebKit()
    {
        var library = IntPtr.Zero;
        if (!OperatingSystem.IsLinux() || !NativeLibrary.TryLoad(WebKit, out library))
            Assert.Skip("WebKitGTK 4.1 is not installed");
        NativeLibrary.Free(library);
        var path = Path.Combine(Path.GetTempPath(), "ccp-webkit-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        var store = webkit_user_content_filter_store_new(path);
        Assert.NotEqual(IntPtr.Zero, store);
        var json = Encoding.UTF8.GetBytes(ScrolllerWebKitFilter.RulesFor(true));
        var bytes = g_bytes_new(json, (UIntPtr)json.Length);
        bool finished = false;
        string? errorMessage = null;
        AsyncReady callback = (source, result, _) =>
        {
            var filter = webkit_user_content_filter_store_save_finish(source, result, out var error);
            if (error != IntPtr.Zero)
            {
                errorMessage = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8));
                g_error_free(error);
            }
            if (filter != IntPtr.Zero) webkit_user_content_filter_unref(filter);
            else errorMessage ??= "WebKit returned no filter";
            finished = true;
        };
        webkit_user_content_filter_store_save(store, "ccp-native-test", bytes, IntPtr.Zero, callback, IntPtr.Zero);
        g_bytes_unref(bytes);
        var timer = Stopwatch.StartNew();
        while (!finished && timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            g_main_context_iteration(IntPtr.Zero, false);
            Thread.Sleep(10);
        }
        GC.KeepAlive(callback);
        Assert.True(finished, "WebKit filter compilation timed out");
        g_object_unref(store);
        Directory.Delete(path, recursive: true);
        Assert.True(errorMessage is null, errorMessage);
    }
}
