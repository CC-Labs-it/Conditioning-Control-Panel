using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;

namespace ConditioningControlPanel.Avalonia.Platform;

// Only the local trailer opts in. Native handles are owned by Avalonia; this seam only borrows them.
internal static class TrailerBrowserPolicy
{
    internal static IDisposable? Attach(IPlatformHandle? handle, Action failed)
    {
        if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle windows)
            return AttachWindows(windows, failed);
        if (OperatingSystem.IsLinux() && handle is IGtkWebViewPlatformHandle gtk)
            return new GtkPolicy(gtk.WebKitWebView, failed);
        // WPE exposes the same GObject signals as WebKitGTK.
        if (OperatingSystem.IsLinux() && handle is ILinuxWpePlatformHandle wpe)
            return new GtkPolicy(wpe.WebKitWebView, failed);
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static IDisposable AttachWindows(IWindowsWebView2PlatformHandle handle, Action failed)
    {
        var core = CoreWebView2.CreateFromComICoreWebView2(handle.CoreWebView2);
        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsWebMessageEnabled = false;
        EventHandler<CoreWebView2ProcessFailedEventArgs> handler = (_, _) => failed();
        core.ProcessFailed += handler;
        return new Subscription(() => core.ProcessFailed -= handler);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { var action = _dispose; _dispose = null; action?.Invoke(); }
    }

    // These GObject signals exist in both supported WebKit engines. Return true to suppress WebKit's
    // default context menu and load-error document; the host's still picture handles failed navigation.
    private sealed class GtkPolicy : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void Terminated(IntPtr view, int reason, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ContextMenu(IntPtr view, IntPtr menu, IntPtr evt, IntPtr hit, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int LoadFailed(IntPtr view, int evt, IntPtr uri, IntPtr error, IntPtr data);
        private readonly IntPtr _view;
        private readonly List<(ulong Id, Delegate Callback)> _signals = new();
        private bool _disposed;

        internal GtkPolicy(IntPtr view, Action failed)
        {
            _view = g_object_ref(view);
            try
            {
                Connect("web-process-terminated", new Terminated((_, _, _) =>
                    Dispatcher.UIThread.Post(() => { if (!_disposed) failed(); })));
                Connect("context-menu", new ContextMenu((_, _, _, _, _) => 1));
                Connect("load-failed", new LoadFailed((_, _, _, _, _) => 1));
            }
            catch { Dispose(); throw; }
        }

        private void Connect(string signal, Delegate callback)
        {
            var id = g_signal_connect_data(_view, signal, Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero, IntPtr.Zero, 0);
            if (id == 0) throw new InvalidOperationException("Trailer browser signal unavailable");
            _signals.Add((id, callback));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var signal in _signals) g_signal_handler_disconnect(_view, signal.Id);
            _signals.Clear();
            g_object_unref(_view);
        }

        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr g_object_ref(IntPtr instance);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_object_unref(IntPtr instance);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong g_signal_connect_data(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal,
            IntPtr callback, IntPtr data, IntPtr destroy, int flags);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)]
        private static extern void g_signal_handler_disconnect(IntPtr instance, ulong id);
    }
}
