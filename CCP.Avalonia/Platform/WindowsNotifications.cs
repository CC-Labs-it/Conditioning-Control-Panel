using System;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Platform;

// Shell_NotifyIcon owns the icon until it is deleted. All calls are made on the UI thread.
internal sealed class WindowsNotifications : IDisposable
{
    internal const uint Add = 0, Modify = 1, Delete = 2;
    private const uint Icon = 2, Tip = 4, Info = 16;
    private readonly Func<uint, NotifyIconData, bool> _notify;
    private readonly IntPtr _window;
    private readonly IntPtr _icon;
    private bool _added;
    private DispatcherTimer? _timer;

    internal WindowsNotifications(IntPtr window, IntPtr icon, Func<uint, NotifyIconData, bool> notify)
    {
        _window = window;
        _icon = icon;
        _notify = notify;
    }

    internal static WindowsNotifications Create(IntPtr window) =>
        new(window, LoadIcon(IntPtr.Zero, (IntPtr)32512), (command, data) => ShellNotifyIcon(command, ref data));

    internal bool Show(string title, string body)
    {
        if (_window == IntPtr.Zero || _icon == IntPtr.Zero) return false;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 0xCC02,
            Flags = Icon | Tip, Icon = _icon, Tip = "Conditioning Control Panel",
            Info = "", InfoTitle = ""
        };
        if (!_added)
        {
            if (!_notify(Add, data)) return false;
            _added = true;
        }
        data.Flags = Info;
        data.InfoTitle = Limit(title, 63);
        data.Info = Limit(body, 255);
        data.InfoFlags = 1; // NIIF_INFO
        try
        {
            if (!_notify(Modify, data)) { Dispose(); return false; }
        }
        catch { Dispose(); throw; }
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => Dispose();
        _timer.Start();
        return true;
    }

    private static string Limit(string value, int length) => value.Length <= length ? value : value.Substring(0, length);

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        if (!_added) return;
        _added = false;
        var data = new NotifyIconData { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 0xCC02, Tip = "", Info = "", InfoTitle = "" };
        _notify(Delete, data);
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

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);
}
