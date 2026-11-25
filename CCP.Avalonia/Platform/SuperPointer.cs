using System;
using System.Runtime.InteropServices;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Super stage's two pointer seams on this head (Core <c>SuperStageHost.CursorProvider</c> and
/// <c>ClickProvider</c>), mirroring WPF <c>Services/Super/SuperStageWpf.cs</c>: the cursor in
/// virtual-desktop device pixels, and the left-or-right button as (held now, pressed since the last
/// read). Both only look; nothing is grabbed, so a click still lands where it was aimed.
///
/// <para><b>Windows</b> reads exactly what WPF reads: <c>GetCursorPos</c> for the cursor and
/// <c>GetAsyncKeyState</c> on both buttons, whose low bit carries a tap shorter than one stage step.
/// The cursor read deliberately avoids <see cref="Win32Pointer.Read()"/>: that one also calls
/// <c>GetAsyncKeyState</c>, and the stage reads the cursor just before the click, so it would
/// clear the tap bit first.</para>
///
/// <para><b>Linux</b> reads <see cref="X11Pointer"/> (X11 and XWayland surfaces only). X has no
/// "pressed since" latch, so Tapped is always false there and a press shorter than one 33 ms step
/// can be missed; under Wayland a pointer over a native Wayland window is not seen at all.</para>
/// Null on any other platform or when no desktop pointer is readable. UI thread.
/// </summary>
internal static class SuperPointer
{
    private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

    internal static (int X, int Y)? Cursor()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return Win32Overlay.CursorPosition() is { } p ? (p.X, p.Y) : null;
            if (OperatingSystem.IsLinux()) return X11Pointer.Read(out _) is { } x ? (x.At.X, x.At.Y) : null;
        }
        catch (Exception ex) { Serilog.Log.Debug("SuperPointer: cursor read failed: {E}", ex.Message); }
        return null;
    }

    internal static (bool Down, bool Tapped)? Click()
    {
        try
        {
            if (OperatingSystem.IsWindows()) return FromKeys(GetAsyncKeyState);
            if (OperatingSystem.IsLinux()) return FromX11(X11Pointer.Read(out _)?.Pressed);
        }
        catch (Exception ex) { Serilog.Log.Debug("SuperPointer: click read failed: {E}", ex.Message); }
        return null;
    }

    /// <summary>WPF's rule, pure for tests: either button's high bit is held, either low bit is a tap.</summary>
    internal static (bool Down, bool Tapped) FromKeys(Func<int, short> key)
    {
        short l = key(VK_LBUTTON), r = key(VK_RBUTTON);
        return (((l | r) & 0x8000) != 0, ((l | r) & 0x0001) != 0);
    }

    /// <summary>X11 only knows the held state (Button1 or Button3 in the pointer mask).</summary>
    internal static (bool Down, bool Tapped)? FromX11(bool? pressed) => pressed is { } p ? (p, false) : null;
}
