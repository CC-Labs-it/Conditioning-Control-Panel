using System;
using System.Collections.Concurrent;
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
}
