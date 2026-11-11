using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Lobby;
/// <summary>WPF AvailableSubjectsService list wire, without its dispatcher-owned collection.
/// Keeps the last successful directory on a fault, as the WPF collection does.</summary>
public sealed class RemoteLobbyApi
{
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly Func<string?> _baseUrl;
    private IReadOnlyList<RemoteSeat> _seats = Array.Empty<RemoteSeat>();
    private string? _caller;
    public RemoteLobbyApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null,
        Func<string?>? baseUrl = null)
    {
        _http = http ?? Shared;
        _identity = identity ?? LobbyWire.DefaultIdentity;
        _baseUrl = baseUrl ?? LobbyWire.DefaultBaseUrl;
    }
    public async Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)> FetchAsync(CancellationToken ct = default)
    {
        var id = _identity();
        var url = _baseUrl();
        if (_caller != id?.UnifiedId) { _caller = id?.UnifiedId; _seats = Array.Empty<RemoteSeat>(); }
        if (id == null || string.IsNullOrEmpty(url)) return (_seats, false);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url + "/v2/directory/list");
            request.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
            request.Headers.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}");
            request.Headers.Add("X-Auth-Token", id.Value.Token);
            request.Headers.Add("X-Caller-Unified-Id", id.Value.UnifiedId);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return (_seats, false);
            var body = JObject.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var seats = new List<RemoteSeat>();
            foreach (var entry in body["entries"] as JArray ?? new JArray())
            {
                try
                {
                    seats.Add(new RemoteSeat(entry["unified_id"]?.ToString() ?? "",
                        entry["display_name"]?.ToString() ?? "Anonymous", entry["level"]?.Value<int>() ?? 1,
                        entry["tier"]?.ToString() ?? "light",
                        (entry["tags"] as JArray ?? new JArray()).Select(t => t.ToString()).Where(t => t.Length > 0).ToArray(),
                        entry["claimed"]?.Value<bool>() ?? false));
                }
                catch { /* WPF skips malformed entries, keeping the rest of the directory. */ }
            }
            _seats = seats;
            return (_seats, true);
        }
        catch { return (_seats, false); }
    }
    /// <summary>WPF AvailableSubjectsService.TryClaimAsync: the session url on 200, else null. The url
    /// carries the PIN in its fragment, so it is never logged or kept.</summary>
    public async Task<string?> ClaimAsync(string subjectUnifiedId, CancellationToken ct = default)
    {
        var id = _identity();
        var url = _baseUrl();
        if (id == null || string.IsNullOrEmpty(url) || string.IsNullOrEmpty(subjectUnifiedId)) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url + "/v2/directory/claim") { Content = new StringContent(
                new JObject { ["unified_id"] = subjectUnifiedId }.ToString(Newtonsoft.Json.Formatting.None), System.Text.Encoding.UTF8, "application/json") };
            request.Headers.Add("X-Auth-Token", id.Value.Token);
            request.Headers.Add("X-Caller-Unified-Id", id.Value.UnifiedId);
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            // A 409 is either a merged account (recovery takes it) or a lost race (the next round shows it taken).
            if ((int)response.StatusCode == 409) { await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false); return null; }
            if (!response.IsSuccessStatusCode) return null;
            var body = JObject.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return body["session_url"]?.ToString() is { Length: > 0 } session ? session : null;
        }
        catch { return null; }
    }
}
