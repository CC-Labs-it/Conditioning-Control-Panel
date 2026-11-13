using System;
using System.Runtime.InteropServices;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
namespace ConditioningControlPanel.Avalonia.Platform;
// WPF DeeperEditorWindow.xaml.cs:5006-5013: keys pressed while the preview has focus reach the window's
// handler through WebView2's accelerator keys, whichever frame of the page holds focus. A page script
// cannot see keys inside a cross-origin frame, so this listens where the engine receives input:
// WebView2 AcceleratorKeyPressed on Windows, the WebKitGTK widget's key-press-event on Linux.
// wants runs on the engine's input thread and decides synchronously; pressed runs on the UI thread.
internal sealed class WebKeySignal(Action detach) : IDisposable {
    private Action? _detach = detach;
    public void Dispose() { var stop = _detach; _detach = null; stop?.Invoke(); }

    internal static IDisposable Attach(IPlatformHandle? handle, Func<Key, KeyModifiers, bool> wants, Action<Key, KeyModifiers> pressed) {
        if (OperatingSystem.IsWindows() && handle is IWindowsWebView2PlatformHandle win) return Windows(win, wants, pressed);
        // WPE has no GTK widget and no key signal: the caller's page script stays the only route there.
        if (!OperatingSystem.IsLinux() || handle is not IGtkWebViewPlatformHandle gtk || gtk.WebKitWebView == IntPtr.Zero)
            throw new NotSupportedException("Native key signal unavailable");
        var view = gtk.WebKitWebView;
        g_object_ref(view); var live = true;
        // GtkWidget::key-press-event is RUN_LAST, so this runs before WebKit forwards the key to the page.
        KeyPress callback = (_, ev, _) => {
            if (gdk_event_get_keyval(ev, out var keyval) == 0 || gdk_event_get_state(ev, out var state) == 0) return 0;
            var (key, mods) = FromGdk(keyval, state);
            if (key == Key.None || !wants(key, mods)) return 0;
            Dispatcher.UIThread.Post(() => { if (live) pressed(key, mods); });
            return 1;
        };
        var id = g_signal_connect_data(view, "key-press-event", callback, IntPtr.Zero, IntPtr.Zero, 0);
        if (id == 0) { g_object_unref(view); throw new InvalidOperationException("Native key signal unavailable"); }
        return new WebKeySignal(() => { live = false; g_signal_handler_disconnect(view, id); g_object_unref(view); GC.KeepAlive(callback); });
    }

    // Only the keys a caller of this signal needs today; anything else stays with the page.
    internal static (Key, KeyModifiers) FromGdk(uint keyval, uint state) {
        var key = keyval switch { 0x030 => Key.D0, 0xffb0 => Key.NumPad0, _ => Key.None };   // GDK_KEY_0, GDK_KEY_KP_0
        var mods = KeyModifiers.None;
        if ((state & 1) != 0) mods |= KeyModifiers.Shift;      // GDK_SHIFT_MASK
        if ((state & 4) != 0) mods |= KeyModifiers.Control;    // GDK_CONTROL_MASK
        if ((state & 8) != 0) mods |= KeyModifiers.Alt;        // GDK_MOD1_MASK
        return (key, mods);
    }

    internal static (Key, KeyModifiers) FromVirtualKey(uint vk, bool ctrl, bool shift, bool alt) {
        var key = vk switch { 0x30 => Key.D0, 0x60 => Key.NumPad0, _ => Key.None };           // VK_0, VK_NUMPAD0
        var mods = KeyModifiers.None;
        if (shift) mods |= KeyModifiers.Shift;
        if (ctrl) mods |= KeyModifiers.Control;
        if (alt) mods |= KeyModifiers.Alt;
        return (key, mods);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IDisposable Windows(IWindowsWebView2PlatformHandle handle, Func<Key, KeyModifiers, bool> wants, Action<Key, KeyModifiers> pressed) {
        var controller = (ICoreWebView2Controller)Marshal.GetObjectForIUnknown(handle.CoreWebView2Controller);
        var sink = new AcceleratorSink(wants, pressed);
        controller.add_AcceleratorKeyPressed(sink, out var token);
        return new WebKeySignal(() => { sink.Live = false; controller.remove_AcceleratorKeyPressed(token); });
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class AcceleratorSink(Func<Key, KeyModifiers, bool> wants, Action<Key, KeyModifiers> pressed) : ICoreWebView2AcceleratorKeyPressedEventHandler {
        internal bool Live = true;
        public void Invoke(IntPtr sender, ICoreWebView2AcceleratorKeyPressedEventArgs args) {
            args.get_KeyEventKind(out var kind);
            if (!Live || (kind != 0 && kind != 2)) return;   // KeyDown, SystemKeyDown
            args.get_VirtualKey(out var vk);
            var (key, mods) = FromVirtualKey(vk, Down(0x11), Down(0x10), Down(0x12));   // VK_CONTROL, VK_SHIFT, VK_MENU
            if (key == Key.None || !wants(key, mods)) return;
            args.put_Handled(1);
            pressed(key, mods);   // WebView2 raises this on the UI thread
        }
        private static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;
    }

    // WebView2.idl vtable order; the slots before the accelerator pair are never called.
    [ComImport, Guid("4d00c0d1-9434-4eb6-8078-8697a560334f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICoreWebView2Controller {
        void _IsVisibleGet(); void _IsVisiblePut(); void _BoundsGet(); void _BoundsPut();
        void _ZoomGet(); void _ZoomPut(); void _ZoomChangedAdd(); void _ZoomChangedRemove();
        void _SetBoundsAndZoom(); void _MoveFocus(); void _MoveFocusAdd(); void _MoveFocusRemove();
        void _GotFocusAdd(); void _GotFocusRemove(); void _LostFocusAdd(); void _LostFocusRemove();
        void add_AcceleratorKeyPressed(ICoreWebView2AcceleratorKeyPressedEventHandler handler, out long token);
        void remove_AcceleratorKeyPressed(long token);
    }

    [ComImport, Guid("b29c7e28-fa79-41a8-8e44-65811c76dcb2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICoreWebView2AcceleratorKeyPressedEventHandler {
        void Invoke(IntPtr sender, ICoreWebView2AcceleratorKeyPressedEventArgs args);
    }

    [ComImport, Guid("9f760f8a-fb79-42be-9990-7b56900fa9c7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICoreWebView2AcceleratorKeyPressedEventArgs {
        void get_KeyEventKind(out int kind);
        void get_VirtualKey(out uint key);
        void _KeyEventLParam(); void _PhysicalKeyStatus(); void _HandledGet();
        void put_Handled(int handled);
    }

    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int KeyPress(IntPtr widget, IntPtr ev, IntPtr data);
    [DllImport("libgdk-3.so.0")] private static extern int gdk_event_get_keyval(IntPtr ev, out uint keyval);
    [DllImport("libgdk-3.so.0")] private static extern int gdk_event_get_state(IntPtr ev, out uint state);
    [DllImport("libgobject-2.0.so.0")] private static extern IntPtr g_object_ref(IntPtr view);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_object_unref(IntPtr view);
    [DllImport("libgobject-2.0.so.0")] private static extern ulong g_signal_connect_data(IntPtr view, [MarshalAs(UnmanagedType.LPUTF8Str)] string signal, KeyPress callback, IntPtr data, IntPtr destroy, int flags);
    [DllImport("libgobject-2.0.so.0")] private static extern void g_signal_handler_disconnect(IntPtr view, ulong id);
}
