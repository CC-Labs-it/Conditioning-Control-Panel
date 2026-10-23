using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// Sets <see cref="ScrolllerPolicy.Away"/> while nobody can be watching (owner, 2026-10-19: no
/// fresh Scrolller fetch for an empty room): the session is locked, or the display is switched
/// off (GUID_CONSOLE_DISPLAY_STATE; a dimmed display still counts as watched). NEVER on minimise
/// or on focus: sessions run minimised and media plays over other apps. Cached media keeps
/// playing while away. Start on the UI thread at launch, Stop at exit. Never throws.
/// </summary>
internal static class ScrolllerAwayWatcher
{
    private static readonly Guid ConsoleDisplayState = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;
    /// <summary>POWERBROADCAST_SETTING: a GUID (16 bytes), DataLength (4), then Data.</summary>
    private const int DataOffset = 20;

    /// <summary>GUID_CONSOLE_DISPLAY_STATE values (Core <see cref="ScrolllerAwayRule"/>, shared with the Avalonia head).</summary>
    internal const int DisplayOff = ScrolllerAwayRule.DisplayOff, DisplayOn = ScrolllerAwayRule.DisplayOn,
        DisplayDimmed = ScrolllerAwayRule.DisplayDimmed;

    private static int _started;
    private static volatile bool _locked;
    private static volatile int _display = DisplayOn;
    private static HwndSource? _sink;
    private static IntPtr _notify;

    /// <summary>The rule, pure (Core <see cref="ScrolllerAwayRule.IsAway"/>): away = locked, or the display is off. Dimmed is not away.</summary>
    internal static bool IsAway(bool locked, int displayState) => ScrolllerAwayRule.IsAway(locked, displayState);

    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        try { SystemEvents.SessionSwitch += OnSessionSwitch; }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAway: no session events: {E}", ex.Message); }
        try
        {
            // A message-only window: no taskbar entry, never shown, only receives broadcasts.
            var p = new HwndSourceParameters("CCP.ScrolllerAway") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
            _sink = new HwndSource(p);
            _sink.AddHook(WndProc);
            var guid = ConsoleDisplayState;
            _notify = RegisterPowerSettingNotification(_sink.Handle, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);
        }
        catch (Exception ex) { App.Logger?.Debug("ScrolllerAway: no display state: {E}", ex.Message); }
    }

    public static void Stop()
    {
        if (Interlocked.Exchange(ref _started, 0) == 0) return;
        try { SystemEvents.SessionSwitch -= OnSessionSwitch; } catch { }
        try { if (_notify != IntPtr.Zero) UnregisterPowerSettingNotification(_notify); } catch { }
        _notify = IntPtr.Zero;
        try { _sink?.Dispose(); } catch { }
        _sink = null;
    }

    private static void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) _locked = true;
        else if (e.Reason == SessionSwitchReason.SessionUnlock) _locked = false;
        else return;
        Apply();
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            if (msg == WM_POWERBROADCAST && wParam.ToInt64() == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                var setting = Marshal.PtrToStructure<Guid>(lParam);
                var length = Marshal.ReadInt32(lParam, 16);
                if (setting == ConsoleDisplayState && length >= 4)
                {
                    _display = Marshal.ReadInt32(lParam, DataOffset);
                    Apply();
                }
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    private static void Apply()
    {
        bool away = IsAway(_locked, _display);
        if (away == ScrolllerPolicy.Away) return;
        ScrolllerPolicy.Away = away;
        App.Logger?.Information("ScrolllerAway: {State} (locked={Locked}, display={Display})",
            away ? "away, no fresh Scrolller fetch" : "back", _locked, _display);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid powerSettingGuid, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}
