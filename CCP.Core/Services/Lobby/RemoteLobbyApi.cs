using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Lobby;
/// <summary>WPF AvailableSubjectsService's directory list and claim wires. The session URL is
/// returned only to the caller and is never logged or retained.</summary>
public sealed class RemoteLobbyApi
{
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;
    public RemoteLobbyApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? LobbyWire.DefaultIdentity;
        _baseUrl = baseUrl ?? LobbyWire.DefaultBaseUrl() ?? "";
    }
    // WPF AvailableSubjectsService.cs:120-181,255-275: dual headers, parse entries, keep the last list on failure.
    public async Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)> FetchAsync(CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null || _baseUrl.Length == 0) return (Array.Empty<RemoteSeat>(), false);
        try
        {
            using var req = Request(HttpMethod.Get, "/v2/directory/list", id.Value);
            using var res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return (Array.Empty<RemoteSeat>(), false);
            var body = JObject.Parse(await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var seats = new List<RemoteSeat>();
            foreach (var entry in body["entries"] as JArray ?? new JArray())
            {
                try
                {
                    var tags = (entry["tags"] as JArray)?.Select(t => t.ToString()).Where(s => s.Length > 0).ToList() ?? new List<string>();
                    seats.Add(new RemoteSeat(entry["unified_id"]?.ToString() ?? "", entry["display_name"]?.ToString() ?? "Anonymous",
                        entry["level"]?.Value<int>() ?? 1, entry["tier"]?.ToString() ?? "light", tags, entry["claimed"]?.Value<bool>() ?? false));
                }
                catch { /* WPF skips malformed entries. */ }
            }
            return (seats, true);
        }
        catch { return (Array.Empty<RemoteSeat>(), false); }
    }
    // WPF AvailableSubjectsService.cs:196-251: 409 refreshes unless it is a merged account;
    // success returns the URL and refreshes. Never log the response or URL (it contains a PIN).
    public async Task<(string? Url, bool Refresh)> TryClaimAsync(string subjectId)
    {
        var id = _identity();
        if (id == null || _baseUrl.Length == 0 || string.IsNullOrEmpty(subjectId)) return (null, false);
        try
        {
            using var req = Request(HttpMethod.Post, "/v2/directory/claim", id.Value);
            req.Content = new StringContent(JsonConvert.SerializeObject(new { unified_id = subjectId }), Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            if (res.StatusCode == HttpStatusCode.Conflict)
                return (null, !await MergedAccountRecovery.TryHandleAsync(res).ConfigureAwait(false));
            if (!res.IsSuccessStatusCode) return (null, false);
            var url = JObject.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false))["session_url"]?.ToString();
            return string.IsNullOrEmpty(url) ? (null, false) : (url, true);
        }
        catch { return (null, false); }
    }

    private HttpRequestMessage Request(HttpMethod method, string path, (string UnifiedId, string Token) id)
    {
        var req = new HttpRequestMessage(method, _baseUrl + path);
        req.Headers.Add("X-Auth-Token", id.Token);
        req.Headers.Add("X-Caller-Unified-Id", id.UnifiedId);
        req.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
        req.Headers.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}");
        return req;
    }
}
