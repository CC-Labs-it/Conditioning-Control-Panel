using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The away watcher on this head (WPF Services/Fyp/Online/ScrolllerAwayWatcher.cs, started App.xaml.cs:2587, stopped
/// :6308): no FRESH Scrolller fetch while nobody can be watching; the rule is Core <see cref="ScrolllerAwayRule"/>.
/// <para>Windows: a message-only window on the UI thread (Avalonia's dispatcher pumps every window of its thread) gets
/// the session lock / unlock (WTSRegisterSessionNotification), the display state (GUID_CONSOLE_DISPLAY_STATE, as WPF)
/// and suspend / resume.</para>
/// <para>Linux: logind on the system bus, through Tmds.DBus.Protocol (already shipped with Avalonia.FreeDesktop and used
/// by PortalPanicShortcut, so nothing new is added): Manager.PrepareForSleep, the caller's Session Lock / Unlock signals
/// and its LockedHint property (what GNOME and KDE set while the lock screen is up). logind reports no display power
/// state; a blanked screen behind a lock is caught by the lock.</para>
/// Start on the UI thread at launch, Stop at exit. Never throws; a hook that cannot be had is logged and skipped.
/// </summary>
internal static class ScrolllerAwayHost
{
    internal static readonly ScrolllerAwayTracker Tracker = new();
    private static int _started;

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        try
        {
            if (OperatingSystem.IsWindows()) Win32.Start();
            else if (OperatingSystem.IsLinux()) _ = Logind.StartAsync();
        }
        catch (Exception ex) { Log.Debug("ScrolllerAway: no hooks: {E}", ex.Message); }
    }

    public static void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;
        try
        {
            if (OperatingSystem.IsWindows()) Win32.Stop();
            else if (OperatingSystem.IsLinux()) Logind.Stop();
        }
        catch (Exception ex) { Log.Debug("ScrolllerAway: stop failed: {E}", ex.Message); }
        Tracker.Reset();
    }

    // Share the existing OS hooks with mandatory video. Display-off alone is not a WPF cleanup signal.
    internal static void SetLocked(bool locked)
    {
        Tracker.SetLocked(locked);
        if (locked) Views.Overlays.MandatoryVideoOverlay.Instance.SystemUnavailable();
    }

    internal static void SetSleeping(bool sleeping)
    {
        Tracker.SetSleeping(sleeping);
        if (sleeping) Views.Overlays.MandatoryVideoOverlay.Instance.SystemUnavailable();
    }

    // ---- Windows -------------------------------------------------------------------------

    internal static class Win32
    {
        internal const uint WM_WTSSESSION_CHANGE = 0x02B1, WM_POWERBROADCAST = 0x0218;
        internal const int WTS_SESSION_LOCK = 7, WTS_SESSION_UNLOCK = 8;
        internal const int PBT_APMSUSPEND = 0x4, PBT_APMRESUMESUSPEND = 0x7, PBT_APMRESUMEAUTOMATIC = 0x12, PBT_POWERSETTINGCHANGE = 0x8013;
        internal static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
        /// <summary>POWERBROADCAST_SETTING: a GUID (16 bytes), DataLength (4), then Data.</summary>
        private const int DataOffset = 20;
        private const string ClassName = "CCP.Avalonia.ScrolllerAway";

        private static WndProc? _proc;   // kept alive: the window calls it
        private static IntPtr _hwnd, _notify, _suspend;

        /// <summary>True once the window exists and all three notifications are registered (tests).</summary>
        internal static bool Hooked => _hwnd != IntPtr.Zero && _notify != IntPtr.Zero && _suspend != IntPtr.Zero;

        internal static void Start()
        {
            _proc = Proc;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = GetModuleHandle(null),
                lpszClassName = ClassName,
            };
            RegisterClassEx(ref wc);   // 0 when already registered by an earlier Start: CreateWindowEx still finds it
            // A message-only window: never shown, no taskbar entry, only the notifications it registers for.
            _hwnd = CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, new IntPtr(-3), IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            if (_hwnd == IntPtr.Zero) { Log.Debug("ScrolllerAway: no message window ({Err})", Marshal.GetLastWin32Error()); return; }
            if (!WTSRegisterSessionNotification(_hwnd, 0)) Log.Debug("ScrolllerAway: no session events ({Err})", Marshal.GetLastWin32Error());
            var guid = ConsoleDisplayState;
            _notify = RegisterPowerSettingNotification(_hwnd, ref guid, 0);
            if (_notify == IntPtr.Zero) Log.Debug("ScrolllerAway: no display state ({Err})", Marshal.GetLastWin32Error());
            // A message-only window gets no broadcast PBT_APMSUSPEND / RESUME: suspend and resume have to be asked for.
            _suspend = RegisterSuspendResumeNotification(_hwnd, 0);
            if (_suspend == IntPtr.Zero) Log.Debug("ScrolllerAway: no suspend events ({Err})", Marshal.GetLastWin32Error());
        }

        internal static void Stop()
        {
            if (_notify != IntPtr.Zero) { try { UnregisterPowerSettingNotification(_notify); } catch { } _notify = IntPtr.Zero; }
            if (_suspend != IntPtr.Zero) { try { UnregisterSuspendResumeNotification(_suspend); } catch { } _suspend = IntPtr.Zero; }
            if (_hwnd != IntPtr.Zero)
            {
                try { WTSUnRegisterSessionNotification(_hwnd); } catch { }
                try { DestroyWindow(_hwnd); } catch { }
                _hwnd = IntPtr.Zero;
            }
        }

        private static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            try { Handle(msg, wParam, lParam); } catch { }
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        /// <summary>One window message into the tracker (tests drive it).</summary>
        internal static void Handle(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_WTSSESSION_CHANGE)
            {
                var reason = wParam.ToInt64();
                if (reason == WTS_SESSION_LOCK) SetLocked(true);
                else if (reason == WTS_SESSION_UNLOCK) SetLocked(false);
                return;
            }
            if (msg != WM_POWERBROADCAST) return;
            switch (wParam.ToInt64())
            {
                case PBT_APMSUSPEND: SetSleeping(true); break;
                case PBT_APMRESUMESUSPEND:
                case PBT_APMRESUMEAUTOMATIC: SetSleeping(false); break;
                case PBT_POWERSETTINGCHANGE when lParam != IntPtr.Zero:
                    var setting = Marshal.PtrToStructure<Guid>(lParam);
                    if (setting == ConsoleDisplayState && Marshal.ReadInt32(lParam, 16) >= 4)
                        Tracker.SetDisplay(Marshal.ReadInt32(lParam, DataOffset));
                    break;
            }
        }

        private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize, style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
        private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
        private static extern IntPtr GetModuleHandle(string? name);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);

        [DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterSuspendResumeNotification(IntPtr recipient, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterSuspendResumeNotification(IntPtr handle);
    }

    // ---- Linux (logind) ------------------------------------------------------------------

    internal static class Logind
    {
        private const string Service = "org.freedesktop.login1", ManagerPath = "/org/freedesktop/login1";
        private const string ManagerIface = "org.freedesktop.login1.Manager", SessionIface = "org.freedesktop.login1.Session";
        private const string PropsIface = "org.freedesktop.DBus.Properties";

        private static DBusConnection? _bus;

        internal static async Task StartAsync()
        {
            try
            {
                var bus = _bus = new DBusConnection(DBusAddress.System!);
                await bus.ConnectAsync();
                await bus.AddMatchAsync(
                    new MatchRule { Type = MessageType.Signal, Interface = ManagerIface, Member = "PrepareForSleep", Path = ManagerPath },
                    (Message m, object? _) => m.GetBodyReader().ReadBool(),
                    (Exception? ex, bool sleeping, object? _, object? _) => { if (ex == null) SetSleeping(sleeping); },
                    null, null, false, ObserverFlags.None);

                var session = await SessionPathAsync(bus);
                if (session == null) { Log.Information("ScrolllerAway: no logind session for this process; sleep only"); return; }
                foreach (var (member, locked) in new[] { ("Lock", true), ("Unlock", false) })
                    await bus.AddMatchAsync(
                        new MatchRule { Type = MessageType.Signal, Interface = SessionIface, Member = member, Path = session },
                        (Message _, object? _) => locked,
                        (Exception? ex, bool l, object? _, object? _) => { if (ex == null) SetLocked(l); },
                        null, null, false, ObserverFlags.None);
                await bus.AddMatchAsync(
                    new MatchRule { Type = MessageType.Signal, Interface = PropsIface, Member = "PropertiesChanged", Path = session, Arg0 = SessionIface },
                    (Message m, object? _) => LockedHintIn(m),
                    (Exception? ex, bool? hint, object? _, object? _) => { if (ex == null && hint is bool l) SetLocked(l); },
                    null, null, false, ObserverFlags.None);

                // Started while the lock screen is already up (autostart behind a locked session).
                var start = await bus.CallMethodAsync(Call(bus, session, PropsIface, "Get", "ss", (ref MessageWriter w) =>
                    { w.WriteString(SessionIface); w.WriteString("LockedHint"); }),
                    (Message m, object? _) => Unwrap(m.GetBodyReader().ReadVariantValue()).GetBool(), null);
                SetLocked(start);
                Log.Information("ScrolllerAway: watching logind session {Session}", session);
            }
            catch (Exception ex) { Log.Information("ScrolllerAway: logind unavailable ({E}); no away state on this desktop", ex.Message); }
        }

        internal static void Stop()
        {
            var bus = _bus;
            _bus = null;
            try { bus?.Dispose(); } catch { }
        }

        /// <summary>This process's session: GetSessionByPID, else the caller's own ("auto"); null when it has none.</summary>
        private static async Task<string?> SessionPathAsync(DBusConnection bus)
        {
            try
            {
                return await bus.CallMethodAsync(Call(bus, ManagerPath, ManagerIface, "GetSessionByPID", "u",
                    (ref MessageWriter w) => w.WriteUInt32((uint)Environment.ProcessId)),
                    (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null);
            }
            catch
            {
                try
                {
                    return await bus.CallMethodAsync(Call(bus, ManagerPath, ManagerIface, "GetSession", "s",
                        (ref MessageWriter w) => w.WriteString("auto")),
                        (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null);
                }
                catch { return null; }
            }
        }

        /// <summary>LockedHint from one PropertiesChanged(s a{sv} as), or null when it did not change.</summary>
        private static bool? LockedHintIn(Message m)
        {
            var r = m.GetBodyReader();
            if (r.ReadString() != SessionIface) return null;
            var changed = r.ReadDictionaryOfStringToVariantValue();
            return changed.TryGetValue("LockedHint", out var v) && Unwrap(v).Type == VariantValueType.Bool ? Unwrap(v).GetBool() : null;
        }

        private static VariantValue Unwrap(VariantValue v) => v.Type == VariantValueType.Variant ? v.GetVariantValue() : v;

        private delegate void Body(ref MessageWriter w);

        private static MessageBuffer Call(DBusConnection bus, string path, string iface, string member, string? signature, Body? body)
        {
            var w = bus.GetMessageWriter();
            w.WriteMethodCallHeader(destination: Service, path: path, @interface: iface, member: member, signature: signature);
            body?.Invoke(ref w);
            return w.CreateMessage();
        }
    }
}
