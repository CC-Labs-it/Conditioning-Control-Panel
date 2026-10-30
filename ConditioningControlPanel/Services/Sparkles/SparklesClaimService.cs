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

/// <summary>What one claim came to: a receipt (paid, zero-pay or shadow) or a reason. Reasons are the
/// server's refusals plus ours: <c>unreachable</c> (no answer, a 5xx or a 404: the server is down, the
/// slip says the bank is closed), <c>pending</c> (an online result never came in), <c>invalid</c> (not
/// sent). <c>Adopted</c>: the receipt went to the wallet.</summary>
public sealed record SparklesClaimOutcome(SparklesReceipt? Receipt, string? Reason, bool Adopted = false)
{
    public bool Ok => Receipt != null;
    public static SparklesClaimOutcome Refused(string reason) => new(null, reason);
}

/// <summary>
/// SPARKLES BY PLACE (B1), client half: <c>POST /v2/sparkles/claim</c> through the stakes door
/// (identity, base url, reply reading). A 202 pending is asked again with the SAME claimId (the
/// server replays the same receipt), at most <see cref="MaxAttempts"/> sends; 401 = signed out.
/// WALLET RULE: only <c>credited &gt; 0</c> and not <c>shadow</c> is adopted, and only ever RAISES
/// the wallet (<see cref="V2WalletAdoption.Decide"/> <c>fromBuy: false</c>: a credit is not a debited
/// receipt), account re-checked in the write.
/// </summary>
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

    // ------------------------------------------------------------------ pure rules

    /// <summary>May this receipt move the wallet at all.</summary>
    public static bool ShouldAdopt(SparklesReceipt? r) =>
        r != null && !r.Shadow && r.Credited > 0 && r.Sp is int sp && sp >= 0;

    /// <summary>The whole wallet decision for one receipt: <see cref="ShouldAdopt"/>, then RAISE
    /// ONLY. A place receipt is a credit, never a debit, and only a debited receipt may lower the
    /// wallet (AGENTS.md wallet rule), so unsynced local level-up points are never wiped.
    /// <paramref name="next"/> is only meaningful when this answers true.</summary>
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
        var wait = ms > 0 ? TimeSpan.FromMilliseconds(ms) : DefaultRetryWait;
        return wait < MinRetryWait ? MinRetryWait : wait > MaxRetryWait ? MaxRetryWait : wait;
    }

    // ------------------------------------------------------------------ the claim

    /// <summary>
    /// Post one claim and settle what came back. Never throws. Retries the SAME claimId on a
    /// pending answer, a <c>busy</c> refusal or no answer, up to <see cref="MaxAttempts"/> sends.
    /// </summary>
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
            if (status == 401) return SparklesClaimOutcome.Refused("signin");

            if (reply != null && reply.Value<bool?>("ok") == true && SparklesReceipt.Parse(reply) is { } receipt)
            {
                var adopted = false;
                if (_adoptSp != null && ShouldAdopt(receipt))
                {
                    try { _adoptSp(sentFor, receipt.Sp!.Value); adopted = true; }
                    catch (Exception ex) { App.Logger?.Debug("Sparkles: adopt sp failed: {E}", ex.Message); }
                }
                return new SparklesClaimOutcome(receipt, null, adopted);
            }

            TimeSpan wait;
            if (reply != null && reply.Value<bool?>("pending") == true)
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
            App.Logger?.Debug("Sparkles claim failed: {E}", ex.Message);
            return (0, null);
        }
    }

    // ------------------------------------------------------------------ the app's one

    /// <summary>The app's service: adopts on the UI thread and fires the wallet juice.</summary>
    public static SparklesClaimService CreateForApp() => new(adoptSp: (account, sp) =>
        App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            if (App.Settings?.Current is not { } s) return;
            // The account is checked AGAIN inside the marshalled write: the player may be
            // somebody else by the time the reply lands.
            var same = string.Equals(BackRoomApi.AppIdentity()?.UnifiedId, account, StringComparison.Ordinal);
            var before = s.SkillPoints;
            if (!V2WalletAdoption.Decide(same, fromBuy: false, sp, before, out var next)) return;
            s.SkillPoints = next;
            App.Settings.Save();
            SparklePointRewards.PublishCredit(before, next, SparklePointSource.Place);
        })));
}
