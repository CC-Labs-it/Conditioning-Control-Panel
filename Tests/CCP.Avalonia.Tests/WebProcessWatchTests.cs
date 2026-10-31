using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebProcessWatchTests
{
    // Exercise the real GObject signal ABI without a display or a WebKit subprocess. WebKitGTK's
    // web-process-terminated carries one enum (a gint); the synthetic GObject signal has that ABI.
    [Fact]
    public void GtkNativeSignalReachesWatchAndDisconnects()
    {
        if (!OperatingSystem.IsLinux()) return;
        var type = ObjectType();
        if (SignalLookup("web-process-terminated", type) == 0)
            Assert.NotEqual(0u, SignalNew("web-process-terminated", type, 2, IntPtr.Zero, IntPtr.Zero,
                IntPtr.Zero, IntPtr.Zero, 4, 1, new nuint[] { 24 })); // void, one gint
        var obj = ObjectNew(type, 0, IntPtr.Zero);
        var reasons = new List<string>();
        var queued = new Queue<Action>();
        using var watch = new WebProcessWatch.Watch(reasons.Add, queued.Enqueue);
        watch.Listen(cb => WebProcessWatch.Gtk(obj, cb));
        try
        {
            SignalEmit(obj, "web-process-terminated", 2);
            Assert.Empty(reasons);
            Assert.Single(queued)();
            queued.Clear();
            Assert.Equal(new[] { "webkit:2" }, reasons);
            watch.Dispose();
            while (Iterate(IntPtr.Zero, false)) { } // the GLib thread disconnects and drops its ref
            SignalEmit(obj, "web-process-terminated", 0);
            Assert.Empty(queued); // proves disconnection, beyond the Watch's stale-callback guard
        }
        finally { ObjectUnref(obj); }
    }

    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_object_get_type")]
    private static extern nuint ObjectType();
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_object_newv")]
    private static extern IntPtr ObjectNew(nuint type, uint count, IntPtr parameters);
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_object_unref")]
    private static extern void ObjectUnref(IntPtr obj);
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_signal_lookup")]
    private static extern uint SignalLookup([MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint type);
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_signal_newv")]
    private static extern uint SignalNew([MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint type, int flags,
        IntPtr closure, IntPtr accumulator, IntPtr data, IntPtr marshaller, nuint result, uint count, nuint[] parameters);
    [DllImport("libgobject-2.0.so.0", EntryPoint = "g_signal_emit_by_name")]
    private static extern void SignalEmit(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int reason);
    [DllImport("libglib-2.0.so.0", EntryPoint = "g_main_context_iteration")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Iterate(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool block);
}
