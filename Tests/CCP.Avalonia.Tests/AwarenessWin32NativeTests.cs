using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class AwarenessWin32NativeTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [Fact]
    public void NativeForegroundReachesTheSelectedPollAndPrivacyFilter()
    {
        // Never take focus or read the desktop in an ordinary developer test run.
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("CCP_TEST_WIN32_FOREGROUND") != "1")
            Assert.Skip("Requires the dedicated Windows CI desktop");

        var previous = GetForegroundWindow();
        var hwnd = CreateWindowExW(0, "STATIC", "League of Legends", 0x00CF0000,
            40, 40, 400, 200, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, hwnd);
        var observer = AvApp.WindowAwareness;
        try
        {
            ShowWindow(hwnd, 5);
            var foregroundThread = previous == IntPtr.Zero ? 0 : GetWindowThreadProcessId(previous, out _);
            var currentThread = GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                AttachThreadInput(currentThread, foregroundThread, true);
            try { SetForegroundWindow(hwnd); }
            finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
            Assert.True(SpinWait.SpinUntil(() => GetForegroundWindow() == hwnd, TimeSpan.FromSeconds(5)),
                "The dedicated CI desktop did not give the test window foreground focus");

            Win32Windows.AwarenessDisabled = false;
            var process = Process.GetCurrentProcess().ProcessName;
            Assert.Equal(("League of Legends", process), Win32Windows.ReadAwarenessForeground());

            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.AwarenessModeEnabled = true;
            settings.Current.AwarenessConsentGiven = true;
            settings.Current.AwarenessDenySeeded = true;
            settings.Current.AwarenessDenyList = new();
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            WindowAwarenessService.V2Lifecycle = null;
            AwarenessPause.Resume();
            observer.Stop();
            observer.Start();
            observer.PollOnce();
            Assert.Equal(ActivityCategory.Gaming, observer.CurrentActivity);
            Assert.Equal("League of Legends", observer.CurrentServiceName);

            // Only the process is denied; the unchanged title remains allowed.
            settings.Current.AwarenessDenyList = new() { process };
            observer.PollOnce();
            Assert.Equal(ActivityCategory.Unknown, observer.CurrentActivity);
            Assert.Equal("", observer.CurrentServiceName);
            Assert.Equal("", observer.CurrentPageTitle);

            settings.Current.AwarenessDenyList = new();
            observer.PollOnce();
            Assert.Equal(ActivityCategory.Gaming, observer.CurrentActivity);
        }
        finally
        {
            observer.Stop();
            Win32Windows.AwarenessDisabled = true;
            AwarenessPause.Resume();
            DestroyWindow(hwnd);
            if (previous != IntPtr.Zero) SetForegroundWindow(previous);
        }
    }
}
