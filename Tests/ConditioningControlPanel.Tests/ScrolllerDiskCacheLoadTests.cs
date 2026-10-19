using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Scrolller device cache on disk: the background load (the UI thread never waits on the
/// folder scan), the index writes, eviction of a file a player holds open, and the consent check.
/// Runs with the switch tests: the switch is process-wide.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class ScrolllerDiskCacheLoadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-scache-load-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public ScrolllerDiskCacheLoadTests()
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => _root;
        ScrolllerDiskCache.UtcNow = () => _now;
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
    }

    public void Dispose()
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.UtcNow = () => DateTime.UtcNow;
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Put(string url, int bytes)
    {
        using var w = ScrolllerDiskCache.BeginWrite(url, new ScrolllerCacheMeta("picture"));
        Assert.NotNull(w);
        w!.Stream.Write(new byte[bytes], 0, bytes);
        w.Commit();
        _now = _now.AddMinutes(1);
    }

    private void Restart()
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => _root;
        ScrolllerDiskCache.UtcNow = () => _now;
    }

    [Fact]
    public void Before_the_background_load_the_cache_reads_empty_then_it_hits()
    {
        var url = "https://images.scrolller.com/w/1.jpg";
        Put(url, 64);
        ScrolllerDiskCache.Flush();
        Restart();
        ScrolllerDiskCache.WarmInline = false;

        Assert.False(ScrolllerDiskCache.Contains(url));   // cold: no scan on this thread
        Assert.True(ScrolllerDiskCache.WarmAsync().Wait(TimeSpan.FromSeconds(10)));
        Assert.True(ScrolllerDiskCache.Contains(url));
    }

    [Fact]
    public void A_file_held_open_keeps_its_row_and_a_younger_one_goes_instead()
    {
        ScrolllerDiskCache.CapBytes = 250;
        var a = "https://i.scrolller.com/a.jpg";
        var b = "https://i.scrolller.com/b.jpg";
        Put(a, 100);
        Put(b, 100);
        using (new FileStream(ScrolllerDiskCache.TryGetPath(a)!, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            _now = _now.AddMinutes(1);
            ScrolllerDiskCache.TryGetPath(b);   // b is now the more recent use: a is the oldest
            Put("https://i.scrolller.com/c.jpg", 100);
            Assert.True(ScrolllerDiskCache.Contains(a));
            Assert.False(ScrolllerDiskCache.Contains(b));
        }
    }

    [Fact]
    public void Moving_the_root_writes_the_old_index_first()
    {
        var url = "https://images.scrolller.com/w/2.jpg";
        Put(url, 64);
        var other = _root + "-b";
        try
        {
            ScrolllerDiskCache.RootProvider = () => other;
            Assert.False(ScrolllerDiskCache.Contains(url));
            Restart();
            Assert.NotNull(ScrolllerDiskCache.TryGetPath(url));
        }
        finally { try { Directory.Delete(other, true); } catch { } }
    }

    [Fact]
    public void A_commit_after_flush_still_reaches_the_index()
    {
        Put("https://images.scrolller.com/w/3.jpg", 10);
        ScrolllerDiskCache.Flush();
        Put("https://images.scrolller.com/w/4.jpg", 10);
        ScrolllerDiskCache.Flush();
        Restart();
        Assert.True(ScrolllerDiskCache.Contains("https://images.scrolller.com/w/3.jpg"));
        Assert.True(ScrolllerDiskCache.Contains("https://images.scrolller.com/w/4.jpg"));
    }

    [Fact]
    public void Without_remote_media_consent_nothing_comes_from_the_cache()
    {
        var url = "https://images.scrolller.com/w/5.jpg";
        Put(url, 10);
        ScrolllerDiskCache.RemoteConsent = () => false;
        Assert.Null(ScrolllerDiskCache.GetOrFetchAsync(url, null, CancellationToken.None).Result);
        ScrolllerDiskCache.RemoteConsent = () => true;
        Assert.NotNull(ScrolllerDiskCache.GetOrFetchAsync(url, null, CancellationToken.None).Result);
    }
}
