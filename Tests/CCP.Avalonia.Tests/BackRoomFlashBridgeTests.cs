using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Views.BackRoom;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BackRoomFlashBridgeTests
{
    [Fact]
    public void PulseHeroAndTeardownUseTheSuppliedSurfaces()
    {
        var file = Path.GetTempFileName();
        try
        {
            int duration = 0, stopped = 0, closed = 0, shown = 0;
            double opacity = 0;
            Action? pending = null;
            long now = 100;
            var bridge = new BackRoomFlashBridge((ms, alpha) => { duration = ms; opacity = alpha; },
                (path, ms, alpha, still, callback) => { Assert.Equal(file, path); Assert.Equal(4000, ms); Assert.Equal(.3, alpha); Assert.False(still); pending = callback; },
                () => { stopped++; pending = null; }, () => closed++, () => false, () => now,
                Path.GetDirectoryName(file)!, Path.GetDirectoryName(file)!);
            bridge.GlitchWash(350, .4);
            Assert.Equal(350, duration); Assert.Equal(.4, opacity);
            var gif = new BackRoomGif("key", "https://ccp.assets/" + Path.GetFileName(file), 0, 0, "local");
            bridge.GifFull(gif, 4000, .3, () => shown++);
            Assert.NotNull(pending); Assert.Equal(0, shown);
            pending!(); Assert.Equal(1, shown);
            bridge.GifFull(gif, 4000, .3, () => shown++);
            bridge.StopAll(); Assert.Null(pending); Assert.Equal(1, stopped); Assert.Equal(1, closed);
            bridge.StopAll(); Assert.Equal(1, closed);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void RefusesRemoteAndEscapingPathsAndPreservesChaosWash()
    {
        int heroes = 0, closed = 0;
        var bridge = new BackRoomFlashBridge((_, _) => { }, (_, _, _, _, _) => heroes++, () => { }, () => closed++,
            () => true, () => 100, Path.GetDirectoryName(typeof(object).Assembly.Location)!, Path.GetTempPath());
        foreach (var url in new[] { "https://example.com/" + Path.GetFileName(typeof(object).Assembly.Location), "file:///tmp/a.gif", "https://ccp.assets/%2e%2e%2fetc%2fpasswd" })
            bridge.GifFull(new BackRoomGif("k", url, 0, 0, ""), 100, .2, () => { });
        bridge.GlitchWash(100, .2); bridge.StopAll();
        Assert.Equal(0, heroes); Assert.Equal(0, closed);
    }
}
