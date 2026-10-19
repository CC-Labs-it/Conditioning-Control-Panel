using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>What a cached Scrolller file is, kept beside it so a reuse pick can honour the
/// user's niches. <see cref="Kind"/> is "clip", "gif", "picture" or "poster".</summary>
internal sealed record ScrolllerCacheMeta(string Kind, string? Sub = null, string? Poster = null,
    int? Width = null, int? Height = null, string? Title = null);

/// <summary>One complete cached file.</summary>
internal sealed record ScrolllerCacheEntry(string Url, string Path, long Bytes, DateTime LastUsedUtc, ScrolllerCacheMeta Meta);

/// <summary>A download being written into the cache as it is read (a tee). Dispose without
/// <see cref="Commit"/> throws the partial file away.</summary>
internal interface IScrolllerCacheWriter : IDisposable
{
    Stream Stream { get; }
    /// <summary>The download finished whole: the file becomes a cache entry.</summary>
    void Commit();
}

/// <summary>
/// Scrolller media kept on the USER'S disk across sessions and restarts (owner, 2026-10-19: reuse
/// what was already seen, so the daily share stretches and Scrolller serves less). A replay from
/// here costs nothing against <see cref="ScrolllerAllowance"/>. Bounded LRU, about
/// <see cref="DefaultCapBytes"/>, under the assets drive. The Scrolller licence covers keeping
/// media cached on the user's device; nothing here ever leaves this machine.
///
/// CONTRACT (lanes build against these signatures; the cache lane owns the implementation):
/// every member is thread-safe and never throws. A fresh download honours
/// <see cref="ScrolllerPolicyService.MayFetchFresh"/>, sends the honest user-agent, and charges
/// the allowance with the bytes actually received.
/// </summary>
internal static class ScrolllerDiskCache
{
    public const long DefaultCapBytes = 2048L * 1024 * 1024;

    /// <summary>Path of a complete cached copy of <paramref name="url"/>, or null. A hit counts as
    /// a use (LRU). Null while the switch is off: cached copies stop too.</summary>
    public static string? TryGetPath(string? url) => null;

    /// <summary>True when a complete copy is cached (no LRU touch).</summary>
    public static bool Contains(string? url) => false;

    /// <summary>The cached copy, else a fresh download into the cache (refused, null, when
    /// <see cref="ScrolllerPolicyService.MayFetchFresh"/> says no). Null on any failure.</summary>
    public static Task<string?> GetOrFetchAsync(string url, ScrolllerCacheMeta? meta, CancellationToken ct)
        => Task.FromResult<string?>(null);

    /// <summary>Starts a tee for a download the caller makes itself (the caller charges the
    /// allowance). Null when the cache cannot take it.</summary>
    public static IScrolllerCacheWriter? BeginWrite(string url, ScrolllerCacheMeta? meta) => null;

    /// <summary>Complete entries, most recently used first, optionally filtered. For reuse picks.</summary>
    public static IReadOnlyList<ScrolllerCacheEntry> Entries(Func<ScrolllerCacheEntry, bool>? filter = null)
        => Array.Empty<ScrolllerCacheEntry>();
}
