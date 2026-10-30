using System;
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
    [Theory]
    [InlineData(false, -2)]  // HWND_NOTOPMOST, WPF ChaosWindowZ.cs:50
    [InlineData(true, -1)]   // HWND_TOPMOST, WPF ChaosWindowZ.cs:53
    public void Win32Stacking_submits_the_native_band_without_activation(bool pinned, int band)
    {
        var saved = Win32Overlay.PositionWindow;
        int calls = 0;
        try
        {
            Win32Overlay.PositionWindow = (hwnd, after, x, y, width, height, flags) =>
            {
                calls++;
                Assert.Equal(new IntPtr(42), hwnd);
                Assert.Equal(new IntPtr(band), after);
                Assert.Equal((0, 0, 0, 0), (x, y, width, height));
                Assert.Equal(0x13u, flags); // SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE
                return false;
            };
            Assert.False(Win32Overlay.SetStacking(new IntPtr(42), pinned));
            Assert.Equal(1, calls);
        }
        finally { Win32Overlay.PositionWindow = saved; }
    }

}
