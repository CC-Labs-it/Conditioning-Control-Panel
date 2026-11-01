using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Xunit;

namespace CCP.Avalonia.Tests;

public class AwarenessX11ForegroundTests
{
    private const string X11 = "libX11.so.6";
    [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint border, ulong foreground, ulong background);
    [DllImport(X11)] private static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport(X11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(X11)] private static extern int XStoreName(IntPtr display, IntPtr window, string name);
    [DllImport(X11)] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, ref long data, int count);
    [DllImport(X11)] private static extern int XDeleteProperty(IntPtr display, IntPtr window, IntPtr property);
    [DllImport(X11)] private static extern int XSync(IntPtr display, bool discard);

    [Fact]
    public void ReadsTitleAndPidFromOneIsolatedX11WindowAndDropsMissingOrStalePid()
    {
        // Opt in only for a disposable X server, never the developer's real desktop.
        var displayName = Environment.GetEnvironmentVariable("CCP_TEST_X11_DISPLAY");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(displayName) ||
            displayName != Environment.GetEnvironmentVariable("DISPLAY"))
            Assert.Skip("Set CCP_TEST_X11_DISPLAY and DISPLAY to an isolated X11 server");

        var display = XOpenDisplay(IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, display);
        var root = XDefaultRootWindow(display);
        var window = XCreateSimpleWindow(display, root, 0, 0, 100, 100, 0, 0, 0);
        var active = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
        var pid = XInternAtom(display, "_NET_WM_PID", false);
        var cardinal = XInternAtom(display, "CARDINAL", false);
        var windowType = XInternAtom(display, "WINDOW", false);
        using var child = Process.Start(new ProcessStartInfo("sleep", "60") { UseShellExecute = false })!;
        try
        {
            XStoreName(display, window, "League of Legends");
            long windowId = window.ToInt64();
            XChangeProperty(display, root, active, windowType, 32, 0, ref windowId, 1);
            long processId = child.Id;
            XChangeProperty(display, window, pid, cardinal, 32, 0, ref processId, 1);
            XSync(display, false);
            X11ActiveWindow.Disabled = false;
            Assert.Equal(("League of Legends", "sleep"), X11ActiveWindow.ReadForeground());
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.AwarenessModeEnabled = true;
            settings.Current.AwarenessConsentGiven = true;
            settings.Current.AwarenessDenySeeded = true;
            settings.Current.AwarenessDenyList = new() { "sleep" };
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            WindowAwarenessService.V2Lifecycle = null;
            AwarenessPause.Resume();
            using var observer = AvApp.CreateWindowAwareness(X11ActiveWindow.ReadForeground);
            var events = 0;
            observer.ActivityChanged += (_, _) => events++;
            observer.Start();
            observer.PollOnce();
            Assert.Equal(0, events);

            processId = int.MaxValue; // exited or nonexistent process
            XChangeProperty(display, window, pid, cardinal, 32, 0, ref processId, 1);
            XSync(display, false);
            Assert.Equal(("League of Legends", ""), X11ActiveWindow.ReadForeground());
            observer.PollOnce();
            Assert.Equal(1, events); // no PID: existing title-only filter
            XDeleteProperty(display, window, pid);
            XSync(display, false);
            Assert.Equal(("League of Legends", ""), X11ActiveWindow.ReadForeground());
        }
        finally
        {
            X11ActiveWindow.Disabled = true;
            XDeleteProperty(display, root, active);
            XDestroyWindow(display, window);
            XCloseDisplay(display);
            if (!child.HasExited) child.Kill();
        }
    }
}
