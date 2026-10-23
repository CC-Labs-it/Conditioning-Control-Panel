using System;
using System.Net.Http;
using System.Text;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// The request half of the board's <c>pbp:net</c> relay (PieceByPieceHostService.OnNetRequest),
/// split out so the one rule that matters here is tested on the real path: the CCP auth token rides
/// as <c>X-Auth-Token</c> on every forwarded call EXCEPT the public spectate routes
/// (<see cref="PbpWatchRules.IsPublicWatchPath"/>), which are shared CDN entries. The caller has
/// already applied the path whitelist and the GET / POST check.
/// </summary>
internal static class PbpNetRelay
{
    public const string TokenHeader = "X-Auth-Token";
    public const string VersionHeader = "X-Client-Version";

    /// <summary>Build the outgoing request. <paramref name="method"/> is the upper-cased verb.
    /// <paramref name="target"/> is the Uri <c>HostNetPathRule.TryBuild</c> already judged; the
    /// request goes there, never to a string glued from base and path.</summary>
    public static HttpRequestMessage Build(Uri target, string method, string path, string body,
        string? token, string appVersion)
    {
        bool post = method == "POST";
        var request = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get, target);
        if (post) request.Content = new StringContent(body ?? "", Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(token) && !PbpWatchRules.IsPublicWatchPath(method, path))
            request.Headers.Add(TokenHeader, token);
        request.Headers.Add(VersionHeader, appVersion);
        return request;
    }
}
