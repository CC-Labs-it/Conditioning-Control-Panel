using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// GTK and WPE WebKit calls run on GLib. WebView2 controller calls run on the UI thread.
internal static class WebViewZoom
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SourceFunc(IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetFn(IntPtr view, double zoom);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double GetFn(IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int WinSetFn(IntPtr controller, double zoom);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int WinGetFn(IntPtr controller, out double zoom);
    [DllImport("libglib-2.0.so.0")] private static extern uint g_idle_add(SourceFunc callback, IntPtr data);
    [DllImport("libgobject-2.0.so.0")] private static extern IntPtr g_object_ref(IntPtr obj);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr obj);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")] private static extern IntPtr Dlopen(string name, int flags);
    private static readonly SourceFunc IdleCallback = OnIdle;
    private sealed record Api(SetFn Set, GetFn Get);
    private sealed record Pending(IntPtr View, Api Api, double? Zoom, TaskCompletionSource<double?>? Answer);
    private static Api? _gtk, _wpe;

    private static Api? Load(bool wpe)
    {
        var names = wpe ? new[] { "libWPEWebKit-2.0.so.1" } : ScrolllerWebKitFilter.Libraries;
        foreach (var name in names)
        {
            var lib = Dlopen(name, 0x1 | 0x4);
            if (lib != IntPtr.Zero && NativeLibrary.TryGetExport(lib, "webkit_web_view_set_zoom_level", out var set)
                && NativeLibrary.TryGetExport(lib, "webkit_web_view_get_zoom_level", out var get))
                return new(Marshal.GetDelegateForFunctionPointer<SetFn>(set), Marshal.GetDelegateForFunctionPointer<GetFn>(get));
        }
        return null;
    }

    internal static void Set(IPlatformHandle? handle, double zoom)
    {
        try
        {
            if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win && win.CoreWebView2Controller != IntPtr.Zero)
            {
                SetWindows(win.CoreWebView2Controller, zoom);
            }
            else if (OperatingSystem.IsLinux() && LinuxView(handle) is var (view, wpe) && view != IntPtr.Zero)
                Queue(view, wpe, zoom, null);
        }
        catch (Exception ex) { Log.Warning("WebView zoom set failed: {Error}", ex.Message); }
    }

    internal static Task<double?> Read(IPlatformHandle? handle)
    {
        try
        {
            if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win && win.CoreWebView2Controller != IntPtr.Zero)
            {
                return Task.FromResult<double?>(ReadWindows(win.CoreWebView2Controller));
            }
            if (OperatingSystem.IsLinux() && LinuxView(handle) is var (view, wpe) && view != IntPtr.Zero)
            {
                var answer = new TaskCompletionSource<double?>(TaskCreationOptions.RunContinuationsAsynchronously);
                Queue(view, wpe, null, answer);
                return answer.Task;
            }
        }
        catch (Exception ex) { Log.Warning("WebView zoom read failed: {Error}", ex.Message); }
        return Task.FromResult<double?>(null);
    }

    // ICoreWebView2Controller vtable: IUnknown, IsVisible, Bounds, then ZoomFactor get/put.
    internal static void SetWindows(IntPtr controller, double zoom) =>
        Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<WinSetFn>(Slot(controller, 8))(controller, zoom));
    internal static double ReadWindows(IntPtr controller)
    {
        Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<WinGetFn>(Slot(controller, 7))(controller, out var zoom));
        return zoom;
    }
    private static IntPtr Slot(IntPtr controller, int index) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(controller), index * IntPtr.Size);
    internal static (IntPtr, bool) LinuxView(IPlatformHandle? handle) => handle switch
    {
        IGtkWebViewPlatformHandle gtk => (gtk.WebKitWebView, false),
        ILinuxWpePlatformHandle wpe => (wpe.WebKitWebView, true),
        _ => (IntPtr.Zero, false)
    };

    private static void Queue(IntPtr view, bool wpe, double? zoom, TaskCompletionSource<double?>? answer)
    {
        try
        {
            var api = wpe ? _wpe ??= Load(true) : _gtk ??= Load(false);
            if (api == null) { answer?.TrySetResult(null); return; }
            g_object_ref(view);
            GCHandle handle = default;
            var queued = false;
            try
            {
                handle = GCHandle.Alloc(new Pending(view, api, zoom, answer));
                queued = g_idle_add(IdleCallback, GCHandle.ToIntPtr(handle)) != 0;
            }
            finally
            {
                if (!queued) { if (handle.IsAllocated) handle.Free(); g_object_unref(view); answer?.TrySetResult(null); }
            }
        }
        catch (Exception ex) { Log.Warning("WebView zoom queue failed: {Error}", ex.Message); answer?.TrySetResult(null); }
    }

    private static int OnIdle(IntPtr data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var pending = (Pending)handle.Target!;
        try
        {
            if (pending.Zoom is { } zoom) pending.Api.Set(pending.View, zoom);
            else pending.Answer?.TrySetResult(pending.Api.Get(pending.View));
        }
        catch (Exception ex) { Log.Warning("WebView zoom failed: {Error}", ex.Message); pending.Answer?.TrySetResult(null); }
        finally { g_object_unref(pending.View); handle.Free(); }
        return 0;
    }
}
