using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The reuse mix (owner, 2026-10-19): session surfaces swap about 60% of a batch for cached
/// Scrolller files of the same niches, the For You reel keeps about 85% fresh, and a spent share
/// or an away user means cached only, never a fetch. Also the url labels every mapped feed item
/// leaves for the device cache, and the GIF rule (rendition or poster, never a real .gif).
/// </summary>
[Collection(ScrolllerPolicyCollection.Name)]
public sealed class ScrolllerReuseTests : IDisposable
{
    public void Dispose() => ScrolllerPolicy.Away = false;

    // ---- shares ------------------------------------------------------------------------

    [Theory]
    [InlineData("fyp", ScrolllerReuse.ReelFreshShare)]
    [InlineData("FYP", ScrolllerReuse.ReelFreshShare)]
    [InlineData("flashes", ScrolllerReuse.SessionFreshShare)]
    [InlineData("backroom", ScrolllerReuse.SessionFreshShare)]
    [InlineData("dtrh-loops", ScrolllerReuse.SessionFreshShare)]
    [InlineData("goon-peer-clips", ScrolllerReuse.SessionFreshShare)]
    [InlineData("wallpaper", ScrolllerReuse.SessionFreshShare)]
    [InlineData("something-new", ScrolllerReuse.SessionFreshShare)]
    [InlineData(null, ScrolllerReuse.SessionFreshShare)]
    public void FreshShare_OnlyTheReelIsAReel(string? consumer, double expected)
        => Assert.Equal(expected, ScrolllerReuse.FreshShareFor(consumer));

    [Theory]
    [InlineData(10, ScrolllerReuse.SessionFreshShare, 100, 6)]
    [InlineData(30, ScrolllerReuse.SessionFreshShare, 100, 18)]
    [InlineData(20, ScrolllerReuse.ReelFreshShare, 100, 3)]
    [InlineData(30, ScrolllerReuse.ReelFreshShare, 100, 5)]
    [InlineData(10, ScrolllerReuse.SessionFreshShare, 2, 2)]   // only what the cache holds
    [InlineData(10, ScrolllerReuse.SessionFreshShare, 0, 0)]
    [InlineData(0, ScrolllerReuse.SessionFreshShare, 100, 0)]
    public void CachedSlots_FollowTheShare(int batch, double freshShare, int available, int expected)
        => Assert.Equal(expected, ScrolllerReuse.CachedSlots(batch, freshShare, available));

    [Fact]
    public void Mix_Session_SwapsSixOfTen_AndReportsTheDropped()
    {
        var fresh = Enumerable.Range(0, 10).Select(i => "f" + i).ToList();
        var cached = Enumerable.Range(0, 10).Select(i => "c" + i).ToList();
        var mixed = ScrolllerReuse.Mix(fresh, cached, ScrolllerReuse.SessionFreshShare, new Random(7), out var dropped);
        Assert.Equal(10, mixed.Count);
        Assert.Equal(6, mixed.Count(s => s[0] == 'c'));
        Assert.Equal(6, dropped.Count);
        Assert.All(dropped, d => Assert.DoesNotContain(d, mixed));
    }

    [Fact]
    public void Mix_Reel_KeepsMostFresh()
    {
        var fresh = Enumerable.Range(0, 20).Select(i => "f" + i).ToList();
        var cached = Enumerable.Range(0, 20).Select(i => "c" + i).ToList();
        var mixed = ScrolllerReuse.Mix(fresh, cached, ScrolllerReuse.ReelFreshShare, new Random(7), out _);
        Assert.Equal(17, mixed.Count(s => s[0] == 'f'));
    }

    [Fact]
    public void Mix_IsShuffled_SoAConsumerTakingFourStillGetsTheMix()
    {
        // FlashService takes the first four of each batch: across many batches the cached share
        // it sees must sit near 60%, not 0% (cached appended at the tail, unshuffled).
        var rng = new Random(11);
        int cachedSeen = 0, total = 0;
        for (int b = 0; b < 400; b++)
        {
            var fresh = Enumerable.Range(0, 30).Select(i => "f" + i).ToList();
            var cached = Enumerable.Range(0, 30).Select(i => "c" + i).ToList();
            var take = ScrolllerReuse.Mix(fresh, cached, ScrolllerReuse.SessionFreshShare, rng, out _).Take(4).ToList();
            cachedSeen += take.Count(s => s[0] == 'c');
            total += take.Count;
        }
        double share = cachedSeen / (double)total;
        Assert.InRange(share, 0.52, 0.68);
    }

    [Fact]
    public void Mix_WithAnEmptyCache_IsTheFreshBatch()
    {
        var fresh = new List<string> { "a", "b", "c" };
        var mixed = ScrolllerReuse.Mix(fresh, new List<string>(), ScrolllerReuse.SessionFreshShare, new Random(1), out var dropped);
        Assert.Equal(fresh.OrderBy(s => s), mixed.OrderBy(s => s));
        Assert.Empty(dropped);
    }

    [Fact]
    public void PickCycle_ServesEveryFileBeforeRepeatingOne()
    {
        var candidates = Enumerable.Range(0, 5).Select(i => "u" + i).ToList();
        var served = new HashSet<string>();
        var rng = new Random(3);
        var first = ScrolllerReuse.PickCycle(candidates, s => s, 2, served, rng);
        var second = ScrolllerReuse.PickCycle(candidates, s => s, 2, served, rng);
        var third = ScrolllerReuse.PickCycle(candidates, s => s, 1, served, rng);
        var all = first.Concat(second).Concat(third).ToList();
        Assert.Equal(5, all.Distinct().Count());
        // The cycle restarted, keeping the last pick marked so it does not come straight back.
        Assert.Equal(third, served.ToList());
    }

    [Fact]
    public void PickCycle_ASmallCache_StillFillsWhatItCan()
    {
        var served = new HashSet<string>();
        var pick = ScrolllerReuse.PickCycle(new[] { "a", "b" }, s => s, 12, served, new Random(1));
        Assert.Equal(2, pick.Count);
    }

    // ---- kinds -------------------------------------------------------------------------

    [Theory]
    [InlineData("Video", "clip", true)]
    [InlineData("Video", "gif", true)]
    [InlineData("Video", "picture", false)]
    [InlineData("GifClip", "gif", true)]
    [InlineData("GifClip", "clip", false)]   // the Back Room never takes a full upload
    [InlineData("Image", "picture", true)]
    [InlineData("Image", "poster", false)]
    [InlineData("GifStill", "poster", true)]
    [InlineData("GifStill", "gif", false)]
    [InlineData("Any", "picture", true)]
    [InlineData("Any", "poster", false)]
    public void KindFits_MirrorsWhatTheFreshFetchWouldHand(string kind, string metaKind, bool fits)
        => Assert.Equal(fits, ScrolllerReuse.KindFits(Enum.Parse<FeedMediaKind>(kind), new ScrolllerCacheMeta(metaKind, "sub")));

    [Fact]
    public void KindFits_AFullScreenVideoConsumerSkipsTheCardSizedRendition()
    {
        Assert.False(ScrolllerReuse.KindFits(FeedMediaKind.Video, new ScrolllerCacheMeta("clip", "s", null, 640, 360)));
        Assert.True(ScrolllerReuse.KindFits(FeedMediaKind.Video, new ScrolllerCacheMeta("clip", "s", null, 1280, 720)));
        Assert.True(ScrolllerReuse.KindFits(FeedMediaKind.GifClip, new ScrolllerCacheMeta("gif", "s", null, 480, 270)));
    }

    // ---- cached file -> entry ----------------------------------------------------------

    private static ScrolllerCacheEntry Cached(string url, ScrolllerCacheMeta meta)
        => new(url, "C:/cache/" + ScrolllerReuse.StableHash(url), 1000, DateTime.UtcNow, meta);

    [Fact]
    public void ToEntry_ACachedGif_IsAClipWithItsPoster()
    {
        var c = Cached("https://v.scrolller.com/a-480x270.mp4",
            new ScrolllerCacheMeta("gif", "hentai", "https://i.scrolller.com/a-480x270.webp", 480, 270, "t"));
        var e = ScrolllerReuse.ToEntry(c, FeedMediaKind.GifClip, _ => true);
        Assert.NotNull(e);
        Assert.Equal(RemoteMediaFormats.TypeVideo, e!.Type);
        Assert.Equal(c.Url, e.Url);
        Assert.Equal("https://i.scrolller.com/a-480x270.webp", e.PosterUrl);
        Assert.Equal("r/hentai", e.Folder);
        Assert.Equal(RemoteMediaFormats.OriginOnline, e.Origin);
        Assert.DoesNotContain(':', e.Id);
        Assert.StartsWith("scrolller/hentai/", e.Id);
    }

    [Fact]
    public void ToEntry_APosterThatCannotBeHad_IsLeftOff()
    {
        var c = Cached("https://v.scrolller.com/b-480x270.mp4",
            new ScrolllerCacheMeta("gif", "hentai", "https://i.scrolller.com/b.webp"));
        Assert.Null(ScrolllerReuse.ToEntry(c, FeedMediaKind.GifClip, _ => false)!.PosterUrl);
    }

    [Fact]
    public void ToEntry_KeepsThePostIdWhenThisRunMappedIt()
    {
        var url = "https://v.scrolller.com/c-480x270.mp4";
        ScrolllerReuse.RememberPost(url, "scrolller/hentai/123");
        var e = ScrolllerReuse.ToEntry(Cached(url, new ScrolllerCacheMeta("gif", "hentai")), FeedMediaKind.GifClip, _ => true);
        Assert.Equal("scrolller/hentai/123", e!.Id);
    }

    [Fact]
    public void ToEntry_RefusesAWrongKindOrAMissingNiche()
    {
        Assert.Null(ScrolllerReuse.ToEntry(Cached("https://v.scrolller.com/d.mp4", new ScrolllerCacheMeta("clip", "x1")), FeedMediaKind.Image, _ => true));
        Assert.Null(ScrolllerReuse.ToEntry(Cached("https://v.scrolller.com/d.mp4", new ScrolllerCacheMeta("clip", null)), FeedMediaKind.Video, _ => true));
        Assert.Null(ScrolllerReuse.ToEntry(Cached("https://v.scrolller.com/d.mp4", new ScrolllerCacheMeta("clip", "a:b")), FeedMediaKind.Video, _ => true));
    }

    // ---- mapping labels + the GIF rule -------------------------------------------------

    private static JObject Item(long id, params string[] urls)
    {
        var sources = new JArray();
        foreach (var u in urls) sources.Add(new JObject { ["url"] = u, ["width"] = 480, ["height"] = 270 });
        return new JObject { ["id"] = id, ["title"] = "post " + id, ["mediaSources"] = sources };
    }

    [Fact]
    public void GifPost_ForAClipConsumer_IsTheVideoRendition_LabelledGif()
    {
        var item = Item(901, "https://v.scrolller.com/g901.gif", "https://v.scrolller.com/g901_thumb.webm",
            "https://v.scrolller.com/g901.mp4", "https://i.scrolller.com/g901.webp");
        var e = ScrolllerSource.MapItem(item, "hentai", stills: false, filter: "GIF");
        Assert.NotNull(e);
        Assert.Equal("https://v.scrolller.com/g901.mp4", e!.Url);
        Assert.Equal("https://i.scrolller.com/g901.webp", e.PosterUrl);
        var meta = ScrolllerItemIndex.MetaFor(e.Url);
        Assert.Equal(new ScrolllerCacheMeta("gif", "hentai", e.PosterUrl, 480, 270, "post 901"), meta);
        Assert.Equal("poster", ScrolllerItemIndex.MetaFor(e.PosterUrl)!.Kind);
        Assert.Equal("scrolller/hentai/901", ScrolllerReuse.PostIdFor(e.PosterUrl));
    }

    [Fact]
    public void GifPost_ForAStillConsumer_IsThePoster_NeverTheGif()
    {
        var item = Item(902, "https://v.scrolller.com/g902.gif", "https://v.scrolller.com/g902.mp4",
            "https://i.scrolller.com/g902.webp");
        var e = ScrolllerSource.MapItem(item, "hentai", stills: true, filter: "GIF");
        Assert.Equal("https://i.scrolller.com/g902.webp", e!.Url);
        Assert.Equal("poster", ScrolllerItemIndex.MetaFor(e.Url)!.Kind);
    }

    [Fact]
    public void GifPost_WithOnlyARealGif_IsDropped()
    {
        Assert.Null(ScrolllerSource.MapItem(Item(903, "https://v.scrolller.com/g903.gif"), "hentai", stills: false, filter: "GIF"));
        Assert.Null(ScrolllerSource.MapItem(Item(904, "https://v.scrolller.com/g904.gif"), "hentai", stills: true, filter: "PICTURE"));
    }

    [Theory]
    [InlineData("VIDEO", false, "clip")]
    [InlineData("GIF", false, "gif")]
    [InlineData("PICTURE", true, "picture")]
    [InlineData("GIF", true, "poster")]
    [InlineData(null, false, "clip")]
    public void LabelFor_NamesWhatWasTaken(string? filter, bool stills, string expected)
        => Assert.Equal(expected, ScrolllerReuse.LabelFor(filter, stills));

    [Fact]
    public void PictureAndVideoPosts_AreLabelledToo()
    {
        var pic = ScrolllerSource.MapItem(Item(905, "https://i.scrolller.com/p905.jpg"), "bimbo", stills: true, filter: "PICTURE");
        Assert.Equal("picture", ScrolllerItemIndex.MetaFor(pic!.Url)!.Kind);
        Assert.Equal("bimbo", ScrolllerItemIndex.MetaFor(pic.Url)!.Sub);

        var clip = ScrolllerSource.MapItem(Item(906, "https://v.scrolller.com/v906.webm", "https://i.scrolller.com/v906.jpg"),
            "bimbo", stills: false, filter: "VIDEO");
        Assert.Equal("clip", ScrolllerItemIndex.MetaFor(clip!.Url)!.Kind);
    }

    // ---- spent / away: cached only, never a fetch --------------------------------------

    [Fact]
    public void Away_ABatchIsCachedOnly_AndWithAnEmptyCacheItIsDryWithNoFetch()
    {
        ScrolllerPolicy.Away = true;
        Assert.False(ScrolllerPolicyService.MayFetchFresh(ScrolllerReuse.MediaProbeUrl));
        var coord = FypOnlineCoordinator.For("reuse-test-away", () => new[] { "hentai" }, FeedMediaKind.GifClip);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var batch = coord.FetchBatchDetailedAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.Empty(batch.Entries);
        Assert.Null(batch.Error);
        Assert.True(batch.Dry);
        // A real page request waits on the 1.1 s politeness gate and then the network.
        Assert.True(sw.ElapsedMilliseconds < 1000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void MediaProbeUrl_IsAScrolllerHost()
        => Assert.True(global::ConditioningControlPanel.Services.Video.ScrolllerCredit.IsScrolllerUrl(ScrolllerReuse.MediaProbeUrl));
}
