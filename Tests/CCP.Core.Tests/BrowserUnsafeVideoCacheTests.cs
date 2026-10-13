using System;
using System.IO;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The browser engine's "never again" list, now in CCP.Core: a file that failed in the player page
/// routes to LibVLC from then on, a content-pack clip keys on its pack identity rather than its
/// per-play temp path, and a re-encode (new size) gets a fresh chance. CorePaths.UserData is this
/// assembly's temp profile, so the debounced save never reaches a real one.
/// </summary>
public sealed class BrowserUnsafeVideoCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-unsafe-" + Guid.NewGuid().ToString("N"));

    public BrowserUnsafeVideoCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Clip(string name, int bytes)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, new byte[bytes]);
        return p;
    }

    [Fact]
    public void AFailedFileIsRememberedAndOthersAreNot()
    {
        var bad = Clip("bad.mp4", 10);
        var good = Clip("good.mp4", 10);

        Assert.False(BrowserUnsafeVideoCache.Contains(bad));
        BrowserUnsafeVideoCache.Add(bad, "test");

        Assert.True(BrowserUnsafeVideoCache.Contains(bad));
        Assert.False(BrowserUnsafeVideoCache.Contains(good));
    }

    [Fact]
    public void AReEncodeOfTheSameNameGetsAFreshChance()
    {
        var clip = Clip("same.mp4", 10);
        BrowserUnsafeVideoCache.Add(clip, "test");

        File.WriteAllBytes(clip, new byte[20]);
        Assert.False(BrowserUnsafeVideoCache.Contains(clip));
    }

    [Fact]
    public void APackClipKeysOnItsStableIdentityAcrossTempPaths()
    {
        var key = "pack:" + Guid.NewGuid().ToString("N") + "/clip.mp4";
        var firstPlay = Clip("ccp_temp_" + Guid.NewGuid().ToString("N") + ".mp4", 32);
        var secondPlay = Clip("ccp_temp_" + Guid.NewGuid().ToString("N") + ".mp4", 32);

        BrowserUnsafeVideoCache.RegisterStableKey(firstPlay, key);
        Assert.Equal(key, BrowserUnsafeVideoCache.ResolveStableKey(firstPlay));
        BrowserUnsafeVideoCache.Add(firstPlay, "test");

        BrowserUnsafeVideoCache.RegisterStableKey(secondPlay, key);
        Assert.True(BrowserUnsafeVideoCache.Contains(secondPlay));
        Assert.Null(BrowserUnsafeVideoCache.ResolveStableKey(Clip("loose.mp4", 32)));
    }

    [Fact]
    public void EmptyPathsAreNeverUnsafe()
    {
        Assert.False(BrowserUnsafeVideoCache.Contains(null));
        Assert.False(BrowserUnsafeVideoCache.Contains(""));
        BrowserUnsafeVideoCache.Add(null, "test");   // never throws
    }
}
