using System;

namespace ConditioningControlPanel.Services.Safety;

/// <summary>
/// The one gate for a game page's "send this HTTP call for me" relay (chess <c>pbp:net</c>, Goon
/// <c>net-post</c>): the host attaches the player's X-Auth-Token, so whatever path this lets
/// through is a call made AS the player.
///
/// <para><b>Why a prefix check on the raw string was not enough (2026-10-14).</b> The relays
/// checked <c>path.StartsWith("/v2/pbp/")</c> and then built <c>ProxyBaseUrl + path</c>.
/// System.Uri collapses dot segments and reads <c>\</c> as <c>/</c>, so
/// <c>/v2/pbp/../user/me</c> passed the check and reached <c>/v2/user/me</c> with the token:
/// an authenticated open proxy for any script that got into the page. This rule builds the Uri
/// FIRST and judges what will actually be sent: same scheme, host and port as the proxy base,
/// and the parsed <c>AbsolutePath</c> under the allowed prefix. On top of that it refuses any raw
/// path with a shape no real route has (dot segments, backslash, encoded dot / slash /
/// backslash, a scheme, <c>//</c>, anything outside printable ASCII), so a parser difference
/// between .NET and the server cannot reopen it.</para>
///
/// <para>Pure: no I/O, no App statics.</para>
/// </summary>
internal static class HostNetPathRule
{
    /// <summary>Longer than any real route plus its query string; a cap so a page cannot hand the
    /// parser something huge.</summary>
    public const int MaxPathLength = 2048;

    /// <summary>
    /// True when <paramref name="path"/> may be relayed; <paramref name="uri"/> is then the exact
    /// address to send to (pass THIS to HttpRequestMessage, never re-concatenate the string).
    /// </summary>
    public static bool TryBuild(string proxyBaseUrl, string? path, string allowedPrefix, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength) return false;
        if (string.IsNullOrEmpty(allowedPrefix) || allowedPrefix[0] != '/') return false;
        if (!path.StartsWith(allowedPrefix, StringComparison.Ordinal)) return false;
        if (HasForbiddenShape(path)) return false;

        if (!Uri.TryCreate(proxyBaseUrl, UriKind.Absolute, out var baseUri)) return false;
        if (!Uri.TryCreate(proxyBaseUrl.TrimEnd('/') + path, UriKind.Absolute, out var built)) return false;

        if (!string.Equals(built.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(built.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)) return false;
        if (built.Port != baseUri.Port) return false;
        if (built.UserInfo.Length != 0) return false;
        if (!built.AbsolutePath.StartsWith(allowedPrefix, StringComparison.Ordinal)) return false;
        if (built.AbsolutePath.Contains("..", StringComparison.Ordinal)) return false;

        uri = built;
        return true;
    }

    /// <summary>A raw path shape that no real route has. Checked over the WHOLE string, query
    /// included: real routes carry only ids and <c>encodeURIComponent</c>'d values.</summary>
    public static bool HasForbiddenShape(string path)
    {
        foreach (var c in path)
        {
            // Printable ASCII only: no whitespace, no control characters, no Unicode look-alikes
            // (fullwidth dots, etc.). encodeURIComponent never emits anything else.
            if (c < 0x21 || c > 0x7E) return true;
            if (c == '\\') return true;
        }
        if (path.Contains("..", StringComparison.Ordinal)) return true;
        if (path.Contains("//", StringComparison.Ordinal)) return true;
        if (path.Contains(":/", StringComparison.Ordinal)) return true;
        if (path.Contains("%2e", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Contains("%2f", StringComparison.OrdinalIgnoreCase)) return true;
        if (path.Contains("%5c", StringComparison.OrdinalIgnoreCase)) return true;
        // A double-encoded dot or slash (%252e) and an encoded NUL: a second decode on the far side
        // would turn them back into the shapes refused above.
        if (path.Contains("%25", StringComparison.Ordinal)) return true;
        if (path.Contains("%00", StringComparison.Ordinal)) return true;
        return false;
    }
}
