using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>Where one relayed Scrolller request goes (<see cref="ScrolllerRelay.Decide"/>).</summary>
internal enum ScrolllerRelayRoute
{
    /// <summary>Not a Scrolller media url: 404, the relay is never an open proxy.</summary>
    NotFound,
    /// <summary>The licence switch is off: 403, nothing leaves the machine.</summary>
    Block,
    /// <summary>A complete cached copy: answered from the file, zero network, zero charge.</summary>
    ServeCached,
    /// <summary>Not cached, a fresh fetch allowed: the device cache downloads it (honest UA, charged), then it is served.</summary>
    FetchThenServe,
    /// <summary>Not cached and no fresh fetch allowed (share used up, book not settled, away): 403, the page falls back.</summary>
    Refuse,
}

/// <summary>
/// The Avalonia head's Scrolller gate for web pages, the same on Windows and Linux (docs/avalonia-decisions.md
/// "scrolller-web-gate"). WPF answers a page's Scrolller requests inside WebView2 (ScrolllerWebGate); this head's web
/// engine cannot answer a request on WebKitGTK, so the host never hands a page a Scrolller CDN url: every frame it
/// posts goes through <see cref="RewriteJson"/>, which turns each Scrolller media url into this head's loopback path
/// <c>/scrolller-media/&lt;sub&gt;/&lt;path&gt;</c> (the Discord Activity's shape, so every page's "via Scrolller" mark
/// still knows it). The asset server answers that path from <see cref="ScrolllerDiskCache"/>: cached copies free,
/// fresh ones fetched by the app with its honest user-agent and charged by the bytes that arrive, refused while the
/// share is spent or the switch is off, and always no-store so the engine keeps no copy of its own.
/// </summary>
internal static class ScrolllerRelay
{
    /// <summary>The loopback path every relayed url starts with.</summary>
    public const string PathPrefix = "/scrolller-media/";

    private static readonly Regex SubLabel = new("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.CultureInvariant);

    /// <summary>The media extensions the relay carries (WPF ScrolllerWebGate.KindOf).</summary>
    internal static bool IsMediaExtension(string? ext) => ext?.ToLowerInvariant() switch
    {
        ".mp4" or ".webm" or ".m4v" or ".mov" or ".gif" or ".webp" or ".jpg" or ".jpeg" or ".png" or ".avif" => true,
        _ => false,
    };

    /// <summary>A Scrolller CDN subdomain: a DNS label, never the API (pages never get it) or the web site.</summary>
    internal static bool IsMediaSub(string? sub)
        => sub != null && SubLabel.IsMatch(sub) && sub != "api" && sub != "www";

    /// <summary>"https://images.scrolller.com/a/b.jpg" -> "/scrolller-media/images/a/b.jpg" (query kept); null when the
    /// url is not Scrolller media (another host, the API, the site, a non-media path).</summary>
    internal static string? ToRelayPath(string? url)
    {
        if (!ScrolllerPolicy.IsScrolllerUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        if (!host.EndsWith(".scrolller.com", StringComparison.Ordinal)) return null;
        var sub = host[..^".scrolller.com".Length];
        if (!IsMediaSub(sub) || !IsMediaExtension(Path.GetExtension(uri.AbsolutePath))) return null;
        return PathPrefix + sub + uri.AbsolutePath + uri.Query;
    }

    /// <summary>The reverse, for the asset server: "/scrolller-media/images/a/b.jpg" -> the https url, or null for
    /// anything that does not map back onto Scrolller media (a lookalike host, a dot-segment escape, a non-media path).</summary>
    internal static string? FromRelayPath(string? pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery) || !pathAndQuery.StartsWith(PathPrefix, StringComparison.Ordinal)) return null;
        var rest = pathAndQuery[PathPrefix.Length..];
        int slash = rest.IndexOf('/');
        if (slash <= 0) return null;
        var sub = rest[..slash].ToLowerInvariant();
        if (!IsMediaSub(sub) || rest.Contains('\\') || rest.Contains('\0')) return null;
        if (!Uri.TryCreate($"https://{sub}.scrolller.com{rest[slash..]}", UriKind.Absolute, out var uri)) return null;
        if (!string.Equals(uri.Host, sub + ".scrolller.com", StringComparison.OrdinalIgnoreCase)) return null;
        if (!IsMediaExtension(Path.GetExtension(uri.AbsolutePath))) return null;
        return uri.AbsoluteUri;
    }

    /// <summary>The whole rule, pure: no Scrolller media url = 404; switch off = 403; cached beats the share; then a
    /// fresh fetch only when allowed.</summary>
    internal static ScrolllerRelayRoute Decide(string? url, bool enabled, bool cached, bool mayFetchFresh)
    {
        if (url == null) return ScrolllerRelayRoute.NotFound;
        if (!enabled) return ScrolllerRelayRoute.Block;
        if (cached) return ScrolllerRelayRoute.ServeCached;
        return mayFetchFresh ? ScrolllerRelayRoute.FetchThenServe : ScrolllerRelayRoute.Refuse;
    }

    /// <summary>
    /// A host -&gt; page frame with every Scrolller media url (any string value, at any depth) turned into its relay
    /// path, absolute on <paramref name="origin"/> (the page's own loopback origin, so the url is as "remote" to the
    /// page's own local-url rules as the CDN url was, and no page archives it as one of its own files); root-relative
    /// when null. A frame that names no Scrolller host, or is not JSON, comes back untouched. Idempotent.
    /// </summary>
    internal static string RewriteJson(string json, string? origin = null)
    {
        if (string.IsNullOrEmpty(json) || json.IndexOf("scrolller.com", StringComparison.OrdinalIgnoreCase) < 0) return json;
        JToken root;
        try
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            root = JToken.ReadFrom(reader);
        }
        catch (JsonException) { return json; }
        bool changed = Walk(root, origin?.TrimEnd('/') ?? "");
        return changed ? root.ToString(Formatting.None) : json;
    }

    private static bool Walk(JToken token, string origin)
    {
        bool changed = false;
        switch (token)
        {
            case JValue { Type: JTokenType.String } v when v.Value is string s && ToRelayPath(s) is { } relay:
                v.Value = origin + relay;
                return true;
            case JContainer c:
                foreach (var child in c.Children()) changed |= Walk(child is JProperty p ? p.Value : child, origin);
                break;
        }
        return changed;
    }

    /// <summary>The origin a host rewrites against: the page's own scheme://authority when the page is this head's
    /// loopback server, else null (a remote site is never handed a relay url).</summary>
    internal static string? LoopbackOrigin(Uri? page)
        => page is { IsAbsoluteUri: true } && page.Host == "127.0.0.1" ? page.GetLeftPart(UriPartial.Authority) : null;

    /// <summary>The Content-Type a relayed answer carries, by extension (WPF ScrolllerWebGate.ContentTypeFor).</summary>
    internal static string ContentTypeFor(string url) => Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant() switch
    {
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".avif" => "image/avif",
        _ => "application/octet-stream",
    };
}
