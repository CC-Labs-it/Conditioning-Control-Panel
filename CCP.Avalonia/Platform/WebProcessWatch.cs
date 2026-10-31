using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF IntakeHostService.cs:164/1025: native death feeds the intake recovery ladder.
/// Adapter destruction alone is normal window teardown, never a crash.</summary>
internal static class WebProcessWatch
{
    public static void Attach(NativeWebView web, Action<string> failed)
    {
        Watch? watch = null;
        int generation = 0;
        web.AdapterDestroyed += (_, _) => { generation++; watch?.Dispose(); watch = null; };
        web.AdapterCreated += (_, e) =>
        {
            var own = ++generation;
            watch?.Dispose();
            watch = new Watch(failed, a => Dispatcher.UIThread.Post(a));
            void Bind(int tries)
            {
                if (own != generation) return;
                try
                {
                    var handle = e.TryGetPlatformHandle() ?? web.TryGetPlatformHandle();
                    if (handle is IWindowsWebView2PlatformHandle w && OperatingSystem.IsWindows())
                    {
                        if (w.CoreWebView2 != IntPtr.Zero) watch.Listen(cb => Windows(w.CoreWebView2, cb));
                        else if (tries > 0) DispatcherTimer.RunOnce(() => Bind(tries - 1), TimeSpan.FromMilliseconds(100));
                    }
                    else if (handle is IGtkWebViewPlatformHandle g && OperatingSystem.IsLinux())
                        watch.Listen(cb => Gtk(g.WebKitWebView, cb));
                }
                catch (Exception ex) { Log.Warning("WebProcessWatch: attach failed: {E}", ex.Message); }
            }
            Bind(50);
        };
    }

    // Every callback is queued: never destroy an engine inside its native event stack. Disposed
    // subscriptions discard callbacks already queued before normal teardown or a replacement adapter.
    internal sealed class Watch(Action<string> failed, Action<Action> queue) : IDisposable
    {
        private bool _disposed;
        private Action? _stop;
        public void Listen(Func<Action<string>, Action> subscribe) => _stop = subscribe(reason =>
            queue(() => { if (!_disposed) failed(reason); }));
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stop?.Invoke();
            _stop = null;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Action Windows(IntPtr core, Action<string> failed)
    {
        var view = Microsoft.Web.WebView2.Core.CoreWebView2.CreateFromComICoreWebView2(core);
        void OnFailed(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedEventArgs e)
            => failed(e.ProcessFailedKind.ToString());
        view.ProcessFailed += OnFailed;
        return () => view.ProcessFailed -= OnFailed;
    }

    // GObject signal connection/disconnection is thread safe. Keep the object referenced until
    // disconnection so adapter destruction cannot turn the signal target into a dangling pointer.
    // Delegate and action live in the GCHandle until GObject destroys the signal closure.
    private sealed record GtkCallback(Action<string> Failed, Terminated Callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Terminated(IntPtr view, int reason, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClosureNotify(IntPtr data, IntPtr closure);
    private static readonly ClosureNotify Free = (data, _) => GCHandle.FromIntPtr(data).Free();

    internal static Action Gtk(IntPtr view, Action<string> failed)
    {
        if (view == IntPtr.Zero) return () => { };
        Terminated callback = (_, reason, _) => failed("webkit:" + reason);
        var root = GCHandle.Alloc(new GtkCallback(failed, callback));
        ObjectRef(view);
        var id = Connect(view, "web-process-terminated", callback, GCHandle.ToIntPtr(root), Free, 0);
        if (id == 0) { root.Free(); ObjectUnref(view); return () => { }; }
        return () =>
        {
            var cleanup = GCHandle.Alloc((Action)(() => { Disconnect(view, id); ObjectUnref(view); }));
            IdleAdd(Cleanup, GCHandle.ToIntPtr(cleanup));
        };
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SourceFunc(IntPtr data);
    private static readonly SourceFunc Cleanup = data =>
    {
        var root = GCHandle.FromIntPtr(data);
        try { ((Action)root.Target!)(); }
        finally { root.Free(); }
        return 0;
    };
    [DllImport("libglib-2.0.so.0", EntryPoint = "g_idle_add")]
    private static extern uint IdleAdd(SourceFunc callback, IntPtr data);

    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_object_ref")]
    private static extern IntPtr ObjectRef(IntPtr obj);
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    private static extern void ObjectUnref(IntPtr obj);
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_signal_connect_data")]
    private static extern nuint Connect(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal,
        Terminated handler, IntPtr data, ClosureNotify destroy, int flags);
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_signal_handler_disconnect")]
    private static extern void Disconnect(IntPtr obj, nuint id);
}
