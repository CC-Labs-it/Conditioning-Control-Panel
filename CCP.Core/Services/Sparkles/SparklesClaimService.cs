using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Sparkles;

/// <summary>A receipt or refusal. Adopted means the wallet callback was dispatched.</summary>
public sealed record SparklesClaimOutcome(SparklesReceipt? Receipt, string? Reason, bool Adopted = false)
{
    public bool Ok => Receipt != null;
    public static SparklesClaimOutcome Refused(string reason) => new(null, reason);
}

/// <summary>B1 claim wire: bounded same-id retries; 401 means signed out.
/// Paying, non-shadow receipts only raise the wallet; the head rechecks the account.</summary>
public sealed class SparklesClaimService
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>Total sends for one claim (the first plus retries on pending / busy / no answer).</summary>
    public const int MaxAttempts = 4;

    public static readonly TimeSpan MinRetryWait = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultRetryWait = TimeSpan.FromMilliseconds(1500);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;
    private readonly Action<string, int>? _adoptSp;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="adoptSp">(account the claim was sent for, receipt sp): the app's re-checks the
    /// account on the UI thread, writes under <see cref="V2WalletAdoption"/> and publishes the credit.</param>
    public SparklesClaimService(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null,
        string? baseUrl = null, Action<string, int>? adoptSp = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
        _adoptSp = adoptSp;
        _delay = delay ?? ((t, c) => Task.Delay(t, c));
    }

    /// <summary>May this receipt move the wallet at all.</summary>
    public static bool ShouldAdopt(SparklesReceipt? r) =>
        r != null && !r.Shadow && r.Credited > 0 && r.Sp is int sp && sp >= 0;

    /// <summary>Eligible receipts only raise the same account's balance, preserving local credits.</summary>
    public static bool DecideWallet(bool sameAccount, SparklesReceipt? r, int localSp, out int next)
    {
        next = localSp;
        if (!ShouldAdopt(r)) return false;
        return V2WalletAdoption.Decide(sameAccount, fromBuy: false, r!.Sp!.Value, localSp, out next);
    }

    /// <summary>How long to wait before asking again, from a 202's <c>retryAfterMs</c>.</summary>
    public static TimeSpan RetryWait(JObject? reply)
    {
        var ms = reply?["retryAfterMs"] is JValue { Type: JTokenType.Integer or JTokenType.Float } v ? v.Value<double>() : -1;
        var wait = double.IsFinite(ms) && ms > 0 ? TimeSpan.FromMilliseconds(Math.Min(ms, MaxRetryWait.TotalMilliseconds)) : DefaultRetryWait;
        return wait < MinRetryWait ? MinRetryWait : wait > MaxRetryWait ? MaxRetryWait : wait;
    }

    /// <summary>Retry the same claimId on pending, busy or no answer, up to MaxAttempts sends.</summary>
    public async Task<SparklesClaimOutcome> ClaimAsync(SparklesClaim claim, CancellationToken ct = default)
    {
        if (claim == null || !claim.IsWellFormed) return SparklesClaimOutcome.Refused("invalid");
        (string UnifiedId, string Token)? id;
        try { id = _identity(); } catch { id = null; }
        if (id == null) return SparklesClaimOutcome.Refused("signin");
        var sentFor = id.Value.UnifiedId;
        var body = claim.ToJson(sentFor).ToString(Formatting.None);

        var last = "unreachable";
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (ct.IsCancellationRequested) break;
            var (status, reply) = await SendAsync(body, id.Value.Token, ct).ConfigureAwait(false);
            if (ct.IsCancellationRequested) break;
            if (status == 401) return SparklesClaimOutcome.Refused("signin");

            if (reply != null && reply["ok"]?.Type == JTokenType.Boolean && (bool)reply["ok"]! && SparklesReceipt.Parse(reply) is { } receipt)
            {
                var adopted = false;
                if (_adoptSp != null && ShouldAdopt(receipt))
                {
                    try { _adoptSp(sentFor, receipt.Sp!.Value); adopted = true; }
                    catch (Exception ex) { Serilog.Log.Debug("Sparkles: adopt sp failed: {E}", ex.Message); }
                }
                return new SparklesClaimOutcome(receipt, null, adopted);
            }

            TimeSpan wait;
            if (reply != null && reply["pending"]?.Type == JTokenType.Boolean && (bool)reply["pending"]!)
            {
                last = "pending";
                wait = RetryWait(reply);
            }
            else if (reply != null)
            {
                var reason = reply.Value<string?>("reason");
                // A worded refusal will not change its mind by being asked again; 'busy' (a lock
                // was held on the server) will.
                if (reason != "busy") return SparklesClaimOutcome.Refused(string.IsNullOrEmpty(reason) ? "bad" : reason!);
                last = "busy";
                wait = DefaultRetryWait;
            }
            else
            {
                last = "unreachable";
                wait = DefaultRetryWait;
            }

            if (attempt == MaxAttempts) break;
            try { await _delay(wait, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
        return SparklesClaimOutcome.Refused(last);
    }

    private async Task<(int Status, JObject? Reply)> SendAsync(string body, string token, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/v2/sparkles/claim")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            var status = (int)res.StatusCode;
            return (status, FriendsApi.Read(status, text));
        }
        catch (OperationCanceledException) { return (0, null); }
        catch (Exception ex)
        {
            Serilog.Log.Debug("Sparkles claim failed: {E}", ex.Message);
            return (0, null);
        }
    }

}
