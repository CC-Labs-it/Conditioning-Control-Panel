using System;
using System.Runtime.InteropServices;
using Avalonia;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class Win32Pointer
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    internal delegate bool CursorReader(out Point point);

    internal static (PixelPoint At, bool Pressed, bool Escape)? Read()
    {
        try { return Read(GetCursorPos, GetAsyncKeyState); }
        catch (Exception) { return null; }
    }

    internal static (PixelPoint At, bool Pressed, bool Escape)? Read(CursorReader cursor, Func<int, short> key)
    {
        if (!cursor(out var point)) return null;
        return (new PixelPoint(point.X, point.Y),
            ((key(1) | key(2)) & 0x8000) != 0, (key(0x1b) & 0x8000) != 0);
    }
}
