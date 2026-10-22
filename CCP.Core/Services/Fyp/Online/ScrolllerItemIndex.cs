using System.Collections.Concurrent;
using System.Threading;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// What each Scrolller media url is (kind, subreddit, poster), remembered when a feed item is
/// mapped so the device cache can label a file it stores without every fetcher passing it along.
/// Bounded: past <see cref="MaxEntries"/> it starts over (a missing label only means a cached
/// file is not offered for reuse by niche). Thread-safe, never throws.
/// </summary>
internal static class ScrolllerItemIndex
{
    public const int MaxEntries = 20000;
    private static readonly ConcurrentDictionary<string, ScrolllerCacheMeta> Map = new();
    private static int _count;

    public static void Remember(string? url, ScrolllerCacheMeta meta)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (Volatile.Read(ref _count) >= MaxEntries) { Map.Clear(); Volatile.Write(ref _count, 0); }
        if (Map.TryAdd(url!, meta)) Interlocked.Increment(ref _count);
        else Map[url!] = meta;
    }

    public static ScrolllerCacheMeta? MetaFor(string? url)
        => !string.IsNullOrEmpty(url) && Map.TryGetValue(url!, out var m) ? m : null;
}
