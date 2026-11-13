using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VideoWedgeWatchdogTests
{
    [Fact]
    public void BlockedDispatcherReleasesEveryCachedWindowAndStrictVeto()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            var uiThread = Environment.CurrentManagedThreadId;
            var released = new ConcurrentDictionary<IntPtr, int>();
            using var done = new CountdownEvent(2);
            using var watchdog = new VideoWedgeWatchdog((handle, _) =>
            {
                released[handle] = Environment.CurrentManagedThreadId;
                done.Signal();
            });
            var overlay = new MandatoryVideoOverlay(null) { _wedge = watchdog };
            watchdog.Add((IntPtr)1, OverlayBackend.X11);
            watchdog.Add((IntPtr)2, OverlayBackend.Win32);
            watchdog.Add((IntPtr)3, OverlayBackend.X11);
            watchdog.Remove((IntPtr)3);
            Assert.True(overlay.VetoStrictClose(true));
            Thread.Sleep(TimeSpan.FromSeconds(10));
            Assert.True(watchdog.StallSeen);
            Assert.False(overlay.VetoStrictClose(true));
            Assert.Empty(released);
            Assert.True(done.Wait(TimeSpan.FromSeconds(25)), "escape hatch did not run while the UI thread was blocked");
            Assert.Equal(2, released.Count);
            Assert.True(released.ContainsKey((IntPtr)1));
            Assert.True(released.ContainsKey((IntPtr)2));
            Assert.All(released.Values, id => Assert.NotEqual(uiThread, id));
        });
    }

    [Fact]
    public void ABlockedNativeCallDoesNotPreventOtherWindowReleases()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        using var firstExited = new ManualResetEventSlim();
        using var secondDone = new ManualResetEventSlim();
        AvaloniaTestDispatcher.Run(() =>
        {
            using var watchdog = new VideoWedgeWatchdog((handle, _) =>
            {
                if (handle == (IntPtr)1) { firstStarted.Set(); releaseFirst.Wait(); firstExited.Set(); }
                else secondDone.Set();
            });
            try
            {
                watchdog.ReleaseHandles(new[] {
                    new KeyValuePair<IntPtr, OverlayBackend>((IntPtr)1, OverlayBackend.Win32),
                    new KeyValuePair<IntPtr, OverlayBackend>((IntPtr)2, OverlayBackend.Win32) });
                Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(secondDone.Wait(TimeSpan.FromSeconds(5)));
            }
            finally { releaseFirst.Set(); Assert.True(firstExited.Wait(TimeSpan.FromSeconds(5))); }
        });
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [Fact]
    public void WindowsCloaksTopmostWindowWhileItsOwnerIsBlocked()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Requires Windows DWM");
        using var created = new ManualResetEventSlim();
        using var unblock = new ManualResetEventSlim();
        IntPtr hwnd = IntPtr.Zero;
        var owner = new Thread(() =>
        {
            hwnd = CreateWindowExW(0x8 | 0x08000000, "STATIC", "CCP wedge test", 0x80000000 | 0x10000000,
                -32000, -32000, 100, 100, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            created.Set();
            try { unblock.Wait(); }
            finally { if (hwnd != IntPtr.Zero) DestroyWindow(hwnd); }
        }) { IsBackground = true };
        owner.Start();
        try
        {
            Assert.True(created.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotEqual(IntPtr.Zero, hwnd);
            Assert.True(IsWindowVisible(hwnd));
            Assert.NotEqual(0u, Win32Overlay.GetWindowLong(hwnd, -20) & Win32Overlay.WsExTopmost);
            Assert.Equal(0, DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out var before, sizeof(int)));
            Assert.Equal(0, before);
            var rescue = Task.Run(() => Win32Overlay.ReleaseWedgedWindow(hwnd));
            Assert.True(rescue.Wait(TimeSpan.FromSeconds(10)), "window release blocked on its owner");
            Assert.Equal(0, rescue.Result);
            Assert.False(unblock.IsSet);
            Assert.Equal(0, DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)));
            Assert.NotEqual(0, cloaked & 1 /* DWM_CLOAKED_APP */);
        }
        finally
        {
            unblock.Set();
            Assert.True(owner.Join(TimeSpan.FromSeconds(10)), "window owner did not finish");
        }
    }

}
