using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia;

/// <summary>Windows desktop check for the passive popup Loaded path. Exit 2 means only the
/// z-order assertion failed, and 3 means the popup took focus. CI tests both failure paths.</summary>
internal static class PassivePopupCheck
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern uint SetWindowLong(IntPtr hwnd, int index, uint value);

    private static bool Above(IntPtr upper, IntPtr lower)
    {
        for (var w = GetWindow(lower, 3); w != IntPtr.Zero; w = GetWindow(w, 3)) // GW_HWNDPREV
            if (w == upper) return true;
        return false;
    }

    // A desktop lifetime for this check must not start App's shell, gates, services or windows.
    private sealed class ProbeApp : Application { public ProbeApp() { } }

    internal static int Run(bool suppressRaise, bool stealFocus)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("FAIL: Windows desktop required"); return 1; }
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppBuilder.Configure<ProbeApp>().UsePlatformDetect().With(Program.Win32Options())
            .SetupWithLifetime(lifetime);
        int result = 1;
        var foreground = new Window { Width = 200, Height = 100, Title = "Passive popup focus check" };
        var other = new Window { Width = 200, Height = 100, Topmost = true, ShowActivated = false, Title = "Other topmost" };
        var popup = new Window { Width = 200, Height = 100, Topmost = true, ShowActivated = false, ShowInTaskbar = false, WindowDecorations = WindowDecorations.None };
        var fg = foreground.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var otherHwnd = other.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        var popupHwnd = popup.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        bool setup = false;
        IntPtr before = IntPtr.Zero;

        // Registered before PlacePassive: force the other topmost window above the shown popup
        // at Loaded, so only the production Loaded handler can restore the requested ordering.
        popup.Loaded += (_, _) =>
        {
            before = GetForegroundWindow();
            bool handles = fg != IntPtr.Zero && otherHwnd != IntPtr.Zero && popupHwnd != IntPtr.Zero;
            bool visible = handles && IsWindowVisible(otherHwnd) && IsWindowVisible(popupHwnd);
            uint popupStyle = handles ? Win32Overlay.GetWindowLong(popupHwnd, -20) : 0;
            uint otherStyle = handles ? Win32Overlay.GetWindowLong(otherHwnd, -20) : 0;
            bool placed = handles && SetWindowPos(otherHwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13); // NOMOVE | NOSIZE | NOACTIVATE
            bool initiallyAbove = placed && Above(otherHwnd, popupHwnd);
            bool focusReady = handles && before == fg && GetForegroundWindow() == before;
            setup = handles && visible && focusReady && initiallyAbove
                && (popupStyle & (Win32Overlay.WsExTopmost | Win32Overlay.WsExNoActivate))
                    == (Win32Overlay.WsExTopmost | Win32Overlay.WsExNoActivate)
                && (otherStyle & Win32Overlay.WsExTopmost) != 0;
            Console.WriteLine($"handles={handles} visible={visible} focus-ready={focusReady} " +
                $"other-raised={placed} other-above={initiallyAbove} popup-style=0x{popupStyle:x} other-style=0x{otherStyle:x}");
        };
        Action<Window>? raise = suppressRaise ? _ => { } : stealFocus ? w =>
        {
            X11Overlay.Raise(w);
            SetWindowLong(popupHwnd, -20, Win32Overlay.GetWindowLong(popupHwnd, -20) & ~Win32Overlay.WsExNoActivate);
            SetForegroundWindow(popupHwnd);
        } : null;
        AchievementPopup.PlacePassive(popup, 20, raise);
        popup.Loaded += (_, _) =>
        {
            var sameForeground = before != IntPtr.Zero && GetForegroundWindow() == before;
            var raised = Above(popupHwnd, otherHwnd);
            Console.WriteLine($"setup={setup} foreground-preserved={sameForeground} popup-above-topmost={raised}");
            result = !setup ? 1 : !sameForeground ? (raised && GetForegroundWindow() == popupHwnd ? 3 : 1) : raised ? 0 : 2;
            lifetime.Shutdown();
        };

        foreground.Show();
        other.Show();
        DispatcherTimer.RunOnce(() =>
        {
            foreground.Activate();
            SetForegroundWindow(fg);
            popup.Show();
        }, TimeSpan.FromMilliseconds(300));
        DispatcherTimer.RunOnce(() => { Console.Error.WriteLine("FAIL: popup did not load"); lifetime.Shutdown(); }, TimeSpan.FromSeconds(10));
        lifetime.Start(Array.Empty<string>());
        Console.WriteLine(result == 0 ? "PASS" : $"FAIL ({result})");
        return result;
    }
}
