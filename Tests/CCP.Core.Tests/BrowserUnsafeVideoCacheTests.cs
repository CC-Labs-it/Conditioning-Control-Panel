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
public sealed class BrowserUnsafeVideoCacheTests : IDisposable
{
    // Stepped clock: the 3 s debounced save never runs on a real timer after a test ends.
    private readonly PopQuizSchedulerTests.FakeClock _clock = new();

    public BrowserUnsafeVideoCacheTests()
    {
        BrowserUnsafeVideoCache.ResetForTests();
        BrowserUnsafeVideoCache.Clock = _clock;
    }

    public void Dispose()
    {
        BrowserUnsafeVideoCache.ResetForTests();
        BrowserUnsafeVideoCache.Clock = TimeProvider.System;
    }

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
    public void AddedFile_IsSavedAfterTheDebounce_AndSurvivesAReload()
    {
        var clip = TempClip(30);
        var file = Path.Combine(ConditioningControlPanel.CorePaths.UserData, "browser_unsafe_videos.json");
        string Saved() => File.Exists(file) ? File.ReadAllText(file) : "";
        var marker = Path.GetFileName(clip);
        try
        {
            BrowserUnsafeVideoCache.Add(clip, "test");
            _clock.Advance(TimeSpan.FromMilliseconds(2900));
            Assert.DoesNotContain(marker, Saved());   // still inside the debounce

            _clock.Advance(TimeSpan.FromMilliseconds(200));
            Assert.Contains(marker, Saved());

            BrowserUnsafeVideoCache.ResetForTests();   // a fresh session reads the file back
            Assert.True(BrowserUnsafeVideoCache.Contains(clip));
        }
        finally { File.Delete(clip); }
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
