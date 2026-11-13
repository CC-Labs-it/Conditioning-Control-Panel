using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Platform;

// Avalonia 12.1.x (fields checked on 12.1.5) owns the tray icon and its click/menu handling. Only modify its balloon fields.
internal sealed class WindowsNotifications : IDisposable
{
    internal const uint Modify = 1;
    private const uint NifInfo = 16;
    private readonly Func<uint, NotifyIconData, bool> _notify;
    private readonly Action _clear;
    private readonly IntPtr _window;
    private readonly uint _id;
    private bool _shown;

    internal WindowsNotifications(IntPtr window, uint id, Func<uint, NotifyIconData, bool> notify, Action clear)
    {
        _window = window;
        _id = id;
        _notify = notify;
        _clear = clear;
    }

    internal static WindowsNotifications? Create(TrayIcon? tray)
    {
        if (tray?.IsVisible != true) return null;
        // Avalonia has no public API for its native tray identity. Fail closed if the pinned
        // implementation changes, so the caller uses its ordinary toast/drop fallback.
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var impl = typeof(TrayIcon).GetField("_impl", instance)?.GetValue(tray);
        var type = impl?.GetType();
        if (type?.FullName != "Avalonia.Win32.TrayIconImpl" ||
            type.GetField("_iconAdded", instance)?.GetValue(impl) is not true ||
            type.GetField("_disposedValue", instance)?.GetValue(impl) is not false ||
            type.GetField("_uniqueId", instance)?.GetValue(impl) is not int id || id <= 0)
            return null;
        var platform = type.Assembly.GetType("Avalonia.Win32.Win32Platform");
        var current = platform?.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
        var handle = platform?.GetProperty("Handle", instance)?.GetValue(current);
        if (handle is not IntPtr hwnd || hwnd == IntPtr.Zero) return null;
        return new WindowsNotifications(hwnd, (uint)id, (command, data) => ShellNotifyIcon(command, ref data), () =>
        {
            // WPF hides the icon to clear a balloon, then shows it again if needed. Avalonia's
            // tray stays visible for its panic menu, so restore its previous visibility.
            if (!tray.IsVisible || type.GetField("_disposedValue", instance)?.GetValue(impl) is not false) return;
            try { tray.IsVisible = false; }
            finally { tray.IsVisible = true; }
        });
    }

    internal bool Show(string title, string body)
    {
        if (_window == IntPtr.Zero || _id == 0) return false;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = _id,
            Flags = NifInfo, Tip = "", Info = Limit(body, 255), InfoTitle = Limit(title, 63),
            InfoFlags = 1 // NIIF_INFO
        };
        if (!_notify(Modify, data)) return false;
        _shown = true;
        return true;
    }

    private static string Limit(string value, int length)
    {
        if (value.Length <= length) return value;
        // Never cut a surrogate pair in half.
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value.Substring(0, length);
    }

    public void Dispose()
    {
        if (!_shown) return;
        _shown = false;
        _clear();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public IntPtr BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint command, ref NotifyIconData data);
}
