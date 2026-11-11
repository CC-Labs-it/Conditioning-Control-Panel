using System;
using System.Runtime.InteropServices;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

// WebKit owns its view on the GLib thread. Keep a reference until the queued call finishes.
internal static class WebViewZoom
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SourceFunc(IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetZoomFn(IntPtr view, double zoom);
    [DllImport("libglib-2.0.so.0")] private static extern uint g_idle_add(SourceFunc callback, IntPtr data);
    [DllImport("libgobject-2.0.so.0")] private static extern IntPtr g_object_ref(IntPtr obj);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr obj);
    [DllImport("libdl.so.2", EntryPoint = "dlopen")] private static extern IntPtr Dlopen(string name, int flags);
    private static readonly SourceFunc IdleCallback = OnIdle;
    private sealed record Pending(IntPtr View, double Zoom, SetZoomFn Set);
    private static SetZoomFn? _set;

    private static SetZoomFn? Load()
    {
        foreach (var name in ScrolllerWebKitFilter.Libraries)
        {
            var lib = Dlopen(name, 0x1 | 0x4);
            if (lib != IntPtr.Zero && NativeLibrary.TryGetExport(lib, "webkit_web_view_set_zoom_level", out var symbol))
                return Marshal.GetDelegateForFunctionPointer<SetZoomFn>(symbol);
        }
        return null;
    }

    internal static void SetGtk(IntPtr view, double zoom)
    {
        if (!OperatingSystem.IsLinux() || view == IntPtr.Zero) return;
        try
        {
            var set = _set ??= Load();
            if (set == null) return;
            g_object_ref(view);
            GCHandle handle = default;
            var queued = false;
            try
            {
                handle = GCHandle.Alloc(new Pending(view, zoom, set));
                queued = g_idle_add(IdleCallback, GCHandle.ToIntPtr(handle)) != 0;
            }
            finally
            {
                if (!queued) { if (handle.IsAllocated) handle.Free(); g_object_unref(view); }
            }
        }
        catch (Exception ex) { Log.Warning("WebView zoom failed: {Error}", ex.Message); }
    }

    private static int OnIdle(IntPtr data)
    {
        var handle = GCHandle.FromIntPtr(data);
        var pending = (Pending)handle.Target!;
        try { pending.Set(pending.View, pending.Zoom); }
        catch (Exception ex) { Log.Warning("WebView zoom failed: {Error}", ex.Message); }
        finally { g_object_unref(pending.View); handle.Free(); }
        return 0;
    }
}
