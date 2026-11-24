using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Platform;

// WebKitGTK's context-menu signal can suppress the native Back/Reload menu before it opens.
internal sealed class LocalPageContextMenu(Action detach) : IDisposable
{
    private Action? _detach = detach;
    public void Dispose() { var detach = _detach; _detach = null; detach?.Invoke(); }

    internal static IDisposable? Attach(IPlatformHandle? handle, Func<Uri?, bool> suppress)
    {
        if (!OperatingSystem.IsLinux() || handle is not IGtkWebViewPlatformHandle gtk || gtk.WebKitWebView == IntPtr.Zero)
            return null;
        IntPtr webkit = IntPtr.Zero;
        foreach (var name in ScrolllerWebKitFilter.Libraries)
            if ((webkit = ScrolllerWebKitFilter.LoadedLibrary(name)) != IntPtr.Zero) break;
        if (webkit == IntPtr.Zero) return null;
        var getUri = Marshal.GetDelegateForFunctionPointer<GetUri>(NativeLibrary.GetExport(webkit, "webkit_web_view_get_uri"));
        var view = gtk.WebKitWebView;
        g_object_ref(view);
        Menu callback = (v, _, _, _, _) =>
        {
            try
            {
                var address = Marshal.PtrToStringUTF8(getUri(v));
                return Uri.TryCreate(address, UriKind.Absolute, out var uri) && suppress(uri) ? 1 : 0;
            }
            catch { return 0; }
        };
        var id = g_signal_connect_data(view, "context-menu", callback, IntPtr.Zero, IntPtr.Zero, 0);
        if (id == 0) { g_object_unref(view); return null; }
        return new LocalPageContextMenu(() => { g_signal_handler_disconnect(view, id); g_object_unref(view); GC.KeepAlive(callback); });
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetUri(IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Menu(IntPtr view, IntPtr menu, IntPtr evt, IntPtr hit, IntPtr data);
    [DllImport("libgobject-2.0.so.0")] private static extern IntPtr g_object_ref(IntPtr view);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr view);
    [DllImport("libgobject-2.0.so.0")] private static extern ulong g_signal_connect_data(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, Menu callback, IntPtr data, IntPtr destroy, int flags);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_signal_handler_disconnect(IntPtr view, ulong id);
}
