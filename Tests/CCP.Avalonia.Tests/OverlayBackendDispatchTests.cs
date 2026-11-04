using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The overlay entry points (X11Overlay.SetClickThrough/SetOverrideRedirect/SetOpacity) pick their
/// backend from the window's platform-handle descriptor alone. Fake handles stand in for real
/// windows; the Win32 half itself only runs on Windows (--overlay-check in the windows CI job).
/// </summary>
public sealed class OverlayBackendDispatchTests
{
    [Theory]
    [InlineData("HWND", 42, "Win32")]
    [InlineData("XID", 42, "X11")]
    [InlineData("HWND", 0, "None")]   // not opened yet
    [InlineData("XID", 0, "None")]
    [InlineData("NSWindow", 42, "None")]
    public void Descriptor_PicksBackend(string descriptor, long handle, string expected)
        => Assert.Equal(expected, X11Overlay.BackendOf(new PlatformHandle(new IntPtr(handle), descriptor)).ToString());

    [Fact]
    public void NoHandle_IsNoBackend() => Assert.Equal(OverlayBackend.None, X11Overlay.BackendOf(null));

    [Fact]
    public void Win32RaiseRequestsTopmostWithoutMovingSizingOrActivating()
    {
        var handle = new IntPtr(42);
        bool called = false;
        Assert.True(Win32Overlay.Raise(handle, (hwnd, after, x, y, width, height, flags) =>
        {
            called = true;
            Assert.Equal(handle, hwnd);
            Assert.Equal(new IntPtr(-1), after);
            Assert.Equal((0, 0, 0, 0, 0x13u), (x, y, width, height, flags));
            return true;
        }));
        Assert.True(called);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    // Walk towards the top of the desktop's Z order, tolerating unrelated windows between ours.
    private static bool IsAbove(IntPtr above, IntPtr below)
    {
        for (var current = GetWindow(below, 3 /* GW_HWNDPREV */); current != IntPtr.Zero;
             current = GetWindow(current, 3 /* GW_HWNDPREV */))
            if (current == above) return true;
        return false;
    }

    [Fact]
    public void Win32RaisePreservesNativeExtendedStyles()
    {
        if (!OperatingSystem.IsWindows()) return;
        AvaloniaTestDispatcher.Run(() =>
        {
            const uint noActivate = 0x08000000;
            const uint popupVisible = 0x80000000 | 0x10000000; // WS_POPUP | WS_VISIBLE
            // Match the visible, no-activate run chrome. Keep both windows off screen.
            var hwnd = CreateWindowExW(noActivate, "STATIC", "raise test", popupVisible, -32000, -32000, 100, 100,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, hwnd);
            IntPtr other = IntPtr.Zero;
            try
            {
                var before = Win32Overlay.GetWindowLong(hwnd, -20);
                Assert.True(IsWindowVisible(hwnd));
                Assert.NotEqual(0u, before & noActivate);
                Assert.Equal(0u, before & Win32Overlay.WsExTopmost);
                other = CreateWindowExW(noActivate | Win32Overlay.WsExTopmost, "STATIC", "raise rival",
                    popupVisible, -32000, -32000, 100, 100, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, other);
                Assert.True(IsWindowVisible(other));
                Assert.True(IsAbove(other, hwnd));
                Assert.True(Win32Overlay.Raise(hwnd));
                Assert.Equal(before | Win32Overlay.WsExTopmost, Win32Overlay.GetWindowLong(hwnd, -20));
                Assert.True(IsAbove(hwnd, other));
            }
            finally
            {
                if (other != IntPtr.Zero) DestroyWindow(other);
                DestroyWindow(hwnd);
            }
        });
    }

    [Fact]
    public void Win32Style_AddsOverlayBits_AndTogglesTransparentWithNoActivate()
    {
        const uint avaloniaRebuild = 0x00000100 | 0x00200000;  // WS_EX_WINDOWEDGE | WS_EX_NOREDIRECTIONBITMAP
        var on = Win32Overlay.Style(avaloniaRebuild, true);
        Assert.Equal(avaloniaRebuild | 0x00080000u | 0x80u | 0x08000000u | 0x20u, on);  // LAYERED|TOOLWINDOW + NOACTIVATE|TRANSPARENT
        // Interactive again (WPF ChaosOverlayWindow): NOACTIVATE goes with TRANSPARENT so it can take focus.
        Assert.Equal(avaloniaRebuild | 0x00080000u | 0x80u, Win32Overlay.Style(on, false));
        // A passive toast takes clicks but keeps WS_EX_NOACTIVATE (WPF PassiveToastWindow).
        Assert.Equal(avaloniaRebuild | 0x00080000u | 0x80u | 0x08000000u, Win32Overlay.Style(on, false, passive: true));
    }
}
