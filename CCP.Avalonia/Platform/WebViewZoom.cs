using System;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// GTK and WPE WebKit calls run on GLib. WebView2 controller calls run on the UI thread.
internal static class WebViewZoom
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SourceFunc(IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyNotify(IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetFn(IntPtr view, double zoom);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int WinSetFn(IntPtr controller, double zoom);
    [DllImport("libglib-2.0.so.0")] private static extern uint g_idle_add_full(int priority, SourceFunc callback, IntPtr data, DestroyNotify destroy);
    [DllImport("libglib-2.0.so.0")] private static extern int g_source_remove(uint id);
    [DllImport("libgobject-2.0.so.0")] private static extern IntPtr g_object_ref(IntPtr obj);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr obj);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")] private static extern IntPtr Dlopen(string name, int flags);
    private static readonly SourceFunc IdleCallback = OnIdle;
    private static readonly DestroyNotify DestroyCallback = OnDestroy;
    private sealed record Api(SetFn Set);
    private sealed class Pending(IntPtr view, Api api, double zoom)
    {
        internal readonly IntPtr View = view;
        internal readonly Api Api = api;
        internal readonly double Zoom = zoom;
        internal uint Source;
        internal bool Cancelled;
        internal void Cancel()
        {
            lock (this)
            {
                Cancelled = true;
                if (Source != 0) { var source = Source; Source = 0; g_source_remove(source); }
            }
        }
    }
    private static Api? _gtk, _wpe;

    private static Api? Load(bool wpe)
    {
        var names = wpe ? new[] { "libWPEWebKit-2.0.so.1" } : ScrolllerWebKitFilter.Libraries;
        foreach (var name in names)
        {
            var lib = Dlopen(name, 0x1 | 0x4);
            if (lib != IntPtr.Zero && NativeLibrary.TryGetExport(lib, "webkit_web_view_set_zoom_level", out var set))
                return new(Marshal.GetDelegateForFunctionPointer<SetFn>(set));
        }
        return null;
    }

    internal static bool IsReady(IPlatformHandle? handle) => handle switch
    {
        IWindowsWebView2PlatformHandle win => win.CoreWebView2Controller != IntPtr.Zero,
        IGtkWebViewPlatformHandle gtk => gtk.WebKitWebView != IntPtr.Zero,
        ILinuxWpePlatformHandle wpe => wpe.WebKitWebView != IntPtr.Zero,
        _ => false
    };

    internal static void Set(IPlatformHandle? handle, double zoom, CancellationToken cancellation)
    {
        try
        {
            if (cancellation.IsCancellationRequested) return;
            if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win && win.CoreWebView2Controller != IntPtr.Zero)
            {
                SetWindows(win.CoreWebView2Controller, zoom);
            }
            else if (OperatingSystem.IsLinux() && LinuxView(handle) is var (view, wpe) && view != IntPtr.Zero)
                Queue(view, wpe, zoom, cancellation);
        }
        catch (Exception ex) { Log.Warning("WebView zoom set failed: {Error}", ex.Message); }
    }

    // ICoreWebView2Controller vtable: IUnknown, IsVisible, Bounds, then ZoomFactor get/put.
    internal static void SetWindows(IntPtr controller, double zoom) =>
        Marshal.ThrowExceptionForHR(Marshal.GetDelegateForFunctionPointer<WinSetFn>(Slot(controller, 8))(controller, zoom));
    private static IntPtr Slot(IntPtr controller, int index) => Marshal.ReadIntPtr(Marshal.ReadIntPtr(controller), index * IntPtr.Size);
    internal static (IntPtr, bool) LinuxView(IPlatformHandle? handle) => handle switch
    {
        IGtkWebViewPlatformHandle gtk => (gtk.WebKitWebView, false),
        ILinuxWpePlatformHandle wpe => (wpe.WebKitWebView, true),
        _ => (IntPtr.Zero, false)
    };

    private static void Queue(IntPtr view, bool wpe, double zoom, CancellationToken cancellation)
    {
        try
        {
            var api = wpe ? _wpe ??= Load(true) : _gtk ??= Load(false);
            if (api == null) return;
            var pending = new Pending(view, api, zoom);
            cancellation.Register(static state => ((Pending)state!).Cancel(), pending);
            lock (pending)
            {
                if (pending.Cancelled) return;
                g_object_ref(view);
                var handle = GCHandle.Alloc(pending);
                try
                {
                    pending.Source = g_idle_add_full(200, IdleCallback, GCHandle.ToIntPtr(handle), DestroyCallback);
                    if (pending.Source == 0) { handle.Free(); g_object_unref(view); }
                }
                catch { handle.Free(); g_object_unref(view); throw; }
            }
        }
        catch (Exception ex) { Log.Warning("WebView zoom queue failed: {Error}", ex.Message); }
    }

    private static int OnIdle(IntPtr data)
    {
        var pending = (Pending)GCHandle.FromIntPtr(data).Target!;
        lock (pending)
        {
            pending.Source = 0;
            try { if (!pending.Cancelled) pending.Api.Set(pending.View, pending.Zoom); }
            catch (Exception ex) { Log.Warning("WebView zoom failed: {Error}", ex.Message); }
        }
        return 0;
    }

    private static void OnDestroy(IntPtr data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var pending = (Pending)handle.Target!;
        g_object_unref(pending.View);
        handle.Free();
    }
}
