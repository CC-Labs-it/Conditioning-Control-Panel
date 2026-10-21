using System;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The licence max age (owner, 2026-10-21): no Scrolller copy outlives 24 h counted from its
/// DOWNLOAD (never from its last use). Every read refuses and deletes an expired row, a row from a
/// build that kept no download time is expired at load, the sweep removes expired rows and
/// strangers, and the off switch deletes every cached file. Runs with the switch tests.
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class ScrolllerCacheMaxAgeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-scache-age-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    public ScrolllerCacheMaxAgeTests()
    {
        Restart();
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
    }

    public void Dispose()
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.UtcNow = () => DateTime.UtcNow;
        RemoteMediaCache.UtcNow = () => DateTime.UtcNow;
        RemoteMediaCache.ClearMemory();
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
        ScrolllerDiskCache.RootProvider = () => null;
        try { Directory.Delete(_root, true); } catch { }
    }

    private void Restart()
    {
        ScrolllerDiskCache.ResetForTests();
        ScrolllerDiskCache.RootProvider = () => _root;
        ScrolllerDiskCache.UtcNow = () => _now;
        RemoteMediaCache.UtcNow = () => _now;
    }

    private string Put(string url, int bytes = 16)
    {
        using var w = ScrolllerDiskCache.BeginWrite(url, new ScrolllerCacheMeta("picture"));
        Assert.NotNull(w);
        w!.Stream.Write(new byte[bytes], 0, bytes);
        w.Commit();
        return ScrolllerDiskCache.TryGetPath(url)!;
    }

    private string[] MediaFiles()
        => Directory.Exists(_root)
            ? Directory.GetFiles(_root).Where(f => ScrolllerDiskCache.IsCacheFileName(Path.GetFileName(f))).ToArray()
            : Array.Empty<string>();

    [Fact]
    public void A_copy_a_day_past_its_download_is_a_miss_and_its_file_is_gone()
    {
        var url = "https://images.scrolller.com/age/1.jpg";
        var path = Put(url);
        Assert.True(File.Exists(path));
        _now = _now.AddHours(24);
        Assert.False(ScrolllerDiskCache.Contains(url));
        Assert.Null(ScrolllerDiskCache.TryGetPath(url));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Age_counts_from_the_download_not_the_last_use()
    {
        var url = "https://images.scrolller.com/age/2.jpg";
        var path = Put(url);
        _now = _now.AddHours(23);
        Assert.NotNull(ScrolllerDiskCache.TryGetPath(url));   // a use at 23 h
        _now = _now.AddHours(2);                             // 25 h since download, 2 h since use
        Assert.Null(ScrolllerDiskCache.TryGetPath(url));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void The_boundary_is_24_hours_exactly()
    {
        var url = "https://images.scrolller.com/age/3.jpg";
        Put(url);
        var start = _now;
        _now = start.AddHours(24).AddSeconds(-1);
        Assert.True(ScrolllerDiskCache.Contains(url));
        _now = start.AddHours(24);
        Assert.False(ScrolllerDiskCache.Contains(url));

        Assert.False(ScrolllerCacheBook.IsExpiredAt(start, start.AddHours(24).AddSeconds(-1)));
        Assert.True(ScrolllerCacheBook.IsExpiredAt(start, start.AddHours(24)));
        Assert.True(ScrolllerCacheBook.IsExpiredAt(DateTime.MinValue, start));          // no stamp
        Assert.True(ScrolllerCacheBook.IsExpiredAt(start.AddHours(2), start));          // clock went back
        Assert.False(ScrolllerCacheBook.IsExpiredAt(start.AddMinutes(30), start));      // small skew is fine
    }

    [Fact]
    public void Entries_never_list_an_expired_row()
    {
        Put("https://images.scrolller.com/age/old.jpg");
        _now = _now.AddHours(20);
        Put("https://images.scrolller.com/age/new.jpg");
        _now = _now.AddHours(5);   // old is 25 h, new is 5 h
        var urls = ScrolllerDiskCache.Entries().Select(e => e.Url).ToArray();
        Assert.Equal(new[] { "https://images.scrolller.com/age/new.jpg" }, urls);
        Assert.Single(MediaFiles());
    }

    [Fact]
    public void GetOrFetch_never_hands_back_an_expired_copy()
    {
        var url = "https://images.scrolller.com/age/4.jpg";
        var path = Put(url);
        _now = _now.AddHours(30);
        // Switch off for the fresh fetch: the expired copy must not be the answer either way.
        ScrolllerPolicy.Away = true;
        try { Assert.Null(ScrolllerDiskCache.GetOrFetchAsync(url, null, CancellationToken.None).GetAwaiter().GetResult()); }
        finally { ScrolllerPolicy.Away = false; }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Index_rows_carry_the_download_time_and_round_trip_it()
    {
        var book = new ScrolllerCacheBook();
        var file = ScrolllerDiskCache.FileNameFor("https://a.scrolller.com/d.jpg");
        var got = _now.AddMinutes(-5);
        book.Put(new ScrolllerCacheEntry("https://a.scrolller.com/d.jpg", Path.Combine(_root, file), 10, _now,
            new ScrolllerCacheMeta("picture"), got));
        var json = book.ToJson();
        Assert.Contains("\"v\":2", json);
        Assert.Contains("\"d\":" + got.Ticks, json);
        Assert.Equal(got, ScrolllerCacheBook.FromJson(json, _root).Get("https://a.scrolller.com/d.jpg")!.DownloadedUtc);
    }

    [Fact]
    public void A_v1_row_without_a_download_time_is_deleted_with_its_file_at_load()
    {
        Directory.CreateDirectory(_root);
        var url = "https://images.scrolller.com/age/v1.jpg";
        var name = ScrolllerDiskCache.FileNameFor(url);
        var file = Path.Combine(_root, name);
        File.WriteAllBytes(file, new byte[10]);
        var v1 = "{\"v\":1,\"entries\":[{\"u\":\"" + url + "\",\"f\":\"" + name + "\",\"b\":10,\"t\":" + _now.Ticks + ",\"k\":\"picture\"}]}";
        File.WriteAllText(Path.Combine(_root, ScrolllerDiskCache.IndexFileName), v1);
        Assert.Equal(DateTime.MinValue, ScrolllerCacheBook.FromJson(v1, _root).Get(url)!.DownloadedUtc);

        Restart();
        Assert.False(ScrolllerDiskCache.Contains(url));
        Assert.Empty(ScrolllerDiskCache.Entries());
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Sweep_removes_expired_rows_and_strangers_and_keeps_fresh_ones()
    {
        var oldPath = Put("https://images.scrolller.com/age/s-old.jpg");
        _now = _now.AddHours(12);
        var freshPath = Put("https://images.scrolller.com/age/s-new.jpg");
        var stranger = Path.Combine(_root, new string('b', 64) + ".jpg");
        File.WriteAllText(stranger, "x");
        _now = _now.AddHours(13);   // old 25 h, fresh 13 h
        var oldPart = Path.Combine(_root, "abc.part");
        File.WriteAllText(oldPart, "x");
        File.SetCreationTimeUtc(oldPart, _now.AddHours(-1));
        var youngPart = Path.Combine(_root, "def.part");   // may still be downloading
        File.WriteAllText(youngPart, "x");
        File.SetCreationTimeUtc(youngPart, _now.AddMinutes(-2));

        Assert.Equal(3, ScrolllerDiskCache.SweepExpired());
        Assert.False(File.Exists(oldPath));
        Assert.False(File.Exists(stranger));
        Assert.False(File.Exists(oldPart));
        Assert.True(File.Exists(youngPart));
        Assert.True(File.Exists(freshPath));
        Assert.True(ScrolllerDiskCache.Contains("https://images.scrolller.com/age/s-new.jpg"));
    }

    [Fact]
    public void Sweep_looks_one_period_ahead_so_no_file_outlives_the_day()
    {
        var path = Put("https://images.scrolller.com/age/ahead.jpg");
        _now = _now.AddHours(23).AddMinutes(1);   // still served, but past it before the next sweep
        Assert.True(ScrolllerDiskCache.Contains("https://images.scrolller.com/age/ahead.jpg"));
        Assert.Equal(1, ScrolllerDiskCache.SweepExpired());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void The_off_switch_deletes_every_cached_file()
    {
        Put("https://images.scrolller.com/age/off1.jpg");
        Put("https://images.scrolller.com/age/off2.jpg");
        Assert.Equal(2, MediaFiles().Length);
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        Assert.Empty(MediaFiles());
        Assert.Empty(Directory.GetFiles(_root).Where(f => !Path.GetFileName(f).StartsWith(ScrolllerDiskCache.IndexFileName)));

        // Switched back on, nothing comes back from the old index.
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
        Restart();
        Assert.Empty(ScrolllerDiskCache.Entries());
    }

    [Fact]
    public void The_off_switch_never_creates_a_missing_folder()
    {
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Memory_copies_expire_a_day_after_their_download()
    {
        var url = "https://images.scrolller.com/age/mem.jpg";
        RemoteMediaCache.Store(url, new byte[] { 1, 2, 3 }, _now);
        _now = _now.AddHours(23);
        Assert.True(RemoteMediaCache.IsCached(url));
        _now = _now.AddHours(1);
        Assert.False(RemoteMediaCache.IsCached(url));

        RemoteMediaCache.Store(url, new byte[] { 1 }, _now.AddHours(-25));
        RemoteMediaCache.Store("https://images.scrolller.com/age/mem2.jpg", new byte[] { 1 }, _now);
        Assert.Equal(1, RemoteMediaCache.DropExpiredMemory());
        Assert.False(RemoteMediaCache.IsCached(url));
        Assert.True(RemoteMediaCache.IsCached("https://images.scrolller.com/age/mem2.jpg"));
    }

    [Fact]
    public void The_off_switch_clears_memory_copies()
    {
        var url = "https://images.scrolller.com/age/mem-off.jpg";
        RemoteMediaCache.Store(url, new byte[] { 1 }, _now);
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(false, null, null));
        ScrolllerPolicyService.Accept(new ScrolllerPolicyService.Answer(true, null, null));
        Assert.False(RemoteMediaCache.IsCached(url));
    }
}
