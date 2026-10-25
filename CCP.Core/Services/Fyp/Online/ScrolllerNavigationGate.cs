using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>Navigation fallback for heads without response interception. WPF ScrolllerWebGate
/// remains the full reference: embedded requests cannot be fenced by a navigation event.</summary>
internal static class ScrolllerNavigationGate
{
    internal const long MaxBodyBytes = 64L * 1024 * 1024;
    internal const string OurReferer = "https://cclabs.app/";

    internal static bool IsScrolller(Uri uri) => uri.IsAbsoluteUri
        && (uri.Scheme is "http" or "https" or "ws" or "wss")
        && (uri.Host.TrimEnd('.').Equals("scrolller.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.TrimEnd('.').EndsWith(".scrolller.com", StringComparison.OrdinalIgnoreCase));

    internal static string? MediaKind(Uri uri) => Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
    {
        ".mp4" or ".webm" or ".m4v" or ".mov" => "video",
        ".gif" => "gif",
        ".webp" or ".jpg" or ".jpeg" or ".png" or ".avif" => "picture",
        _ => null
    };

    internal static bool IsMedia(Uri uri) => IsScrolller(uri) && uri.Scheme is "http" or "https"
        && !uri.Host.TrimEnd('.').Equals("api.scrolller.com", StringComparison.OrdinalIgnoreCase)
        && MediaKind(uri) != null;

    internal static bool AllowsNavigation(Uri uri) => !IsScrolller(uri) || ScrolllerPolicy.Enabled;

    /// <summary>Cached beats spent/away. Fresh bytes use the existing per-account charging,
    /// consent, day expiry, format check and incomplete-download cleanup in the device cache.
    /// ponytail: clips fetch whole here; this API cannot meter the native player's received bytes.</summary>
    internal static async Task<string?> ResolveAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsMedia(uri)
            || !ScrolllerPolicy.Enabled || ct.IsCancellationRequested) return null;
        // A cold cache is not a miss: await its index before applying the fresh-fetch gate.
        try { await ScrolllerDiskCache.WarmAsync().WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        var cached = ScrolllerDiskCache.TryGetPath(url);
        if (cached != null) return cached;
        if (!ScrolllerPolicyService.MayFetchFresh(url)) return null;
        return await ScrolllerDiskCache.GetOrFetchAsync(url, new ScrolllerCacheMeta(MediaKind(uri)!),
            MaxBodyBytes, ct).ConfigureAwait(false);
    }
}
