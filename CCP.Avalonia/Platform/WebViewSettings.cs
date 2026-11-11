using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Platform;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF MainWindow.Browser.cs:425, 629, 730. Home browser zoom.
internal static class WebViewSettings
{
    private delegate int Idle(IntPtr data);
    private delegate uint IdleAdd(Idle callback, IntPtr data);
    private delegate IntPtr Ref(IntPtr view);
    private delegate void Unref(IntPtr view);
    private delegate void SetZoom(IntPtr view, double zoom);
    private static readonly Idle Apply = OnIdle;

    internal static Task<bool> SetZoomAsync(IPlatformHandle? handle, double zoom)
    {
        try
        {
            if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle w && w.CoreWebView2Controller != IntPtr.Zero)
                return Task.FromResult(SetWindows(w.CoreWebView2Controller, zoom));
            if (OperatingSystem.IsLinux() && handle is IGtkWebViewPlatformHandle g && g.WebKitWebView != IntPtr.Zero)
                return SetGtk(g.WebKitWebView, zoom);
        }
        catch (Exception ex) { Log.Debug("WebViewSettings: zoom unavailable: {E}", ex.Message); }
        return Task.FromResult(false);
    }

    // ICoreWebView2Controller::put_ZoomFactor follows IUnknown, IsVisible, Bounds and get_ZoomFactor.
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PutZoom(IntPtr controller, double zoom);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SetWindows(IntPtr controller, double zoom)
    {
        var vtable = Marshal.ReadIntPtr(controller);
        var putZoom = Marshal.GetDelegateForFunctionPointer<PutZoom>(Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size));
        return putZoom(controller, zoom) >= 0;
    }

    private sealed record Request(IntPtr View, double Zoom, SetZoom Set, Unref Release, TaskCompletionSource<bool> Done);

    private static Task<bool> SetGtk(IntPtr view, double zoom)
    {
        // Use only the WebKit already loaded by Avalonia. Loading another libsoup can abort the process.
        IntPtr webkit = IntPtr.Zero;
        foreach (var name in ScrolllerWebKitFilter.Libraries)
            if ((webkit = ScrolllerWebKitFilter.LoadedLibrary(name)) != IntPtr.Zero) break;
        var glib = ScrolllerWebKitFilter.LoadedLibrary("libglib-2.0.so.0");
        var objects = ScrolllerWebKitFilter.LoadedLibrary("libgobject-2.0.so.0");
        if (webkit == IntPtr.Zero || glib == IntPtr.Zero || objects == IntPtr.Zero) return Task.FromResult(false);
        T Get<T>(IntPtr lib, string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
        var set = Get<SetZoom>(webkit, "webkit_web_view_set_zoom_level");
        var release = Get<Unref>(objects, "g_object_unref");
        var enqueue = Get<IdleAdd>(glib, "g_idle_add");
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Get<Ref>(objects, "g_object_ref")(view); // survive adapter detachment until the GLib callback
        var state = GCHandle.Alloc(new Request(view, zoom, set, release, done));
        try
        {
            if (enqueue(Apply, GCHandle.ToIntPtr(state)) != 0) return done.Task;
        }
        catch { state.Free(); release(view); throw; }
        state.Free();
        release(view);
        return Task.FromResult(false);
    }

    private static int OnIdle(IntPtr data)
    {
        var state = GCHandle.FromIntPtr(data);
        var r = (Request)state.Target!;
        var ok = false;
        try { r.Set(r.View, r.Zoom); ok = true; }
        catch (Exception ex) { Log.Debug("WebViewSettings: zoom failed: {E}", ex.Message); }
        finally { r.Release(r.View); state.Free(); r.Done.TrySetResult(ok); }
        return 0;
    }
}
