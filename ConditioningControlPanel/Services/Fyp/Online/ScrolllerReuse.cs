using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// The reuse mix (owner, 2026-10-19): Scrolller media already on this device is played again
/// instead of fetching new media every time, so the daily share stretches. Session surfaces
/// (flashes, videos, Back Room, DtRH, Goon, wallpaper, Arcademy, intake) swap about
/// 60% of a batch for cached files of the same niches; the For You reel keeps about 85% fresh.
/// When a fresh fetch is not allowed (<see cref="ScrolllerPolicyService.MayFetchFresh"/>: share
/// spent or nobody watching) a batch is cached files only, or empty, and nothing is fetched.
///
/// Everything here is pure or a bounded thread-safe map, so the shares and the picks are tested
/// without a cache on disk. <see cref="FypOnlineCoordinator"/> wires it to
/// <see cref="ScrolllerDiskCache.Entries"/>.
/// </summary>
internal static class ScrolllerReuse
{
    /// <summary>Share of a For You reel batch that stays fresh.</summary>
    public const double ReelFreshShare = 0.85;

    /// <summary>Share of a session-surface batch that stays fresh (the rest comes from the cache).</summary>
    public const double SessionFreshShare = 0.40;

    /// <summary>Size of a batch made only of cached files (share spent or user away).</summary>
    public const int CachedOnlyBatchSize = 12;

    /// <summary>A cached clip offered to a full-size video consumer must reach this long side:
    /// the card-sized rendition (<see cref="FypAssetManifest.Entry.SmallUrl"/>, at most 640 wide)
    /// is cached too, and it looks like a bug on a full screen.</summary>
    public const int MinVideoLongSide = 720;

    /// <summary>Any Scrolller media host, for asking <see cref="ScrolllerPolicyService.MayFetchFresh"/>
    /// about "media in general" before a batch is built.</summary>
    internal const string MediaProbeUrl = "https://media.scrolller.com/";

    // Cache-meta kinds (ScrolllerCacheMeta.Kind).
    public const string KindClip = "clip";
    public const string KindGif = "gif";
    public const string KindPicture = "picture";
    public const string KindPoster = "poster";

    /// <summary>Fresh share for one coordinator tenant. Only the For You reel is a reel; every
    /// other tenant (and any id this list does not know) is a session surface.</summary>
    public static double FreshShareFor(string? consumerId)
        => string.Equals(consumerId, FypOnlineCoordinator.FypConsumerId, StringComparison.OrdinalIgnoreCase)
            ? ReelFreshShare
            : SessionFreshShare;

    /// <summary>How many of a <paramref name="batchSize"/> batch go to cached files.</summary>
    public static int CachedSlots(int batchSize, double freshShare, int cachedAvailable)
    {
        if (batchSize <= 0 || cachedAvailable <= 0) return 0;
        double reuse = 1 - Math.Clamp(freshShare, 0, 1);
        int want = (int)Math.Round(batchSize * reuse, MidpointRounding.AwayFromZero);
        return Math.Min(want, cachedAvailable);
    }

    /// <summary>
    /// Swaps the tail of <paramref name="fresh"/> for <paramref name="cached"/> picks (up to
    /// <see cref="CachedSlots"/>) and shuffles, so a consumer that takes only the first few
    /// still gets the same mix. <paramref name="dropped"/> are the fresh items left out, which
    /// the caller hands back to its rotation.
    /// </summary>
    public static List<T> Mix<T>(IReadOnlyList<T> fresh, IReadOnlyList<T> cached, double freshShare,
        Random rng, out List<T> dropped)
    {
        int take = CachedSlots(fresh.Count, freshShare, cached.Count);
        int keep = fresh.Count - take;
        var result = fresh.Take(keep).Concat(cached.Take(take)).ToList();
        dropped = fresh.Skip(keep).ToList();
        Shuffle(result, rng);
        return result;
    }

    /// <summary>
    /// Up to <paramref name="count"/> candidates, ones this tenant has not been served in the
    /// current cycle first. When every candidate has had its turn the cycle starts over (keeping
    /// this pick marked), so a small cache rotates instead of repeating one file.
    /// </summary>
    public static List<T> PickCycle<T>(IReadOnlyList<T> candidates, Func<T, string> key, int count,
        HashSet<string> served, Random rng)
    {
        if (count <= 0 || candidates.Count == 0) return new List<T>();
        var unseen = candidates.Where(c => !served.Contains(key(c))).ToList();
        var seen = candidates.Where(c => served.Contains(key(c))).ToList();
        Shuffle(unseen, rng);
        Shuffle(seen, rng);
        var pick = unseen.Concat(seen).Take(count).ToList();
        foreach (var p in pick) served.Add(key(p));
        if (candidates.All(c => served.Contains(key(c))))
        {
            served.Clear();
            foreach (var p in pick) served.Add(key(p));
        }
        return pick;
    }

    /// <summary>Does a cached file of this kind suit a consumer asking for <paramref name="kind"/>?
    /// Mirrors what the fresh fetch would hand that consumer: the Back Room takes GIF clips only,
    /// never a full-length upload; still surfaces never get a clip.</summary>
    public static bool KindFits(FeedMediaKind kind, ScrolllerCacheMeta meta)
    {
        switch (kind)
        {
            case FeedMediaKind.Video:
                if (meta.Kind != KindClip && meta.Kind != KindGif) return false;
                return meta.Width is not int w || meta.Height is not int h || Math.Max(w, h) >= MinVideoLongSide;
            case FeedMediaKind.GifClip: return meta.Kind == KindGif;
            case FeedMediaKind.Image: return meta.Kind == KindPicture;
            case FeedMediaKind.GifStill: return meta.Kind == KindPoster;
            default: return meta.Kind is KindClip or KindGif or KindPicture;
        }
    }

    /// <summary>The cache label for a mapped item: the filter it came from, and whether the
    /// still or the clip half of the post was taken.</summary>
    public static string LabelFor(string? filter, bool stills)
        => stills
            ? (filter == "PICTURE" ? KindPicture : KindPoster)
            : (filter == "GIF" ? KindGif : KindClip);

    /// <summary>Labels every url of one mapped entry (<see cref="ScrolllerItemIndex"/>) and
    /// remembers its post id, so a file cached from it can be offered again by niche.</summary>
    public static void Label(FypAssetManifest.Entry e, string sub, string kind, int? smallWidth, int? smallHeight)
    {
        bool clip = kind is KindClip or KindGif;
        var title = string.IsNullOrEmpty(e.Filename) ? null : e.Filename;
        ScrolllerItemIndex.Remember(e.Url, new ScrolllerCacheMeta(kind, sub, clip ? e.PosterUrl : null, e.Width, e.Height, title));
        RememberPost(e.Url, e.Id);
        if (!string.IsNullOrEmpty(e.SmallUrl))
        {
            ScrolllerItemIndex.Remember(e.SmallUrl, new ScrolllerCacheMeta(kind, sub, clip ? e.PosterUrl : null, smallWidth, smallHeight, title));
            RememberPost(e.SmallUrl, e.Id);
        }
        if (clip && !string.IsNullOrEmpty(e.PosterUrl))
        {
            ScrolllerItemIndex.Remember(e.PosterUrl, new ScrolllerCacheMeta(KindPoster, sub, null, null, null, title));
            RememberPost(e.PosterUrl, e.Id);
        }
    }

    /// <summary>
    /// A cached file as a feed entry for <paramref name="kind"/>, or null when it does not fit.
    /// The id is the post's own when this run mapped it (so a consumer's dedupe sees one post,
    /// not two), else a stable one from the url. <paramref name="posterUsable"/> says whether
    /// the clip's poster can be had (cached, or a fresh fetch is allowed); a poster that cannot
    /// is left off rather than handed out as a url that will never load.
    /// </summary>
    public static FypAssetManifest.Entry? ToEntry(ScrolllerCacheEntry c, FeedMediaKind kind, Func<string, bool> posterUsable)
    {
        var meta = c.Meta;
        if (meta == null || string.IsNullOrEmpty(c.Url)) return null;
        var sub = FypOnlineCoordinator.SanitizeSub(meta.Sub);
        if (sub == null || !string.Equals(sub, meta.Sub, StringComparison.Ordinal)) return null;
        if (!KindFits(kind, meta)) return null;

        bool clip = meta.Kind is KindClip or KindGif;
        var entry = new FypAssetManifest.Entry
        {
            Id = PostIdFor(c.Url) ?? $"scrolller/{sub}/c{StableHash(c.Url)}",
            Url = c.Url,
            Type = clip ? RemoteMediaFormats.TypeVideo : RemoteMediaFormats.TypeImage,
            Filename = string.IsNullOrEmpty(meta.Title) ? $"r/{sub}" : meta.Title!,
            Folder = "r/" + sub,
            Width = meta.Width,
            Height = meta.Height,
            Origin = RemoteMediaFormats.OriginOnline,
            PosterUrl = clip && !string.IsNullOrEmpty(meta.Poster) && posterUsable(meta.Poster!) ? meta.Poster : null,
        };
        return RemoteMediaFormats.Validate(entry, kind, out _) ? entry : null;
    }

    // ---- url -> post id (this run only, bounded like ScrolllerItemIndex) ----------------

    public const int MaxPostIds = 20000;
    private static readonly ConcurrentDictionary<string, string> PostIds = new(StringComparer.Ordinal);
    private static int _postCount;

    public static void RememberPost(string? url, string? id)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(id)) return;
        if (Volatile.Read(ref _postCount) >= MaxPostIds) { PostIds.Clear(); Volatile.Write(ref _postCount, 0); }
        if (PostIds.TryAdd(url!, id!)) Interlocked.Increment(ref _postCount);
        else PostIds[url!] = id!;
    }

    public static string? PostIdFor(string? url)
        => !string.IsNullOrEmpty(url) && PostIds.TryGetValue(url!, out var id) ? id : null;

    /// <summary>FNV-1a 64 of the url, hex: the same file gets the same id across restarts.</summary>
    internal static string StableHash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char ch in s) { h ^= ch; h *= 1099511628211UL; }
        return h.ToString("x16");
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
