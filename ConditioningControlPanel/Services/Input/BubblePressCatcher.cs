using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Helpers;

namespace ConditioningControlPanel.Services;

/// <summary>
/// ccp-bugs #1406: a press on a red (Natasha's favourite) bubble clicked whatever sat behind it.
///
/// <para>A red bubble is judged by how long the REAL button stays down, so the mouse hook must
/// let its press through (ccp-bugs #1342: a swallowed press is invisible to the system, and
/// counting the hook's button-ups instead proved unreliable). Let through, the press fell past
/// the click-through bubble host onto the app underneath.</para>
///
/// <para>So the press is still let through, but onto this: a near-invisible patch (alpha 1 of
/// 255, never activating) slid under the pointer from the hook callback, before Windows picks
/// the window the press goes to. The press and its release land here and nowhere else, and the
/// button state the bubble reads stays real. It hides on the release.</para>
///
/// <para>Fails safe: if the patch cannot be made or does not catch the press, the press simply
/// goes where it went before.</para>
/// </summary>
internal static class BubblePressCatcher
{
    private static NativeLayeredWindow? s_win;
    private static volatile bool s_gotDown;
    private static long s_shownAt;

    private const int WatchTimerId = 1;
    private const int WatchMs = 100;

    /// <summary>A patch that never received its press is taken away after this long.</summary>
    internal const int NoPressTimeoutMs = 400;

    /// <summary>Whatever happens, the patch never outlives this.</summary>
    internal const int HardTimeoutMs = 6000;

    /// <summary>The patch's handle while it exists (tests).</summary>
    internal static IntPtr Handle => s_win?.Handle ?? IntPtr.Zero;

    /// <summary>UI thread: make the patch (hidden). Idempotent.</summary>
    public static void EnsureCreated()
    {
        if (s_win != null && s_win.Handle != IntPtr.Zero) return;
        try
        {
            var win = new NativeLayeredWindow(0, 0, 0, clickThrough: false);
            if (win.Handle == IntPtr.Zero) { win.Dispose(); return; }
            win.SetAlpha(1);
            win.Message = OnMessage;
            s_win = win;
        }
        catch (Exception ex) { App.Logger?.Debug("Bubble press catcher: {E}", ex.Message); }
    }

    /// <summary>UI thread: take the patch away for good.</summary>
    public static void Destroy()
    {
        var win = s_win;
        s_win = null;
        try { win?.Dispose(); } catch { /* best effort */ }
    }

    /// <summary>HOOK CALLBACK: put the patch over this square (physical px, centre + half side)
    /// right now, so the press being decided lands on it. One synchronous call, no WPF.</summary>
    public static void Catch(double centerX, double centerY, double half)
    {
        var win = s_win;
        if (win == null || win.Handle == IntPtr.Zero) return;
        try
        {
            int side = Math.Max(8, (int)Math.Ceiling(half * 2));
            s_gotDown = false;
            s_shownAt = Environment.TickCount64;
            win.ShowAt((int)Math.Floor(centerX - side / 2.0), (int)Math.Floor(centerY - side / 2.0), side, side);
            SetTimer(win.Handle, (IntPtr)WatchTimerId, WatchMs, IntPtr.Zero);
        }
        catch { /* the press goes where it went before */ }
    }

    /// <summary>Should a showing patch be taken away now? Pure, so the rule has a test.</summary>
    internal static bool ShouldRelease(bool gotDown, bool buttonHeld, long shownForMs)
    {
        if (shownForMs >= HardTimeoutMs) return true;
        if (!gotDown) return shownForMs >= NoPressTimeoutMs;   // the press went elsewhere
        return !buttonHeld;                                     // released (even if the up was lost)
    }

    private static IntPtr? OnMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case NativeLayeredWindow.WM_LBUTTONDOWN:
            case NativeLayeredWindow.WM_RBUTTONDOWN:
                // Keep the release too, wherever the pointer has drifted by then.
                s_gotDown = true;
                SetCapture(hwnd);
                return IntPtr.Zero;

            case NativeLayeredWindow.WM_LBUTTONUP:
            case NativeLayeredWindow.WM_RBUTTONUP:
                Release(hwnd);
                return IntPtr.Zero;

            case NativeLayeredWindow.WM_TIMER:
                if (wParam == (IntPtr)WatchTimerId
                    && ShouldRelease(s_gotDown, BubbleService.AnyPointerButtonHeld(), Environment.TickCount64 - s_shownAt))
                    Release(hwnd);
                return IntPtr.Zero;
        }
        return null;
    }

    private static void Release(IntPtr hwnd)
    {
        try
        {
            KillTimer(hwnd, (IntPtr)WatchTimerId);
            if (GetCapture() == hwnd) ReleaseCapture();
        }
        catch { /* best effort */ }
        s_win?.Hide();
    }

    [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetCapture();
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, IntPtr id);
}
