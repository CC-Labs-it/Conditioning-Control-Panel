using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class Win32AwarenessForegroundProbeTests
{
    [Theory]
    [InlineData("CHROME.EXE", "chrome")]
    [InlineData("Game", "game")]
    public void NormalizesProcess(string input, string expected) =>
        Assert.Equal(expected, Win32AwarenessForegroundProbe.NormalizeProcessName(input));

    [Theory]
    [InlineData("progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("Shell_TrayWnd", true)]
    [InlineData("Windows.UI.Core.CoreWindow", true)]
    [InlineData("Other", false)]
    public void ExcludesShell(string name, bool expected) =>
        Assert.Equal(expected, Win32AwarenessForegroundProbe.IsShellClass(name));

    [Fact]
    public void MonitorCoverageAllowsTwoPixelsOnEveryEdge()
    {
        var screen = new Win32AwarenessForegroundProbe.Rect { Left = -100, Top = 10, Right = 100, Bottom = 110 };
        var window = new Win32AwarenessForegroundProbe.Rect { Left = -98, Top = 12, Right = 98, Bottom = 108 };
        Assert.True(Win32AwarenessForegroundProbe.Covers(window, screen));
        window.Left++;
        Assert.False(Win32AwarenessForegroundProbe.Covers(window, screen));
    }

    [Fact]
    public void NativeReadHandlesUnavailableUser32()
    {
        if (!OperatingSystem.IsWindows()) Assert.Null(new Win32AwarenessForegroundProbe().Read());
    }

    [Theory]
    [InlineData(true, null, false)]
    [InlineData(true, "0", false)]
    [InlineData(true, "true", false)]
    [InlineData(true, "1", true)]
    [InlineData(false, "1", false)]
    public void NativeDesktopRequiresWindowsAndExplicitOptIn(bool windows, string? optIn, bool expected) =>
        Assert.Equal(expected, NativeDesktopEnabled(windows, optIn));

    private static bool NativeDesktopEnabled(bool windows, string? optIn) => windows && optIn == "1";

    [Fact]
    public void NativeReadReportsControlledForegroundWindow()
    {
        if (!NativeDesktopEnabled(OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable("CCP_NATIVE_DESKTOP_TEST"))) return;

        const string title = "CCP foreground probe test";
        var previous = GetForegroundWindow();
        // The built-in STATIC class has a real Win32 window procedure and is not a shell class.
        var hwnd = CreateWindowEx(0, "STATIC", title, 0x80000000, 0, 0, 200, 150,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);
        try
        {
            ShowWindow(hwnd, 5);
            Assert.True(SetForegroundWindow(hwnd), "Could not activate the test window on the Windows desktop.");
            Assert.True(SpinWait.SpinUntil(() => GetForegroundWindow() == hwnd, TimeSpan.FromSeconds(5)),
                "The test window did not become foreground.");

            var probe = new Win32AwarenessForegroundProbe();
            using var currentProcess = Process.GetCurrentProcess();
            var processName = currentProcess.ProcessName.ToLowerInvariant();
            var windowed = Assert.IsType<ConditioningControlPanel.Services.Awareness.ForegroundSample>(probe.Read());
            Assert.Equal(hwnd, windowed.Handle);
            Assert.Equal(title, windowed.Title);
            Assert.Equal(processName, windowed.ProcessName);
            Assert.False(windowed.IsFullscreen);

            int width = GetSystemMetrics(0), height = GetSystemMetrics(1);
            Assert.True(width > 200 && height > 150, "The primary monitor is too small for this test.");
            Assert.True(SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, 0x0010));
            var fullscreen = Assert.IsType<ConditioningControlPanel.Services.Awareness.ForegroundSample>(probe.Read());
            Assert.Equal(hwnd, fullscreen.Handle);
            Assert.Equal(title, fullscreen.Title);
            Assert.Equal(processName, fullscreen.ProcessName);
            Assert.True(fullscreen.IsFullscreen);
        }
        finally
        {
            DestroyWindow(hwnd);
            if (previous != IntPtr.Zero) SetForegroundWindow(previous);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
