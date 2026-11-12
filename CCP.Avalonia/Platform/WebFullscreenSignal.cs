using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Threading;
namespace ConditioningControlPanel.Avalonia.Platform;
// WPF EnhancementPlayerWindow.xaml.cs:1722,2008-2028: fullscreen entry follows the engine's own
// ContainsFullScreenElementChanged, never a page message. The engine raises it only for a
// requestFullscreen it granted, and both engines grant that only inside a user gesture.
internal sealed class WebFullscreenSignal(Action detach) : IDisposable {
    private Action? _detach = detach;
    public void Dispose() { var stop = _detach; _detach = null; stop?.Invoke(); }
    internal static IDisposable Attach(IPlatformHandle? handle, Action entered, Action exited) {
        if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win) return Windows(win, entered, exited);
        var view = handle switch { IGtkWebViewPlatformHandle gtk => gtk.WebKitWebView, ILinuxWpePlatformHandle wpe => wpe.WebKitWebView, _ => IntPtr.Zero };
        if (!OperatingSystem.IsLinux() || view == IntPtr.Zero) throw new NotSupportedException("Fullscreen signal unavailable");
        g_object_ref(view); var live = true;
        // WebKitWebView::enter-fullscreen. FALSE keeps WebKit's default handling, as before.
        Enter callback = (_, _) => { Dispatcher.UIThread.Post(() => { if (live) entered(); }); return 0; };
        Enter exitCallback = (_, _) => { Dispatcher.UIThread.Post(() => { if (live) exited(); }); return 0; };
        var id = g_signal_connect_data(view, "enter-fullscreen", callback, IntPtr.Zero, IntPtr.Zero, 0);
        var exitId = g_signal_connect_data(view, "leave-fullscreen", exitCallback, IntPtr.Zero, IntPtr.Zero, 0);
        if (id == 0 || exitId == 0) {
            if (id != 0) g_signal_handler_disconnect(view, id);
            if (exitId != 0) g_signal_handler_disconnect(view, exitId);
            g_object_unref(view); throw new InvalidOperationException("Fullscreen signal unavailable");
        }
        return new WebFullscreenSignal(() => { live = false; g_signal_handler_disconnect(view, id); g_signal_handler_disconnect(view, exitId); g_object_unref(view); GC.KeepAlive(callback); GC.KeepAlive(exitCallback); });
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IDisposable Windows(IWindowsWebView2PlatformHandle handle, Action entered, Action exited) {
        var core = Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2);
        EventHandler<object> callback = (_, _) => { if (core.ContainsFullScreenElement) entered(); else exited(); };
        core.ContainsFullScreenElementChanged += callback;
        return new WebFullscreenSignal(() => core.ContainsFullScreenElementChanged -= callback);
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enter(IntPtr view, IntPtr data);
    [DllImport("libgobject-2.0.so.0")] private static extern IntPtr g_object_ref(IntPtr view);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr view);
    [DllImport("libgobject-2.0.so.0")] private static extern ulong g_signal_connect_data(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, Enter callback, IntPtr data, IntPtr destroy, int flags);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_signal_handler_disconnect(IntPtr view, ulong id);
}
