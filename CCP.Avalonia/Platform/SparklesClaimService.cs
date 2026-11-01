// WPF Services/Sparkles/SparklesClaimService.cs:94-170: retries keep one claim id.
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Sparkles;

internal sealed record SparklesClaimOutcome(SparklesReceipt? Receipt, string? Reason);

internal static class SparklesClaimService
{
    internal static async Task<SparklesClaimOutcome> ClaimAsync(SparklesClaim claim, (string UnifiedId, string Token)? identity,
        string? baseUrl, HttpClient http)
    {
        if (!claim.IsWellFormed) return new(null, "invalid");
        if (identity == null) return new(null, "signin");
        if (baseUrl == null) return new(null, "unreachable");
        var body = claim.ToJson(identity.Value.UnifiedId).ToString(Formatting.None);
        string last = "unreachable";
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            int status = 0;
            JObject? reply = null;
            try
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/v2/sparkles/claim")
                { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Add("X-Auth-Token", identity.Value.Token);
                using var res = await http.SendAsync(req, budget.Token).ConfigureAwait(false);
                status = (int)res.StatusCode;
                reply = FriendsApi.Read(status, await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false));
            }
            catch (Exception ex) { Log.Debug("Sparkles claim failed: {E}", ex.Message); }
            if (status == 401) return new(null, "signin");
            if (reply?.Value<bool?>("ok") == true && SparklesReceipt.Parse(reply) is { } receipt) return new(receipt, null);
            TimeSpan wait;
            if (reply?.Value<bool?>("pending") == true)
            {
                last = "pending";
                var ms = reply["retryAfterMs"] is JValue { Type: JTokenType.Integer or JTokenType.Float } v ? v.Value<double>() : -1;
                wait = ms > 0 ? TimeSpan.FromMilliseconds(Math.Clamp(ms, 500, 5000)) : TimeSpan.FromMilliseconds(1500);
            }
            else if (reply != null)
            {
                var reason = reply.Value<string?>("reason");
                if (reason != "busy") return new(null, string.IsNullOrEmpty(reason) ? "bad" : reason);
                last = "busy";
                wait = TimeSpan.FromMilliseconds(1500);
            }
            else { last = "unreachable"; wait = TimeSpan.FromMilliseconds(1500); }
            if (attempt < 4) await Task.Delay(wait).ConfigureAwait(false);
        }
        return new(null, last);
    }
}
