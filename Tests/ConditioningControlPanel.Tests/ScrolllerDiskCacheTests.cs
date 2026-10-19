using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Scrolller device cache (owner, 2026-10-19): a bounded LRU on the user's disk that survives
/// restarts, so a replay costs nothing against the daily share. Pins the disk behaviour against a
/// throwaway folder (the pure rules are in ScrolllerCacheBookTests). Runs with the switch tests: the
/// switch is process-wide.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class ScrolllerDiskCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-scache-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public ScrolllerDiskCacheTests()
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
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Put(string url, int bytes)
    {
        using var w = ScrolllerDiskCache.BeginWrite(url, new ScrolllerCacheMeta("picture", "sub"));
        Assert.NotNull(w);
        w!.Stream.Write(new byte[bytes], 0, bytes);
        w.Commit();
        return ScrolllerDiskCache.TryGetPath(url)!;
    }

    [Fact]
    public void A_committed_write_becomes_an_entry_and_survives_a_restart()
    {
        var url = "https://images.scrolller.com/a/1.jpg";
        var path = Put(url, 100);
        Assert.True(File.Exists(path));
        Assert.True(ScrolllerDiskCache.Contains(url));
        Assert.Equal("sub", ScrolllerDiskCache.Entries().Single().Meta.Sub);

        ScrolllerDiskCache.Flush();
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => _root;
        Assert.Equal(path, ScrolllerDiskCache.TryGetPath(url));
    }

    [Fact]
    public void A_write_disposed_without_commit_leaves_nothing()
    {
        var url = "https://images.scrolller.com/a/2.jpg";
        using (var w = ScrolllerDiskCache.BeginWrite(url, null))
        {
            w!.Stream.Write(new byte[50], 0, 50);
        }
        Assert.False(ScrolllerDiskCache.Contains(url));
        Assert.Null(ScrolllerDiskCache.TryGetPath(url));
        Assert.Empty(Directory.GetFiles(_root, "*" + ScrolllerDiskCache.PartSuffix));
    }

    [Fact]
    public void Past_the_cap_the_least_recently_used_file_goes()
    {
        ScrolllerDiskCache.CapBytes = 250;
        var a = Put("https://i.scrolller.com/a.jpg", 100);
        _now = _now.AddMinutes(1);
        Put("https://i.scrolller.com/b.jpg", 100);
        _now = _now.AddMinutes(1);
        Assert.NotNull(ScrolllerDiskCache.TryGetPath("https://i.scrolller.com/a.jpg")); // a is now newer than b
        _now = _now.AddMinutes(1);
        Put("https://i.scrolller.com/c.jpg", 100);

        Assert.True(ScrolllerDiskCache.Contains("https://i.scrolller.com/a.jpg"));
        Assert.False(ScrolllerDiskCache.Contains("https://i.scrolller.com/b.jpg"));
        Assert.True(ScrolllerDiskCache.Contains("https://i.scrolller.com/c.jpg"));
        Assert.True(File.Exists(a));
    }

    [Fact]
    public void Switch_off_hides_every_cached_copy()
    {
        var url = "https://i.scrolller.com/off.jpg";
        Put(url, 10);
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        Assert.Null(ScrolllerDiskCache.TryGetPath(url));
        Assert.False(ScrolllerDiskCache.Contains(url));
        Assert.Empty(ScrolllerDiskCache.Entries());
        Assert.Null(ScrolllerDiskCache.BeginWrite(url, null));
    }

    [Fact]
    public void First_use_sweeps_leftover_parts_and_unknown_files()
    {
        Directory.CreateDirectory(_root);
        var part = Path.Combine(_root, "abc.part");
        var stray = Path.Combine(_root, new string('a', 64) + ".jpg");
        File.WriteAllText(part, "x");
        File.WriteAllText(stray, "x");
        Assert.Empty(ScrolllerDiskCache.Entries());
        Assert.False(File.Exists(part));
        Assert.False(File.Exists(stray));
    }

    [Fact]
    public void A_spent_share_refuses_a_fresh_fetch_without_touching_the_network()
    {
        // Switch off is the simplest "may not fetch fresh": the call must come back null at once.
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        var got = ScrolllerDiskCache.GetOrFetchAsync("https://v.scrolller.com/never.mp4", null, default).GetAwaiter().GetResult();
        Assert.Null(got);
        // A non-Scrolller url is never the cache's business.
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
        Assert.Null(ScrolllerDiskCache.GetOrFetchAsync("https://example.com/a.mp4", null, default).GetAwaiter().GetResult());
    }
}
