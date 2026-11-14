using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Threading;
namespace ConditioningControlPanel.Avalonia.Platform; // 7cf2cee4dcfa; WPF Controls/ChasterTrailerView.cs:164,197-209.
internal sealed class TrailerBrowserPolicy(Action detach) : IDisposable {
    private Action? _detach = detach;
    public void Dispose() { var stop = _detach; _detach = null; stop?.Invoke(); }
    internal static IDisposable Attach(IPlatformHandle? handle, Action failed) {
        if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win) return Windows(win, failed);
        var view = handle switch { IGtkWebViewPlatformHandle gtk => gtk.WebKitWebView, ILinuxWpePlatformHandle wpe => wpe.WebKitWebView, _ => IntPtr.Zero };
        if (!OperatingSystem.IsLinux() || view == IntPtr.Zero) throw new NotSupportedException("Trailer browser handle unavailable");
        g_object_ref(view); var live = true;
        Terminated callback = (_, _, _) => Dispatcher.UIThread.Post(() => { if (live) failed(); });
        var id = g_signal_connect_data(view, "web-process-terminated", callback, IntPtr.Zero, IntPtr.Zero, 0);
        if (id == 0) { g_object_unref(view); throw new InvalidOperationException("Trailer failure signal unavailable"); }
        return new TrailerBrowserPolicy(() => { live = false; g_signal_handler_disconnect(view, id); g_object_unref(view); GC.KeepAlive(callback); });
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IDisposable Windows(IWindowsWebView2PlatformHandle handle, Action failed) {
        var core = Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2);
        EventHandler<Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedEventArgs> callback = (_, _) => failed();
        core.ProcessFailed += callback;
        return new TrailerBrowserPolicy(() => core.ProcessFailed -= callback);
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Terminated(IntPtr view, int reason, IntPtr data);
    [DllImport("libgobject-2.0.so.0")] private static extern IntPtr g_object_ref(IntPtr view);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr view);
    [DllImport("libgobject-2.0.so.0")] private static extern ulong g_signal_connect_data(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, Terminated callback, IntPtr data, IntPtr destroy, int flags);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_signal_handler_disconnect(IntPtr view, ulong id);
}
