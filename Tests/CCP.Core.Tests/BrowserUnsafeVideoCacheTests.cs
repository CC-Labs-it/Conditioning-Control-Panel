using System;
using System.IO;
using ConditioningControlPanel.Services.Video.Browser;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The browser-unsafe list after its move to Core: keyed by stable identity + size, so a content-pack
/// clip's per-play temp path still hits, and a re-encode (new size) gets a fresh chance. Runs in this
/// assembly's CCP_USERDATA_DIR sandbox (RoadmapTestProfile), so the persisted file never touches a profile.
/// </summary>
public sealed class BrowserUnsafeVideoCacheTests
{
    private static string TempClip(int bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-unsafe-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void AddedFile_IsContained_UntilItsSizeChanges()
    {
        var clip = TempClip(10);
        try
        {
            Assert.False(BrowserUnsafeVideoCache.Contains(clip));
            BrowserUnsafeVideoCache.Add(clip, "test");
            Assert.True(BrowserUnsafeVideoCache.Contains(clip));

            File.WriteAllBytes(clip, new byte[11]);   // a re-encode
            Assert.False(BrowserUnsafeVideoCache.Contains(clip));
        }
        finally { File.Delete(clip); }
    }

    [Fact]
    public void PackTempPaths_ShareTheRegisteredStableKey()
    {
        var first = TempClip(20);
        var second = TempClip(20);
        var key = "pack:" + Guid.NewGuid().ToString("N") + "/clip.mp4";
        try
        {
            BrowserUnsafeVideoCache.RegisterStableKey(first, key);
            BrowserUnsafeVideoCache.RegisterStableKey(second, key);
            Assert.Equal(key, BrowserUnsafeVideoCache.ResolveStableKey(second));

            BrowserUnsafeVideoCache.Add(first, "test");
            Assert.True(BrowserUnsafeVideoCache.Contains(second));
        }
        finally { File.Delete(first); File.Delete(second); }
    }

    [Fact]
    public void BlankInput_NeverThrowsAndIsNeverContained()
    {
        BrowserUnsafeVideoCache.Add(null, "test");
        BrowserUnsafeVideoCache.RegisterStableKey("", "k");
        Assert.False(BrowserUnsafeVideoCache.Contains(null));
        Assert.Null(BrowserUnsafeVideoCache.ResolveStableKey(null));
    }
}
