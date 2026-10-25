using System;
using System.IO;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>What the gate does with one web view request.</summary>
internal enum ScrolllerWebDecision
{
    /// <summary>Not a Scrolller host: left alone.</summary>
    Pass,
    /// <summary>Scrolller host while the licence switch is off: answered locally with an empty 403.</summary>
    Block,
    /// <summary>Scrolller host while on: goes out with the app's honest user-agent.</summary>
    Rewrite,
}

/// <summary>What a Scrolller media request is, from its url (or the page's resource context).</summary>
internal enum ScrolllerMediaKind { Other, Picture, Gif, Clip }

/// <summary>Where one Scrolller request goes once the switch is on (see <see cref="ScrolllerWebRules.Route"/>).</summary>
internal enum ScrolllerWebRoute
{
    /// <summary>Not Scrolller: left alone.</summary>
    Pass,
    /// <summary>Switch off: local 403.</summary>
    Block,
    /// <summary>API calls and anything that is not media: honest UA, not charged.</summary>
    Rewrite,
    /// <summary>Media, not cached, and no fresh fetch allowed (share used up, or away): local 403.</summary>
    Refuse,
    /// <summary>Media with a complete cached copy: answered from the file, zero network, zero charge.</summary>
    ServeCached,
    /// <summary>Picture or GIF: the app fetches it whole, tees it into the cache, charges, then serves it.</summary>
    FetchWhole,
    /// <summary>Clip or ranged request: goes out (honest UA) and is charged by the response's length.</summary>
    PassCharged,
}

/// <summary>A byte range of a file, both ends inclusive.</summary>
internal readonly record struct ScrolllerByteRange(long Start, long End)
{
    public long Length => End - Start + 1;
}

/// <summary>
/// The web gate's pure rules, one copy for both heads: WPF's ScrolllerWebGate (WebView2, which can
/// also answer a request) forwards here, and the Avalonia WebHost uses the host, referer and
/// decision rules (its engine can only rewrite request headers). Moved from WPF
/// ScrolllerWebGate.cs (main c45d58a65, f7102be55, 92fdebaa4) unchanged.
/// </summary>
internal static class ScrolllerWebRules
{
    internal const string OurReferer = "https://cclabs.app/";

    /// <summary>An open-ended Range (<c>bytes=N-</c>) on a cached file gets at most this much.</summary>
    internal const long SliceBytes = 2L * 1024 * 1024;

    /// <summary>Largest body the gate holds in memory (an explicit range, or a whole fetch).</summary>
    internal const long MaxBodyBytes = 64L * 1024 * 1024;

    /// <summary>scrolller.com or any subdomain of it; never a lookalike such as notscrolller.com.</summary>
    internal static bool IsScrolllerHost(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        var h = host.TrimEnd('.');
        return h.Equals("scrolller.com", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".scrolller.com", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsApiHost(string? host)
        => host != null && host.TrimEnd('.').Equals("api.scrolller.com", StringComparison.OrdinalIgnoreCase);

    internal static ScrolllerWebDecision Decide(string? requestUri, bool enabled)
    {
        if (string.IsNullOrEmpty(requestUri)) return ScrolllerWebDecision.Pass;
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)) return ScrolllerWebDecision.Pass;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp
            && uri.Scheme != "wss" && uri.Scheme != "ws") return ScrolllerWebDecision.Pass;
        if (!IsScrolllerHost(uri.Host)) return ScrolllerWebDecision.Pass;
        return enabled ? ScrolllerWebDecision.Rewrite : ScrolllerWebDecision.Block;
    }

    /// <summary>Kind from the url's extension; an unknown extension falls back to the page's
    /// resource context (a &lt;video&gt; is a clip, an &lt;img&gt; a picture).</summary>
    internal static ScrolllerMediaKind KindOf(string? requestUri, bool mediaContext = false, bool imageContext = false)
    {
        switch (ExtensionOf(requestUri))
        {
            case ".mp4": case ".webm": case ".m4v": case ".mov": return ScrolllerMediaKind.Clip;
            case ".gif": return ScrolllerMediaKind.Gif;
            case ".webp": case ".jpg": case ".jpeg": case ".png": case ".avif": return ScrolllerMediaKind.Picture;
        }
        if (mediaContext) return ScrolllerMediaKind.Clip;
        if (imageContext) return ScrolllerMediaKind.Picture;
        return ScrolllerMediaKind.Other;
    }

    /// <summary>The whole routing rule, pure. Cached beats everything (free); then the share;
    /// then clips and ranged requests go out charged, pictures and GIFs are fetched whole.</summary>
    internal static ScrolllerWebRoute Route(string? requestUri, string? method, bool enabled, bool cached,
        bool mayFetchFresh, bool hasRange, ScrolllerMediaKind kind)
    {
        switch (Decide(requestUri, enabled))
        {
            case ScrolllerWebDecision.Pass: return ScrolllerWebRoute.Pass;
            case ScrolllerWebDecision.Block: return ScrolllerWebRoute.Block;
        }
        var uri = new Uri(requestUri!);
        if (!string.Equals(method ?? "GET", "GET", StringComparison.OrdinalIgnoreCase)
            || IsApiHost(uri.Host) || kind == ScrolllerMediaKind.Other
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return ScrolllerWebRoute.Rewrite;
        if (cached) return ScrolllerWebRoute.ServeCached;
        if (!mayFetchFresh) return ScrolllerWebRoute.Refuse;
        if (hasRange || kind == ScrolllerMediaKind.Clip) return ScrolllerWebRoute.PassCharged;
        return ScrolllerWebRoute.FetchWhole;
    }

    /// <summary>
    /// Parses one <c>Range: bytes=...</c> against a file of <paramref name="total"/> bytes.
    /// False = serve the whole file with a 200 (no header, junk, several ranges). True with
    /// <paramref name="unsatisfiable"/> = answer 416. An open end is capped at
    /// <see cref="SliceBytes"/> (the player asks for the next slice), an explicit one at
    /// <see cref="MaxBodyBytes"/>.
    /// </summary>
    internal static bool TryParseRange(string? header, long total, out ScrolllerByteRange range, out bool unsatisfiable)
    {
        range = default;
        unsatisfiable = false;
        if (string.IsNullOrWhiteSpace(header) || total <= 0) return false;
        var h = header.Trim();
        if (!h.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var spec = h.Substring(6).Trim();
        if (spec.Contains(',')) return false;
        var dash = spec.IndexOf('-');
        if (dash < 0) return false;
        var a = spec.Substring(0, dash).Trim();
        var b = spec.Substring(dash + 1).Trim();
        long start, end;
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var suffix) || suffix < 0) return false;
            if (suffix == 0) { unsatisfiable = true; return true; }
            start = Math.Max(0, total - suffix);
            end = total - 1;
            if (end - start + 1 > MaxBodyBytes) start = end - MaxBodyBytes + 1;
        }
        else
        {
            if (!long.TryParse(a, out start) || start < 0) return false;
            if (start >= total) { unsatisfiable = true; return true; }
            if (b.Length == 0) end = Math.Min(total - 1, start + SliceBytes - 1);
            else
            {
                if (!long.TryParse(b, out end) || end < start) return false;
                end = Math.Min(Math.Min(end, total - 1), start + MaxBodyBytes - 1);
            }
        }
        range = new ScrolllerByteRange(start, end);
        return true;
    }

    /// <summary>Bytes a passed-through response carried: Content-Length, else the Content-Range
    /// span, else 0 (not charged; a chunked body tells us nothing up front).</summary>
    internal static long ChargeableLength(string? contentLength, string? contentRange)
    {
        if (long.TryParse(contentLength?.Trim(), out var n) && n > 0) return n;
        var cr = contentRange?.Trim();
        if (cr == null || !cr.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase)) return 0;
        var span = cr.Substring(6).Split('/')[0].Split('-');
        if (span.Length == 2 && long.TryParse(span[0], out var a) && long.TryParse(span[1], out var b) && b >= a)
            return b - a + 1;
        return 0;
    }

    /// <summary>A Referer is ours when it names cclabs.app (or a subdomain) or one of the app's own
    /// virtual hosts (ccp.game, ccp.assets, leash-video.ccp...). Missing counts as ours.</summary>
    internal static bool IsOurReferer(string? referer)
    {
        if (string.IsNullOrEmpty(referer)) return true;
        if (!Uri.TryCreate(referer, UriKind.Absolute, out var uri)) return false;
        var h = uri.Host;
        return h.Equals("cclabs.app", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".cclabs.app", StringComparison.OrdinalIgnoreCase)
            || h.StartsWith("ccp.", StringComparison.OrdinalIgnoreCase)
            || h.EndsWith(".ccp", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The Referer the app's own fetch sends: none when the page sent none, the page's
    /// when it is ours, else ours.</summary>
    internal static string? OutgoingReferer(string? pageReferer)
        => string.IsNullOrEmpty(pageReferer) ? null : IsOurReferer(pageReferer) ? pageReferer : OurReferer;

    /// <summary>What a passed-through response still owes today: never past the file's whole
    /// <paramref name="total"/> for the day; with no known total, only the first response pays.</summary>
    internal static long DedupedCharge(long already, long bytes, long? total)
    {
        if (bytes <= 0) return 0;
        if (total is long t && t > 0) return Math.Max(0, Math.Min(bytes, t - already));
        return already > 0 ? 0 : bytes;
    }

    /// <summary>The whole file's size: a 206's "bytes a-b/TOTAL", a 200's Content-Length, else null.</summary>
    internal static long? TotalSize(int status, string? contentLength, string? contentRange)
    {
        if (status == 206)
        {
            var slash = contentRange?.LastIndexOf('/') ?? -1;
            return slash >= 0 && long.TryParse(contentRange!.AsSpan(slash + 1), out var t) && t > 0 ? t : null;
        }
        return long.TryParse(contentLength, out var n) && n > 0 ? n : null;
    }

    internal static string ExtensionOf(string? requestUri)
    {
        if (string.IsNullOrEmpty(requestUri) || !Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)) return "";
        try { return Path.GetExtension(uri.AbsolutePath).ToLowerInvariant(); }
        catch { return ""; }
    }
}
