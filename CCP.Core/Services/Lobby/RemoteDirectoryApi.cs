using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>
/// The Remote directory wire of WPF <c>AvailableSubjectsService</c> (list :120, claim :196) for
/// heads that read it through <see cref="LobbyPoller"/>: <c>GET /v2/directory/list</c> and
/// <c>POST /v2/directory/claim</c> with the account's <c>X-Auth-Token</c> + <c>X-Caller-Unified-Id</c>.
/// The claim's session url carries the PIN in its fragment: it is returned, never logged.
/// </summary>
public sealed class RemoteDirectoryApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public RemoteDirectoryApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? LobbyWire.DefaultIdentity;
        _baseUrl = baseUrl ?? LobbyWire.DefaultBaseUrl() ?? "";
    }

    /// <summary>The directory, or (empty, false) without an identity, on a non-2xx or on any failure.</summary>
    public async Task<(IReadOnlyList<RemoteSeat> Seats, bool Ok)> ListAsync(CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null || _baseUrl.Length == 0) return (Array.Empty<RemoteSeat>(), false);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = Request(HttpMethod.Get, "/v2/directory/list", id.Value);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Log.Warning("[AvailableSubjects] list failed: {Status}", res.StatusCode);
                return (Array.Empty<RemoteSeat>(), false);
            }
            return (Parse(await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false)), true);
        }
        catch (Exception ex)
        {
            Log.Debug("[AvailableSubjects] refresh error: {E}", ex.Message);
            return (Array.Empty<RemoteSeat>(), false);
        }
    }

    /// <summary>WPF TryClaimAsync: the session url on 200; null on 409 (lost the race, or a merged
    /// account handled by <see cref="MergedAccountRecovery"/>), any other status, or a failure.</summary>
    public async Task<string?> ClaimAsync(string subjectUnifiedId, CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null || _baseUrl.Length == 0 || string.IsNullOrEmpty(subjectUnifiedId)) return null;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = Request(HttpMethod.Post, "/v2/directory/claim", id.Value);
            req.Content = new StringContent(JsonConvert.SerializeObject(new { unified_id = subjectUnifiedId }), Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            if ((int)res.StatusCode == 409)
            {
                await MergedAccountRecovery.TryHandleAsync(res).ConfigureAwait(false);
                return null;
            }
            if (!res.IsSuccessStatusCode)
            {
                Log.Warning("[AvailableSubjects] claim failed: {Status}", res.StatusCode);
                return null;
            }
            var url = (JsonConvert.DeserializeObject<JToken>(await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false)) as JObject)?["session_url"]?.ToString();
            if (string.IsNullOrEmpty(url)) Log.Warning("[AvailableSubjects] claim 200 without session_url");
            return string.IsNullOrEmpty(url) ? null : url;
        }
        catch (Exception ex)
        {
            Log.Warning("[AvailableSubjects] claim error: {E}", ex.GetType().Name);
            return null;
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string path, (string UnifiedId, string Token) id)
    {
        var req = new HttpRequestMessage(method, _baseUrl + path);
        req.Headers.Add("X-Auth-Token", id.Token);
        req.Headers.Add("X-Caller-Unified-Id", id.UnifiedId);
        return req;
    }

    /// <summary>WPF ParseEntry: unified_id, display_name ("Anonymous"), level (1), tags, tier ("light"), claimed.</summary>
    internal static IReadOnlyList<RemoteSeat> Parse(string? text)
    {
        JObject? o = null;
        try { o = JsonConvert.DeserializeObject<JToken>(text ?? "") as JObject; } catch { }
        if (o?["entries"] is not JArray arr) return Array.Empty<RemoteSeat>();
        var seats = new List<RemoteSeat>();
        foreach (var t in arr)
        {
            try
            {
                var tags = (t["tags"] as JArray)?.Select(x => x.ToString()).Where(s => s.Length > 0).ToList() ?? new List<string>();
                seats.Add(new RemoteSeat(t["unified_id"]?.ToString() ?? "", t["display_name"]?.ToString() ?? "Anonymous",
                    t["level"]?.Value<int>() ?? 1, t["tier"]?.ToString() ?? "light", tags, t["claimed"]?.Value<bool>() ?? false));
            }
            catch { }
        }
        return seats;
    }
}
