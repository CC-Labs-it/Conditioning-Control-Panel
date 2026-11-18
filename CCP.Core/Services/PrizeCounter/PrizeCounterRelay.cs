using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

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
    /// <summary>The sheet's Wear for a screen effect look (POST /v2/shop/equip { slot, itemId|null }); answers a state body.</summary>
    public const string OpEquip = "shop-equip";

    /// <summary>Under the page's own 15 s wait (host.js), so the page always hears our answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly Func<bool> _offline;
    private readonly Action<string, bool, int>? _adoptSp;
    private readonly Action<string, long, string[]>? _applyPrizes;
    private readonly string _baseUrl;
    private (string Account, int Sp)? _lastReceipt;

    /// <param name="http">Null = the shared client. Tests pass one over a fake handler.</param>
    /// <param name="identity">The account the counter speaks for, or null (signed out).</param>
    /// <param name="offline">True while the app is in offline mode: every request answers <c>offline</c>.</param>
    /// <param name="adoptSp">(account the request went out under, is it a settlement, balance).</param>
    /// <param name="applyPrizes">(account, revision, grants) from a reply's <c>prizes</c> block.</param>
    /// <param name="baseUrl">Null = <see cref="BackRoomApi.BaseUrl"/>. The Avalonia head passes its sandbox-checked proxy.</param>
    public PrizeCounterRelay(HttpClient? http, Func<(string UnifiedId, string Token)?> identity, Func<bool> offline,
        Action<string, bool, int>? adoptSp, Action<string, long, string[]>? applyPrizes, string? baseUrl = null)
    {
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
        _http = http ?? SharedHttp;
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _offline = offline ?? (() => false);
        _adoptSp = adoptSp;
        _applyPrizes = applyPrizes;
    }

    public static JObject Refusal(string reason) => new() { ["ok"] = false, ["reason"] = reason };

    /// <summary>The sheet's own three (CCP-Server shop-routes.js "7.3 SHEET ADDITIONS"): the weekly gift, a stamp,
    /// the row being saved for. Until 2026-11-17 only the DEBUG preview answered them, so the real server
    /// never heard a press.</summary>
    public const string OpGift = "shop-gift", OpStamp = "shop-stamp", OpGoal = "shop-goal";

    /// <summary>What this app can draw that older ones cannot (server shop.js envForCaps): the 7.3.1 chess rows
    /// (finishes, boards, seated Emi) are listed and sold only to a request that says so.</summary>
    public const string Caps = "chess2";

    /// <summary>The whitelist. Exact, case-sensitive.</summary>
    public static bool TryRoute(string? op, out HttpMethod method, out string path)
    {
        switch (op)
        {
            case OpState: method = HttpMethod.Get; path = "/v2/shop/state"; return true;
            case OpBuy: method = HttpMethod.Post; path = "/v2/shop/buy"; return true;
            case OpEquip: method = HttpMethod.Post; path = "/v2/shop/equip"; return true;
            case OpGift: method = HttpMethod.Post; path = "/v2/shop/gift"; return true;
            case OpStamp: method = HttpMethod.Post; path = "/v2/shop/stamp"; return true;
            case OpGoal: method = HttpMethod.Post; path = "/v2/shop/goal"; return true;
            default: method = HttpMethod.Get; path = string.Empty; return false;
        }
    }

    /// <summary>The request for one op. A buy carries only the fields the route reads; the page cannot
    /// name another account, because <c>unified_id</c> is the host's and stamped last.</summary>
    internal static HttpRequestMessage BuildRequest(string op, (string UnifiedId, string Token) id, string? idem, JObject? body,
        string? baseUrl = null)
    {
        baseUrl ??= BackRoomApi.BaseUrl;
        if (!TryRoute(op, out var method, out var path)) throw new ArgumentException("unknown op", nameof(op));
        HttpRequestMessage req;
        if (method == HttpMethod.Get)
        {
            req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}{path}?unified_id={Uri.EscapeDataString(id.UnifiedId)}&caps={Caps}");
        }
        else if (op == OpEquip)
        {
            var o = new JObject { ["slot"] = body?["slot"]?.DeepClone(), ["itemId"] = body?["itemId"]?.DeepClone() ?? JValue.CreateNull(), ["caps"] = Caps, ["unified_id"] = id.UnifiedId };
            req = new HttpRequestMessage(HttpMethod.Post, baseUrl + path)
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
        }
        else if (op is OpGift or OpStamp or OpGoal)
        {
            // Nothing of the page's but the goal's row: the reply is a whole state, so it carries the caps too.
            var o = new JObject { ["caps"] = Caps };
            if (op == OpGoal) o["itemId"] = body?["itemId"]?.DeepClone() ?? JValue.CreateNull();
            o["unified_id"] = id.UnifiedId;
            req = new HttpRequestMessage(HttpMethod.Post, baseUrl + path)
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
        }
        else
        {
            var o = new JObject
            {
                ["idem"] = idem,
                ["itemId"] = body?["itemId"]?.DeepClone(),
                ["catalogVersion"] = body?["catalogVersion"]?.DeepClone(),
                ["week"] = body?["week"]?.DeepClone(),
                ["caps"] = Caps,
                ["unified_id"] = id.UnifiedId,
            };
            // The stamp card's coupon: only ever the literal true (the server refuses anything else).
            if (body?["coupon"] is JValue { Type: JTokenType.Boolean } c && c.Value<bool>()) o["coupon"] = true;
            req = new HttpRequestMessage(HttpMethod.Post, baseUrl + path)
            {
                Content = new StringContent(o.ToString(Formatting.None), Encoding.UTF8, "application/json"),
            };
        }
        req.Headers.Add("X-Auth-Token", id.Token);
        return req;
    }

#if DEBUG
    /// <summary>The DEBUG shop preview seam (CCP_SHOP_PREVIEW=1). The WPF head seeds its ShopPreviewBackend here
    /// (App, inside #if DEBUG); unseeded = no preview. Core cannot name the head's preview class.</summary>
    public static Func<bool>? PreviewOn { get; set; }
    public static Func<string?, JObject?, string?, JObject>? PreviewHandle { get; set; }

    private static bool PreviewActive()
    {
        try { return PreviewOn?.Invoke() == true; } catch { return false; }
    }
#endif

    /// <summary>One page request to one body. Never throws.</summary>
    public async Task<JObject> RelayAsync(string? op, JObject? body, string? idem, CancellationToken ct = default)
    {
#if DEBUG
        // The DEBUG shop preview (CCP_SHOP_PREVIEW=1) answers locally: no server, no token, no real wallet.
        if (PreviewActive() && PreviewHandle is { } preview) return preview(op, body, idem);
#endif
        if (!TryRoute(op, out _, out _)) return Refusal("bad_input");
        if (op == OpBuy && (string.IsNullOrWhiteSpace(idem) || body?["itemId"] is not JValue { Type: JTokenType.String }))
            return Refusal("bad_input");
        if (op == OpEquip && (body?["slot"] is not JValue { Type: JTokenType.String }
            || body["itemId"] is { Type: not (JTokenType.String or JTokenType.Null) }))
            return Refusal("bad_input");
        if (op == OpGoal && body?["itemId"] is { Type: not (JTokenType.String or JTokenType.Null) })
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
                using var req = BuildRequest(op!, id.Value, idem, body, _baseUrl);
                using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
                var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
                // The account changed under the request: its answer belongs to nobody signed in now.
                if (SafeIdentity()?.UnifiedId != id.Value.UnifiedId) return Refusal("signed_out");
                NoteMerged((int)res.StatusCode, text);
                return Read(op!, id.Value.UnifiedId, res.IsSuccessStatusCode, text, op == OpEquip ? (string?)body?["slot"] : null);
            }
            catch (OperationCanceledException) { return Refusal("offline"); }
            catch (HttpRequestException ex)
            {
                // Never reached the server: once more with the same idem, which the receipt table replays.
                Log.Debug("PrizeCounter relay {Op} network failure (attempt {N}): {E}", op, attempt + 1, ex.Message);
            }
        }
        return Refusal("offline");
    }

    /// <summary>The <c>wallet</c> frame the page sends after a receipt. The relay already adopted that
    /// receipt; the frame is honoured only when it names the same balance, so a page can never move
    /// the wallet to a number the server did not settle.</summary>
    public bool HandleWalletFrame(int sp)
    {
#if DEBUG
        if (PreviewActive()) return false;   // a preview receipt never moves the real wallet
#endif
        if (_lastReceipt is not { } last || last.Sp != sp) return false;
        Adopt(last.Account, true, sp);
        return true;
    }

    private JObject Read(string op, string sentFor, bool success, string text, string? wroteSlot = null)
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
        if (op == OpBuy && ok) NoteBuyLanded();
        NoteLooks(op, sentFor, ok, reason, o, wroteSlot);
        return o;
    }

    /// <summary>The account look store (WPF <c>Prizes.AccountLookStore</c>): null = none (the DEBUG preview, tests,
    /// a head with no looks yet). Unset = <see cref="LooksProvider"/>, the head's own store.</summary>
    internal IShopLookSink? Looks { get => _looksSet ? _looks : SafeLooks(); init { _looks = value; _looksSet = true; } }
    private readonly IShopLookSink? _looks;

    /// <summary>The head's look store, seeded at startup (WPF: <c>AccountLookStore.ForRelay</c>). Unseeded (the
    /// Avalonia head today): no looks, the state body is passed on as it came.</summary>
    public static Func<IShopLookSink?>? LooksProvider { get; set; }

    private static IShopLookSink? SafeLooks()
    {
        try { return LooksProvider?.Invoke(); } catch { return null; }
    }
    private readonly bool _looksSet;

    /// <summary>A state body with looks settles the store (and, with a pick owed here, shows the page that pick);
    /// a refused equip tells the store whether the server sells looks at all.</summary>
    private void NoteLooks(string op, string sentFor, bool ok, string? reason, JObject o, string? wroteSlot)
    {
        try
        {
            var looks = Looks;
            if (looks == null) return;
            if (!ok) { looks.CounterRefused(op, reason); return; }
            if (op is OpState or OpEquip && o["equip"] is JObject)
                looks.FromCounter(sentFor, o, wroteSlot);
        }
        catch (Exception ex) { Log.Debug("PrizeCounter looks note failed: {E}", ex.Message); }
    }

    /// <summary>A buy receipt, or a buy refused for want of Sparkles: the two replies whose balance
    /// is adopted both ways. Everything else may only raise.</summary>
    public static bool IsSettlement(string? op, bool ok, string? reason)
        => op == OpBuy && (ok || reason == "insufficient");

    private void Adopt(string account, bool fromBuy, int sp)
    {
        try { _adoptSp?.Invoke(account, fromBuy, sp); }
        catch (Exception ex) { Log.Debug("PrizeCounter adopt sp failed: {E}", ex.Message); }
    }

    private void ApplyPrizes(string sentFor, JToken? prizes)
    {
        if (_applyPrizes == null || prizes is not JObject p || p["revision"] is not JValue { Type: JTokenType.Integer } rev
            || p["grants"] is not JArray list) return;
        var grants = list.Where(g => g.Type == JTokenType.String).Select(g => (string)g!).ToArray();
        try { _applyPrizes(sentFor, rev.Value<long>(), grants); }
        catch (Exception ex) { Log.Debug("PrizeCounter apply prizes failed: {E}", ex.Message); }
    }

    /// <summary>A buy receipt landed (ok:true). The app sets this to the feature day log's <c>e_shop</c>;
    /// a throwing sink never breaks the reply.</summary>
    public Action? BuyLanded { get; set; }

    private void NoteBuyLanded()
    {
        try { BuyLanded?.Invoke(); }
        catch (Exception ex) { Log.Debug("PrizeCounter buy-landed sink failed: {E}", ex.Message); }
    }

    /// <summary>Contract D: every reply goes to the merged-account recovery, which switches the app to the
    /// canonical id on a <c>409 merged</c>. Tests replace it; a throwing hook never breaks the reply.</summary>
    public Func<int, string, bool>? MergedReply { get; set; } = MergedAccountRecovery.TryHandle;

    private void NoteMerged(int status, string text)
    {
        try { MergedReply?.Invoke(status, text); }
        catch (Exception ex) { Log.Debug("PrizeCounter merged hook failed: {E}", ex.Message); }
    }

    private (string UnifiedId, string Token)? SafeIdentity()
    {
        try { return _identity(); } catch { return null; }
    }
}

/// <summary>What the relay tells the head's look store (WPF <c>Prizes.AccountLookStore</c>): a state or equip body
/// with looks settles it, a refusal says whether the server sells looks at all.</summary>
public interface IShopLookSink
{
    void FromCounter(string account, JObject body, string? wroteSlot);
    void CounterRefused(string op, string? reason);
}
