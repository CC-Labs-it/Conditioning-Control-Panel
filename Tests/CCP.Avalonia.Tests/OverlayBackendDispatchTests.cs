using System;
using System.Runtime.InteropServices;
using Avalonia.Platform;
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

    [Fact]
    public void Win32RaisePreservesNativeExtendedStyles()
    {
        if (!OperatingSystem.IsWindows()) return;
        const uint noActivate = 0x08000000;
        var hwnd = CreateWindowExW(noActivate, "STATIC", "raise test", 0, 0, 0, 100, 100,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);
        try
        {
            var before = Win32Overlay.GetWindowLong(hwnd, -20);
            Assert.NotEqual(0u, before & noActivate);
            Assert.True(Win32Overlay.Raise(hwnd));
            Assert.Equal(before | Win32Overlay.WsExTopmost, Win32Overlay.GetWindowLong(hwnd, -20));
        }
        finally { DestroyWindow(hwnd); }
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
