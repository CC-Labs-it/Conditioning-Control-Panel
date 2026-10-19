using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace ConditioningControlPanel.Tests;
/// <summary>
/// The Scrolller device cache books, pure (owner, 2026-10-19): LRU eviction, the whole-download
/// rule, file names and the index format. The disk behaviour is in ScrolllerDiskCacheTests.
/// </summary>
public sealed class ScrolllerCacheBookTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-scache-book");
    private readonly DateTime _now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private static ScrolllerCacheEntry E(string url, long bytes, int minute)
        => new(url, "x", bytes, new DateTime(2026, 10, 19, 10, minute, 0, DateTimeKind.Utc), new ScrolllerCacheMeta("clip"));

    [Fact]
    public void Evictions_drop_oldest_use_first_until_under_the_cap()
    {
        var entries = new[] { E("a", 40, 1), E("b", 40, 5), E("c", 40, 3) };
        var drop = ScrolllerCacheBook.Evictions(entries, 80, null);
        Assert.Equal(new[] { "a" }, drop.Select(e => e.Url));
        Assert.Empty(ScrolllerCacheBook.Evictions(entries, 120, null));
        Assert.Equal(new[] { "a", "c" }, ScrolllerCacheBook.Evictions(entries, 50, null).Select(e => e.Url));
    }

    [Fact]
    public void Evictions_never_pick_the_file_just_written()
    {
        var entries = new[] { E("new", 60, 0), E("old", 60, 9) };
        var drop = ScrolllerCacheBook.Evictions(entries, 70, keepUrl: "new");
        Assert.Equal(new[] { "old" }, drop.Select(e => e.Url));
    }

    [Theory]
    [InlineData(100, 100L, true, true)]
    [InlineData(100, null, true, true)]
    [InlineData(99, 100L, true, false)]   // short body: partial
    [InlineData(100, 100L, false, false)] // stopped before the end
    [InlineData(0, null, true, false)]    // nothing came
    public void A_download_is_whole_only_when_it_reached_the_end_with_the_declared_length(long got, long? declared, bool end, bool whole)
        => Assert.Equal(whole, ScrolllerCacheBook.IsWhole(got, declared, end));

    [Fact]
    public void File_names_are_the_url_hash_plus_a_safe_extension()
    {
        var name = ScrolllerDiskCache.FileNameFor("https://v.scrolller.com/abc/clip.mp4?x=1");
        Assert.Matches("^[0-9a-f]{64}\\.mp4$", name);
        Assert.True(ScrolllerDiskCache.IsCacheFileName(name));
        Assert.EndsWith(".bin", ScrolllerDiskCache.FileNameFor("https://images.scrolller.com/abc"));
        Assert.False(ScrolllerDiskCache.IsCacheFileName("..\\..\\evil.exe"));
        Assert.NotEqual(ScrolllerDiskCache.FileNameFor("https://a.scrolller.com/1.jpg"), ScrolllerDiskCache.FileNameFor("https://a.scrolller.com/2.jpg"));
    }

    [Fact]
    public void Index_round_trips_and_refuses_paths_outside_the_folder()
    {
        var book = new ScrolllerCacheBook();
        var file = ScrolllerDiskCache.FileNameFor("https://a.scrolller.com/1.jpg");
        book.Put(new ScrolllerCacheEntry("https://a.scrolller.com/1.jpg", Path.Combine(_root, file), 10, _now,
            new ScrolllerCacheMeta("picture", "sub", null, 640, 480, "t")));
        var back = ScrolllerCacheBook.FromJson(book.ToJson(), _root);
        var e = back.Get("https://a.scrolller.com/1.jpg");
        Assert.NotNull(e);
        Assert.Equal(Path.Combine(_root, file), e!.Path);
        Assert.Equal("sub", e.Meta.Sub);
        Assert.Equal(_now, e.LastUsedUtc);

        var evil = "{\"entries\":[{\"u\":\"https://x.scrolller.com/a.jpg\",\"f\":\"..\\\\a.jpg\",\"b\":5}]}";
        Assert.Equal(0, ScrolllerCacheBook.FromJson(evil, _root).Count);
        Assert.Equal(0, ScrolllerCacheBook.FromJson("not json", _root).Count);
    }

    [Fact]
    public void Meta_falls_back_to_a_guess_from_the_extension()
    {
        Assert.Equal("clip", ScrolllerDiskCache.ResolveMeta("https://v.scrolller.com/q1.mp4", null).Kind);
        Assert.Equal("gif", ScrolllerDiskCache.ResolveMeta("https://i.scrolller.com/q2.gif", null).Kind);
        Assert.Equal("picture", ScrolllerDiskCache.ResolveMeta("https://i.scrolller.com/q3.jpg", null).Kind);
        Assert.Equal("poster", ScrolllerDiskCache.ResolveMeta("https://i.scrolller.com/q4.jpg", new ScrolllerCacheMeta("poster")).Kind);
    }
}
