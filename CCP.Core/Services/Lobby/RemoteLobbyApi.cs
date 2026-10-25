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
}
