using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Launcher;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// ccp-bugs #1548 (7.2.1, three monitors 2560x1440 + 2x 1920x1080): the launcher's WHOLE client
/// area stayed one flat colour (#0B0710, not even the window's own #121220 background) inside the
/// Windows 11 accent border, while every button still answered clicks where it should be. So the
/// visual tree, layout and hit testing were alive and only this HWND's hardware render target had
/// stopped presenting: first after the open-time grow (a resize that rebuilds the swap chain, then
/// a separate move), and from scratch after the window was hidden for Piece by Piece and shown
/// again. Opacity fixes cannot reach that (the settle pass from #4104 forced tiles visible into a
/// surface nobody presented).
///
/// <para>Three answers, in order of cost:</para>
/// <list type="number">
/// <item>The grow is one SetWindowPos in physical pixels inside one monitor's work area
/// (<see cref="LauncherPlacement.GrowWithin"/>), never Height then Top in DIPs.</item>
/// <item>After the grow, on every show but the first, and after a DPI change, the render target
/// is rebuilt: the HwndTarget goes software for one beat and back, which makes WPF throw the old
/// device surface away. Runs under the fade-in, so nobody sees the beat.</item>
/// <item>The settle pass reads nine screen pixels inside the launcher when it is in front. One
/// flat colour = still not presenting: the window stays on software rendering for the rest of the
/// run and logs it. Every settle logs monitor, DPI, render mode, tier and DWM cloak state.</item>
/// </list>
/// </summary>
public partial class LauncherWindow
{
    private const int RenderKickHoldMs = 120;

    /// <summary>Set once the probe saw a flat window: hardware rendering is off for this run.</summary>
    private bool _softwareForRun;
    private DispatcherTimer? _renderKickBack;
    private string _lastKick = "none";

    /// <summary>
    /// Drop and rebuild this window's render target. Software for <see cref="RenderKickHoldMs"/>,
    /// then back to the default (hardware where the tier allows it).
    /// </summary>
    private void KickRenderTarget(string why)
    {
        try
        {
            var target = CompositionTargetOrNull();
            if (target == null) return;
            _lastKick = why;
            if (_softwareForRun) { target.RenderMode = RenderMode.SoftwareOnly; return; }
            if (target.RenderMode != RenderMode.SoftwareOnly) target.RenderMode = RenderMode.SoftwareOnly;

            _renderKickBack ??= new DispatcherTimer(DispatcherPriority.Normal, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(RenderKickHoldMs),
            };
            _renderKickBack.Tick -= RenderKickBack_Tick;
            _renderKickBack.Tick += RenderKickBack_Tick;
            _renderKickBack.Stop();
            _renderKickBack.Start();
            InvalidateVisual();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] render target kick failed ({Why})", why); }
    }

    private void RenderKickBack_Tick(object? sender, EventArgs e)
    {
        _renderKickBack?.Stop();
        try
        {
            if (_softwareForRun) return;
            var target = CompositionTargetOrNull();
            if (target != null) target.RenderMode = RenderMode.Default;
            InvalidateVisual();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] render target restore failed"); }
    }

    private HwndTarget? CompositionTargetOrNull()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return null;
        return HwndSource.FromHwnd(hwnd)?.CompositionTarget;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Log.Information("[Launcher] DPI {Old:0.##}x -> {New:0.##}x on {Monitor}",
            oldDpi.DpiScaleX, newDpi.DpiScaleX, DescribeMonitor());
        if (IsVisible) KickRenderTarget("dpi");
    }

    /// <summary>
    /// The one-time grow, in pixels, in one move, inside the work area of the monitor the window
    /// is on. False when it could not be placed that way (the caller then leaves the size alone).
    /// </summary>
    private bool PlaceGrown(double grownDip, out PxRect placed)
    {
        placed = default;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var wr)) return false;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        if (screen == null) return false;
        var wa = screen.WorkingArea;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;

        var window = new PxRect(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top);
        var work = new PxRect(wa.Left, wa.Top, wa.Width, wa.Height);
        placed = LauncherPlacement.GrowWithin(window, work, LauncherPlacement.ToPx(grownDip, scale));
        if (placed == window) return false;
        return SetWindowPos(hwnd, IntPtr.Zero, placed.Left, placed.Top, placed.Width, placed.Height,
            SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    /// <summary>
    /// The settle pass's second half: when the launcher is the foreground window and nothing is
    /// dimmed or veiled, read the screen behind its probe points. Returns the log line's tail.
    /// </summary>
    private string ProbePaint(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "probe skipped (no hwnd)";
        if (GetForegroundWindow() != hwnd) return "probe skipped (not in front)";
        if (LockdownVeil.Visibility == Visibility.Visible) return "probe skipped (veil)";
        if (!GetWindowRect(hwnd, out var wr)) return "probe skipped (no rect)";

        var pts = LauncherPlacement.ProbePoints(new PxRect(wr.Left, wr.Top, wr.Right - wr.Left, wr.Bottom - wr.Top));
        var rgb = new List<int>(pts.Count);
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return "probe skipped (no screen dc)";
        try
        {
            foreach (var (x, y) in pts)
            {
                uint c = GetPixel(dc, x, y);
                if (c == 0xFFFFFFFF) continue;   // CLR_INVALID: off any monitor
                // COLORREF is 0x00BBGGRR.
                rgb.Add((int)(((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF)));
            }
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        if (!LauncherPlacement.LooksBlank(rgb)) return $"probe painted ({rgb.Count} samples)";

        string flat = rgb.Count > 0 ? $"#{rgb[0]:X6}" : "?";
        if (_softwareForRun)
            return $"probe BLANK again at {flat} on software rendering";

        _softwareForRun = true;
        try
        {
            _renderKickBack?.Stop();
            var target = HwndSource.FromHwnd(hwnd)?.CompositionTarget;
            if (target != null) target.RenderMode = RenderMode.SoftwareOnly;
            InvalidateVisual();
            RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwAllChildren | RdwUpdateNow | RdwFrame);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] software fallback failed"); }
        ScheduleTileSettle("after-software");
        return $"probe BLANK ({rgb.Count} samples flat at {flat}): software rendering for the rest of this run";
    }

    /// <summary>Monitor, work area and DPI for the log line.</summary>
    private string DescribeMonitor()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return "no hwnd";
            var s = System.Windows.Forms.Screen.FromHandle(hwnd);
            var dpi = VisualTreeHelper.GetDpi(this);
            string rect = GetWindowRect(hwnd, out var wr)
                ? $"{wr.Left},{wr.Top} {wr.Right - wr.Left}x{wr.Bottom - wr.Top}px"
                : "?";
            var wa = s.WorkingArea;
            return $"{s.DeviceName} ({s.Bounds.Width}x{s.Bounds.Height}, work {wa.Left},{wa.Top} {wa.Width}x{wa.Height}) " +
                   $"at {dpi.DpiScaleX * 100:0}%, window {rect}";
        }
        catch (Exception ex) { return "monitor ? (" + ex.GetType().Name + ")"; }
    }

    /// <summary>DWMWA_CLOAKED: 0 = shown, else the cloak reason bits (1 app, 2 shell, 4 inherited).</summary>
    private static string CloakState(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return "?";
            return DwmGetWindowAttribute(hwnd, DwmwaCloaked, out int cloaked, sizeof(int)) == 0
                ? (cloaked == 0 ? "uncloaked" : $"CLOAKED({cloaked})")
                : "?";
        }
        catch { return "?"; }
    }

    private const int DwmwaCloaked = 14;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
