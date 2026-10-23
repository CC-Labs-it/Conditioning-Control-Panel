using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1453: a Takeover wallpaper shuffle called SystemParametersInfo with
/// SPIF_SENDCHANGE on the UI thread and hung the app. Writes now go through a queue that never
/// blocks its caller, keeps order, and flushes with a bound.</summary>
public class WallpaperSetQueueTests
{
    [Fact]
    public void Enqueue_ReturnsAtOnce_WhileTheWriteIsStuck()
    {
        using var release = new ManualResetEventSlim(false);
        var q = new WallpaperSetQueue(_ => { release.Wait(TimeSpan.FromSeconds(10)); return true; });

        var t = q.Enqueue("a.png");

        Assert.False(t.IsCompleted);
        Assert.True(q.IsBusy);
        Assert.False(q.Flush(TimeSpan.FromMilliseconds(50)));   // bounded, never hangs
        release.Set();
        Assert.True(q.Flush(TimeSpan.FromSeconds(5)));
        Assert.True(t.Result);
        Assert.False(q.IsBusy);
    }

    [Fact]
    public void Writes_LandInOrder()
    {
        var seen = new List<string>();
        var q = new WallpaperSetQueue(p => { Thread.Sleep(5); lock (seen) seen.Add(p); return true; });

        q.Enqueue("shuffle.png");
        q.Enqueue("restore.png");

        Assert.True(q.Flush(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { "shuffle.png", "restore.png" }, seen);
    }

    [Fact]
    public void ThrowingOrRefusedWrite_IsFalse_AndTheQueueGoesOn()
    {
        var q = new WallpaperSetQueue(p => p == "boom" ? throw new InvalidOperationException() : p == "ok");

        var boom = q.Enqueue("boom");
        var refused = q.Enqueue("no");
        var ok = q.Enqueue("ok");

        Assert.True(q.Flush(TimeSpan.FromSeconds(5)));
        Assert.False(boom.Result);
        Assert.False(refused.Result);
        Assert.True(ok.Result);
        Assert.False(q.IsBusy);
    }

    [Fact]
    public void WallpaperService_NeverWaitsOnABroadcast()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services", "Video", "WallpaperService.cs"));

        // No SENDCHANGE flag in any call, and the only SPI_SETDESKWALLPAPER write is the queued one.
        Assert.DoesNotMatch(new Regex(@"SystemParametersInfo\([^;]*SPIF_SENDCHANGE"), src);
        Assert.Single(Regex.Matches(src, @"SystemParametersInfo\(SPI_SETDESKWALLPAPER"));
        Assert.Contains("private static bool ApplyWallpaperNow", src);
    }
}
