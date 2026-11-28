using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>The dares wire as the service sees it. <see cref="DaresApi"/> is the only real one;
/// tests hand in a fake. Nothing here throws: a fault is a null reply.</summary>
public interface IDaresApi
{
    /// <summary>One op. The parsed JSON reply (worded refusals included), or null for a network
    /// fault, a timeout, an HTML page or no account.</summary>
    Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default);
}

/// <summary>
/// THE DARES WIRE (CONTRACT.md beside this file). <c>POST /v2/dares/&lt;op&gt;</c> through the same
/// token door, base url and reply reading as the friends and leash wires. Nothing is retried here.
/// </summary>
public sealed class DaresApi : IDaresApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public DaresApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    public async Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
    {
        (string UnifiedId, string Token)? id;
        try { id = _identity(); } catch { id = null; }
        if (id == null) return null;
        var o = (JObject)body.DeepClone();
        o["unified_id"] = id.Value.UnifiedId;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/dares/{op}")
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            return FriendsApi.Read((int)res.StatusCode, text);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            Serilog.Log.Debug("Dares {Op} failed: {E}", op, ex.Message);
            return null;
        }
    }
}
