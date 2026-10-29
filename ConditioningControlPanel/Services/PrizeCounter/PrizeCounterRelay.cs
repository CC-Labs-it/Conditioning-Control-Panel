using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>
/// THE PRIZE COUNTER RELAY (web tree <c>prizecounter/CONTRACT.md</c> "Host seam", B3 decisions section 5).
/// The page names an op; this class maps it to the one route it may reach, adds the account's token
/// door (<c>X-Auth-Token</c> plus <c>unified_id</c>, the pair the Back Room counter sends) and hands
/// back the server body as it came. The page never sees the token or the base url.
///
/// <para><b>The wallet.</b> Only a debited receipt may lower <c>SkillPoints</c>
/// (<see cref="Prizes.V2WalletAdoption"/>, <c>fromBuy = true</c>). The one exception is an
/// <c>insufficient</c> refusal to a buy: the server just said the balance is lower than we think,
/// and that is adopted as is (owner, 2026-09-24). Every other reply that carries <c>sp</c> (a state
/// read, any other refusal) is a snapshot and may only raise.</para>
///
/// <para>WPF-free so the mapping is tested without a window. Host refusals are only
/// <c>offline</c> and <c>signed_out</c>; an unknown op answers <c>bad_input</c>.</para>
/// </summary>
public sealed class PrizeCounterRelay
{
    public const string OpState = "shop-state";
    public const string OpBuy = "shop-buy";

    /// <summary>Under the page's own 15 s wait (host.js), so the page always hears our answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly Func<bool> _offline;
    private readonly Action<string, bool, int>? _adoptSp;
    private readonly Action<string, long, string[]>? _applyPrizes;
    private (string Account, int Sp)? _lastReceipt;

    /// <param name="http">Null = the shared client. Tests pass one over a fake handler.</param>
    /// <param name="identity">The account the counter speaks for, or null (signed out).</param>
    /// <param name="offline">True while the app is in offline mode: every request answers <c>offline</c>.</param>
    /// <param name="adoptSp">(account the request went out under, is it a settlement, balance).</param>
    /// <param name="applyPrizes">(account, revision, grants) from a reply's <c>prizes</c> block.</param>
    public PrizeCounterRelay(HttpClient? http, Func<(string UnifiedId, string Token)?> identity, Func<bool> offline,
        Action<string, bool, int>? adoptSp, Action<string, long, string[]>? applyPrizes)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _offline = offline ?? (() => false);
        _adoptSp = adoptSp;
        _applyPrizes = applyPrizes;
    }

    public static JObject Refusal(string reason) => new() { ["ok"] = false, ["reason"] = reason };

    /// <summary>The whitelist. Exact, case-sensitive.</summary>
    public static bool TryRoute(string? op, out HttpMethod method, out string path)
    {
        switch (op)
        {
            case OpState: method = HttpMethod.Get; path = "/v2/shop/state"; return true;
            case OpBuy: method = HttpMethod.Post; path = "/v2/shop/buy"; return true;
            default: method = HttpMethod.Get; path = string.Empty; return false;
        }
    }

    /// <summary>The request for one op. A buy carries only the fields the route reads; the page cannot
    /// name another account, because <c>unified_id</c> is the host's and stamped last.</summary>
    internal static HttpRequestMessage BuildRequest(string op, (string UnifiedId, string Token) id, string? idem, JObject? body)
    {
        if (!TryRoute(op, out var method, out var path)) throw new ArgumentException("unknown op", nameof(op));
        HttpRequestMessage req;
        if (method == HttpMethod.Get)
        {
            req = new HttpRequestMessage(HttpMethod.Get, $"{BackRoomApi.BaseUrl}{path}?unified_id={Uri.EscapeDataString(id.UnifiedId)}");
        }
        else
        {
            var o = new JObject
            {
                ["idem"] = idem,
                ["itemId"] = body?["itemId"]?.DeepClone(),
                ["catalogVersion"] = body?["catalogVersion"]?.DeepClone(),
                ["week"] = body?["week"]?.DeepClone(),
                ["unified_id"] = id.UnifiedId,
            };
            req = new HttpRequestMessage(HttpMethod.Post, BackRoomApi.BaseUrl + path)
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
        }
        req.Headers.Add("X-Auth-Token", id.Token);
        return req;
    }

    /// <summary>One page request to one body. Never throws.</summary>
    public async Task<JObject> RelayAsync(string? op, JObject? body, string? idem, CancellationToken ct = default)
    {
        if (!TryRoute(op, out _, out _)) return Refusal("bad_input");
        if (op == OpBuy && (string.IsNullOrWhiteSpace(idem) || body?["itemId"] is not JValue { Type: JTokenType.String }))
            return Refusal("bad_input");
        bool offline;
        try { offline = _offline(); } catch { offline = false; }
        if (offline) return Refusal("offline");
        var id = SafeIdentity();
        if (id == null) return Refusal("signed_out");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var req = BuildRequest(op!, id.Value, idem, body);
                using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
                // The account changed under the request: its answer belongs to nobody signed in now.
                if (SafeIdentity()?.UnifiedId != id.Value.UnifiedId) return Refusal("signed_out");
                MergedAccountRecovery.TryHandle((int)res.StatusCode, text);
                return Read(op!, id.Value.UnifiedId, res.IsSuccessStatusCode, text);
            }
            catch (OperationCanceledException) { return Refusal("offline"); }
            catch (HttpRequestException ex)
            {
                // Never reached the server: once more with the same idem, which the receipt table replays.
                App.Logger?.Debug("PrizeCounter relay {Op} network failure (attempt {N}): {E}", op, attempt + 1, ex.Message);
            }
        }
        return Refusal("offline");
    }

    /// <summary>The <c>wallet</c> frame the page sends after a receipt. The relay already adopted that
    /// receipt; the frame is honoured only when it names the same balance, so a page can never move
    /// the wallet to a number the server did not settle.</summary>
    public bool HandleWalletFrame(int sp)
    {
        if (_lastReceipt is not { } last || last.Sp != sp) return false;
        Adopt(last.Account, true, sp);
        return true;
    }

    private JObject Read(string op, string sentFor, bool success, string text)
    {
        JObject? o = null;
        try { o = JsonConvert.DeserializeObject(text) as JObject; } catch { }
        if (o == null || (!success && o["reason"] == null)) return Refusal("offline");

        bool ok = success && o.Value<bool?>("ok") == true;
        var reason = o["reason"]?.Type == JTokenType.String ? (string?)o["reason"] : null;
        if (o["sp"] is JValue { Type: JTokenType.Integer } sp)
        {
            bool settlement = IsSettlement(op, ok, reason);
            if (op == OpBuy && ok) _lastReceipt = (sentFor, sp.Value<int>());
            Adopt(sentFor, settlement, sp.Value<int>());
        }
        if (ok || reason == "owned") ApplyPrizes(sentFor, o["prizes"]);
        return o;
    }

    /// <summary>A buy receipt, or a buy refused for want of Sparkles: the two replies whose balance
    /// is adopted both ways. Everything else may only raise.</summary>
    public static bool IsSettlement(string? op, bool ok, string? reason)
        => op == OpBuy && (ok || reason == "insufficient");

    private void Adopt(string account, bool fromBuy, int sp)
    {
        try { _adoptSp?.Invoke(account, fromBuy, sp); }
        catch (Exception ex) { App.Logger?.Debug("PrizeCounter adopt sp failed: {E}", ex.Message); }
    }

    private void ApplyPrizes(string sentFor, JToken? prizes)
    {
        if (_applyPrizes == null || prizes is not JObject p || p["revision"] is not JValue { Type: JTokenType.Integer } rev
            || p["grants"] is not JArray list) return;
        var grants = list.Where(g => g.Type == JTokenType.String).Select(g => (string)g!).ToArray();
        try { _applyPrizes(sentFor, rev.Value<long>(), grants); }
        catch (Exception ex) { App.Logger?.Debug("PrizeCounter apply prizes failed: {E}", ex.Message); }
    }

    private (string UnifiedId, string Token)? SafeIdentity()
    {
        try { return _identity(); } catch { return null; }
    }
}
