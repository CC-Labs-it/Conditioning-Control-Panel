using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia.Threading;
namespace ConditioningControlPanel.Avalonia.Platform;
// WPF Controls/ChasterTrailerView.cs:152,156,164,197-209; WebKit counterpart of the Windows policy.
internal sealed class TrailerLinuxPolicy : IDisposable {
    internal interface ISignals {
        void Ref(IntPtr view);
        void Unref(IntPtr view);
        ulong Connect(IntPtr view, string signal, Delegate callback);
        void Disconnect(IntPtr view, ulong id);
    }
    private readonly ISignals _native;
    private readonly IntPtr _view;
    private readonly List<(ulong Id, Delegate Callback)> _signals = new();
    private bool _disposed;
    internal TrailerLinuxPolicy(IntPtr view, Action failed, ISignals? native = null, Action<Action>? post = null) {
        _native = native ?? new NativeSignals(); _view = view;
        post ??= action => Dispatcher.UIThread.Post(action);
        _native.Ref(view);
        try {
            Connect("web-process-terminated", new Terminated((_, _, _) => post(() => { if (!_disposed) failed(); })));
            Connect("context-menu", new ContextMenu((_, _, _, _, _) => 1));
            Connect("load-failed", new LoadFailed((_, _, _, _, _) => 1));
        }
        catch { Dispose(); throw; }
    }
    private void Connect(string name, Delegate callback) {
        var id = _native.Connect(_view, name, callback);
        if (id == 0) throw new InvalidOperationException("Trailer browser signal unavailable");
        _signals.Add((id, callback));
    }
    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        foreach (var signal in _signals) _native.Disconnect(_view, signal.Id);
        _native.Unref(_view);
        GC.KeepAlive(_signals);
        _signals.Clear();
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Terminated(IntPtr view, int reason, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ContextMenu(IntPtr view, IntPtr menu, IntPtr evt, IntPtr hit, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoadFailed(IntPtr view, int evt, IntPtr uri, IntPtr error, IntPtr data);
    private sealed class NativeSignals : ISignals {
        public void Ref(IntPtr view) => g_object_ref(view);
        public void Unref(IntPtr view) => g_object_unref(view);
        public ulong Connect(IntPtr view, string signal, Delegate callback) => g_signal_connect_data(view, signal, Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero, IntPtr.Zero, 0);
        public void Disconnect(IntPtr view, ulong id) => g_signal_handler_disconnect(view, id);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr g_object_ref(IntPtr view);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)] private static extern void g_object_unref(IntPtr view);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)] private static extern ulong g_signal_connect_data(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, IntPtr callback, IntPtr data, IntPtr destroy, int flags);
        [DllImport("libgobject-2.0.so.0", CallingConvention = CallingConvention.Cdecl)] private static extern void g_signal_handler_disconnect(IntPtr view, ulong id);
    }
}
