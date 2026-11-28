using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.SuperCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Prizes;

/*
 * CONTRACT (CCP-Server proxy/equip.js "LOOKS" + shop-routes.js, draft stack #565-#569, #574 and the fog2 follow-up;
 * read from the server code, nothing of it is live yet):
 *   GET  /v2/equip?unified_id=      -> { ok, equip: { ..., looks?: { bubble, card, behind, word, mix, fog2 }, updatedAt } }
 *                                      `looks` is ABSENT while env SHOP_LOOKS is off (the feature-off answer).
 *   PUT  /v2/equip { unified_id, family: 'looks', value: { bubble, card, behind, word, mix, fog2 } }
 *                                   -> the same body. PARTIAL: a missing slot keeps the server's; a slot the account
 *                                      may not wear is IGNORED and the answer shows the old value (ok: true), so a
 *                                      push settles on the ANSWER's looks, never on what was sent. Off: { ok:false,
 *                                      reason:'unavailable' }. mix is applied before fog2.
 *   POST /v2/shop/equip { unified_id, slot, itemId|null } (the sheet's Wear) -> a whole shop state body with `equip`
 *                                      (the looks, same shape), or { ok:false, reason: not_owned | not_worn |
 *                                      unavailable | bad_input | busy }.
 *   GET  /v2/shop/state             -> carries `equip` (the looks) while SHOP_LOOKS is on.
 *   Owned = the account's grants `cos.<look id>` (prizes block, App.Ownership); the free row of a slot is owned by all.
 *   The fog wears no colour (the user's own filter colour); Two-tone is the `mix` switch; `fog2` is a FogLooks.Palette
 *   value, only while Two-tone is owned AND on, null while the mix is off.
 * Unsure readings are listed in the lane report: fog2 = the full `fx.fog.<name>` id; the state's `equip` carries no
 * updatedAt, so a sheet answer is adopted as the truth for the slot it wrote.
 */

/// <summary>What one account wears: four slots, Two-tone's switch and its second colour. Immutable.</summary>
public sealed record LookSet(string Bubble, string Card, string Behind, string Word, bool Mix, string? Fog2)
{
    public static readonly LookSet Default = new(
        WornLooks.DefaultOf(WornLooks.Bubble)!, WornLooks.DefaultOf(WornLooks.Card)!,
        WornLooks.DefaultOf(WornLooks.Behind)!, WornLooks.DefaultOf(WornLooks.Word)!, false, null);

    /// <summary>The row worn in a slot (null = the free default; mix: the Two-tone id while on; fog2 only while on).</summary>
    public string? Get(string slot) => slot switch
    {
        WornLooks.Bubble => Bubble,
        WornLooks.Card => Card,
        WornLooks.Behind => Behind,
        WornLooks.Word => Word,
        WornLooks.Mix => Mix ? FogLooks.Twotone : null,
        WornLooks.Fog2 => Mix ? Fog2 ?? FogLooks.DefaultSecond : null,
        _ => null,
    };

    /// <summary>The slots whose answer differs between two sets.</summary>
    public static IEnumerable<string> Diff(LookSet a, LookSet b)
    {
        foreach (var s in new[] { WornLooks.Bubble, WornLooks.Card, WornLooks.Behind, WornLooks.Word, WornLooks.Mix, WornLooks.Fog2 })
            if (a.Get(s) != b.Get(s)) yield return s;
    }
}

/// <summary>
/// The server's look rules, mirrored so a wear lands on screen before the answer does (CCP-Server equip.js
/// <c>clampLooks</c> / <c>wearLook</c>). Pure.
/// </summary>
public static class LookEquipRules
{
    public static readonly IReadOnlyList<string> WearSlots = new[] { WornLooks.Bubble, WornLooks.Card, WornLooks.Behind, WornLooks.Word };

    private static PrizeCounter.PreviewEffect? Row(string? id) => PrizeCounter.ShopPreviewRules.Effect(id);

    /// <summary>A looks block off the wire (<c>equip.looks</c> or a state's <c>equip</c>), clamped; null when it is none.</summary>
    public static LookSet? Read(JToken? t)
    {
        if (t is not JObject o) return null;
        string Slot(string slot)
        {
            var id = o[slot] is JValue { Type: JTokenType.String } v ? (string?)v : null;
            return Row(id) is { Todo: false, Special: false } r && r.Slot == slot ? r.Id : WornLooks.DefaultOf(slot)!;
        }
        bool mix = o["mix"] is JValue { Type: JTokenType.Boolean } m && (bool)m;
        var f2 = o["fog2"] is JValue { Type: JTokenType.String } f ? (string?)f : null;
        return new LookSet(Slot(WornLooks.Bubble), Slot(WornLooks.Card), Slot(WornLooks.Behind), Slot(WornLooks.Word),
            mix, mix ? (FogLooks.IsPalette(f2) ? f2 : FogLooks.DefaultSecond) : null);
    }

    /// <summary>The wire shape (PUT value, and a state body's <c>equip</c>).</summary>
    public static JObject ToJson(LookSet s) => new()
    {
        [WornLooks.Bubble] = s.Bubble, [WornLooks.Card] = s.Card, [WornLooks.Behind] = s.Behind, [WornLooks.Word] = s.Word,
        [WornLooks.Mix] = s.Mix, [WornLooks.Fog2] = s.Mix ? new JValue(s.Fog2 ?? FogLooks.DefaultSecond) : JValue.CreateNull(),
    };

    /// <summary>A set with everything the account no longer owns put back to its default (the server's clamp).</summary>
    public static LookSet Clamp(LookSet s, Func<string, bool> owns)
    {
        string Keep(string slot, string id) => id == WornLooks.DefaultOf(slot) || owns(id) ? id : WornLooks.DefaultOf(slot)!;
        bool mix = s.Mix && owns(FogLooks.Twotone);
        return new LookSet(Keep(WornLooks.Bubble, s.Bubble), Keep(WornLooks.Card, s.Card), Keep(WornLooks.Behind, s.Behind),
            Keep(WornLooks.Word, s.Word), mix, mix ? (FogLooks.IsPalette(s.Fog2) ? s.Fog2 : FogLooks.DefaultSecond) : null);
    }

    /// <summary>Wear or take off one slot. Answers the next set, or a refusal reason (the server's words).</summary>
    public static (LookSet? Next, string? Refusal) Apply(LookSet cur, string? slot, string? itemId, Func<string, bool> owns)
    {
        var s = Clamp(cur, owns);
        if (slot == WornLooks.Fog2)
        {
            if (itemId != null && !FogLooks.IsPalette(itemId)) return (null, "bad_input");
            if (!owns(FogLooks.Twotone)) return (null, "not_owned");
            if (!s.Mix) return (null, "not_worn");
            return (s with { Fog2 = itemId ?? FogLooks.DefaultSecond }, null);
        }
        var r = Row(itemId);
        if (itemId != null && r == null) return (null, "bad_input");
        if (r is { Todo: true }) return (null, "unavailable");
        if (slot is WornLooks.Mix or WornLooks.Fog)
        {
            if (r == null) return (s with { Mix = false, Fog2 = null }, null);
            if (r.Id != FogLooks.Twotone) return (null, "bad_input");
            if (!owns(r.Id)) return (null, "not_owned");
            return (s with { Mix = true, Fog2 = s.Fog2 ?? FogLooks.DefaultSecond }, null);
        }
        if (slot == null || !WearSlots.Contains(slot)) return (null, "bad_input");
        var id = r?.Id ?? WornLooks.DefaultOf(slot)!;
        if (r != null && r.Slot != slot) return (null, "bad_input");
        if (r != null && id != WornLooks.DefaultOf(slot) && !owns(id)) return (null, "not_owned");
        return (slot switch
        {
            WornLooks.Bubble => s with { Bubble = id },
            WornLooks.Card => s with { Card = id },
            WornLooks.Behind => s with { Behind = id },
            _ => s with { Word = id },
        }, null);
    }
}

/// <summary>The machine-local cache: who it belongs to, what is worn, whether a push is owed, the looks last seen owned.
/// Its own file beside the settings, never in AppSettings, so presets, the session journal and cloud backup never see it.</summary>
internal sealed class LookCacheFile
{
    [JsonProperty("account")] public string? Account { get; set; }
    [JsonProperty("looks")] public JObject? Looks { get; set; }
    [JsonProperty("owed")] public bool PushOwed { get; set; }
    [JsonProperty("owned")] public List<string> Owned { get; set; } = new();
}

/// <summary>
/// THE REAL ACCOUNT EQUIP behind <see cref="LookWardrobe"/> (signed in, outside the DEBUG preview). Owned = the
/// account's grants as the app already learns them (<see cref="PrizeGrants"/>, fed by the profile sync and every
/// counter reply's prizes block), with the looks last seen owned kept on this PC for a launch before the grants land.
/// Worn = <c>/v2/equip</c> family <c>looks</c>, cached on this PC so looks apply at launch and offline.
/// <para>The <see cref="ProfileEquipSync"/> pattern: a wear lands locally at once and is owed until a push lands; an
/// owed push beats adoption (once per account per launch, and on every counter state); a push settles on the
/// ANSWER's looks. Pushes run one at a time. A server without the routes (flags off: no <c>looks</c>, a 404, or
/// <c>unavailable</c>) leaves everything as it is, logs one line and stays quiet for <see cref="OffBackoff"/>.</para>
/// <para>Signed out = free only: nothing is read, worn or sent.</para>
/// </summary>
public sealed class AccountLookStore : ILookStore, PrizeCounter.IShopLookSink
{
    public const string FileName = "looks-equip.json";
    public const string EquipPath = "/v2/equip";
    public static readonly TimeSpan OffBackoff = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerSettings NoDates = new() { DateParseHandling = DateParseHandling.None };

    private readonly HttpClient _http;
    private readonly Func<string?> _account;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly Func<string, bool?> _grant;
    private readonly string? _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly string _baseUrl;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _pushing = new(1, 1);

    private LookCacheFile? _file;
    private LookSet _worn = LookSet.Default;
    private long _seq;
    private string? _pulledFor;
    private DateTimeOffset _offUntil = DateTimeOffset.MinValue;

    /// <summary>Raised with each slot whose answer changed (any thread). The app wires it to <see cref="WornLooks.Raise"/>.</summary>
    internal Action<string> Raise { get; set; } = WornLooks.Raise;

    /// <param name="account">The signed-in account (a token exists), offline or not; null = signed out.</param>
    /// <param name="identity">The token door for a request; null = no request now (signed out, offline mode).</param>
    /// <param name="grant">"cos.&lt;id&gt;" -> owned; null while no grants snapshot is held yet.</param>
    /// <param name="path">The cache file; null = memory only (tests).</param>
    internal AccountLookStore(HttpClient http, Func<string?> account, Func<(string UnifiedId, string Token)?> identity,
        Func<string, bool?> grant, string? path, Func<DateTimeOffset>? now = null, string? baseUrl = null)
    {
        _http = http;
        _account = account;
        _identity = identity;
        _grant = grant;
        _path = path;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    private static AccountLookStore? _shared;

    /// <summary>The app's one store (lazily, on first ask).</summary>
    public static AccountLookStore Shared => _shared ??= new AccountLookStore(
        new HttpClient { Timeout = TimeSpan.FromSeconds(10) },
        () =>
        {
            var s = App.Settings?.Current;
            return s == null || string.IsNullOrWhiteSpace(s.UnifiedId) || string.IsNullOrWhiteSpace(s.AuthToken) ? null : s.UnifiedId;
        },
        BackRoomApi.AppIdentity,
        LiveGrant,
        Path.Combine(App.UserDataPath, FileName));

    /// <summary>The app's grants: null until a snapshot for the signed-in account is held.</summary>
    private static bool? LiveGrant(string grant)
    {
        var o = App.Ownership;
        if (o == null) return null;
        if (o.IsGranted(grant)) return true;
        return o.Revision > 0 || o.Grants.Count > 0 ? false : null;
    }

    /// <summary>The store the counter relay talks to, or null while the DEBUG preview answers (it never reaches the server).</summary>
    internal static AccountLookStore? ForRelay()
    {
#if DEBUG
        if (PrizeCounter.ShopPreviewBackend.Enabled) return null;
#endif
        return LookWardrobe.Store as AccountLookStore;
    }

    /// <summary>Startup (App, after <see cref="WornLooks.Wire"/>): outside the preview the effects read this store and
    /// a change of grants refreshes the Studio rows and the effects.</summary>
    public static void Wire()
    {
#if DEBUG
        if (PrizeCounter.ShopPreviewBackend.Enabled) return;
#endif
        WornLooks.Source = ReadWorn;
        PrizeGrants.GrantsChanged += () => { try { (LookWardrobe.Store as AccountLookStore)?.OnGrantsChanged(); } catch { } };
    }

    /// <summary>The effects' question through the wardrobe (owned checked there).</summary>
    internal static string? ReadWorn(string slot) => slot switch
    {
        WornLooks.Mix => LookWardrobe.TwoToneOn ? FogLooks.Twotone : null,
        WornLooks.Fog2 => LookWardrobe.TwoToneOn ? LookWardrobe.SecondFogColour : null,
        WornLooks.Fog => null,
        _ => LookWardrobe.Worn(slot),
    };

    // ---------------------------------------------------------------- ILookStore

    public bool Owns(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        var account = SafeAccount();
        if (account == null) return false;
        if (SafeGrant(itemId) is bool b) return b;
        lock (_gate)
        {
            var f = Load();
            return f.Account == account && f.Owned.Contains(itemId);
        }
    }

    public string? Worn(string slot)
    {
        var account = SafeAccount();
        if (account == null) return null;
        lock (_gate)
        {
            var f = Load();
            return f.Account == account ? _worn.Get(slot) : null;
        }
    }

    /// <summary>A Studio wear: lands here at once, then goes up. Signed out = only the free rows, nothing kept.</summary>
    public bool Equip(string slot, string? itemId)
    {
        var account = SafeAccount();
        if (account == null) return itemId == null || itemId == WornLooks.DefaultOf(slot);
        LookSet before, next;
        lock (_gate)
        {
            var f = Load();
            if (f.Account != account) { StartFor(f, account); RefreshOwned(f); }
            before = _worn;
            var (n, why) = LookEquipRules.Apply(_worn, slot, itemId, Owns);
            if (n == null) { App.Logger?.Debug("Looks: wear {Slot} refused here: {Why}", slot, why); return false; }
            next = n;
            if (next == before) return true;
            SetWorn(f, next, owed: true);
        }
        RaiseDiff(before, next);
        _ = PushAsync();
        return true;
    }

    // ---------------------------------------------------------------- the server

    /// <summary>Once per account per launch (after the profile landed): an owed push goes up, else the server's looks
    /// are adopted. A failure leaves the next profile load free to try.</summary>
    public async Task PullOnceAsync()
    {
        var id = SafeIdentity();
        if (id == null || _pulledFor == id.Value.UnifiedId || IsOff()) return;
        var account = id.Value.UnifiedId;
        _pulledFor = account;
        try
        {
            bool owed;
            lock (_gate) { var f = Load(); owed = f.Account == account && f.PushOwed; }
            if (owed) { await PushAsync().ConfigureAwait(false); return; }
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{EquipPath}?unified_id={Uri.EscapeDataString(account)}");
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req).ConfigureAwait(false);
            var o = res.IsSuccessStatusCode ? Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false)) : null;
            if ((int)res.StatusCode == 404) { NoteOff("GET answered 404"); return; }
            if (o?.Value<bool?>("ok") != true || o["equip"] is not JObject eq) { _pulledFor = null; return; }
            if (LookEquipRules.Read(eq["looks"]) is not { } server) { NoteOff("the server has no looks yet"); return; }
            Adopt(account, server, wrote: null);
        }
        catch (Exception ex)
        {
            if (_pulledFor == account) _pulledFor = null;
            App.Logger?.Debug("Looks: GET failed soft: {E}", ex.Message);
        }
    }

    /// <summary>Push the owed looks, one push at a time; a pick made while one was out goes up right after.</summary>
    internal async Task PushAsync()
    {
        if (IsOff()) return;
        await _pushing.WaitAsync().ConfigureAwait(false);
        try
        {
            for (int round = 0; round < 3; round++)
            {
                var id = SafeIdentity();
                if (id == null) return;
                var account = id.Value.UnifiedId;
                LookSet sent; long seq;
                lock (_gate)
                {
                    var f = Load();
                    if (f.Account != account || !f.PushOwed) return;
                    sent = _worn; seq = _seq;
                }
                var body = new JObject { ["unified_id"] = account, ["family"] = "looks", ["value"] = LookEquipRules.ToJson(sent) };
                using var req = new HttpRequestMessage(HttpMethod.Put, _baseUrl + EquipPath)
                {
                    Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Auth-Token", id.Value.Token);
                using var res = await _http.SendAsync(req).ConfigureAwait(false);
                if ((int)res.StatusCode == 404) { NoteOff("PUT answered 404"); return; }
                var o = Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
                if (!res.IsSuccessStatusCode || o == null) { App.Logger?.Debug("Looks: PUT answered {S}; the push stays owed", (int)res.StatusCode); return; }
                if (o.Value<bool?>("ok") != true)
                {
                    var why = o["reason"]?.ToString();
                    if (why == "unavailable") NoteOff("PUT answered unavailable");
                    else App.Logger?.Debug("Looks: PUT refused {Why}; the push stays owed", why);
                    return;
                }
                if (LookEquipRules.Read(o["equip"]?["looks"]) is not { } stored) { NoteOff("the PUT answer has no looks"); return; }
                LookSet before, after;
                lock (_gate)
                {
                    var f = Load();
                    if (f.Account != account) return;
                    if (_seq != seq) continue;           // picked again while this one was out: push that one
                    before = _worn; after = stored;       // the ANSWER's looks: an unowned id came back as the old value
                    SetWorn(f, after, owed: false);
                }
                RaiseDiff(before, after);
                return;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Looks: PUT failed soft: {E}", ex.Message); }
        finally { _pushing.Release(); }
    }

    /// <summary>
    /// A counter reply that carries the looks (<c>shop-state</c>, the sheet's <c>shop-equip</c>). Without an owed push the
    /// server's looks are adopted. With one, only the slot the sheet itself just wrote (<paramref name="wroteSlot"/>) is
    /// taken from the answer, the rest stays as picked here, the body's <c>equip</c> is rewritten to match (so the sheet
    /// shows what the Studio shows) and the push goes up. Never throws.
    /// </summary>
    public void FromCounter(string account, JObject body, string? wroteSlot)
    {
        try
        {
            if (LookEquipRules.Read(body["equip"]) is not { } server) return;
            var shown = Adopt(account, server, wroteSlot);
            if (shown != null) body["equip"] = LookEquipRules.ToJson(shown);
        }
        catch (Exception ex) { App.Logger?.Debug("Looks: counter reply not read: {E}", ex.Message); }
    }

    /// <summary>A counter refusal: <c>unavailable</c> from the equip route means the server does not sell looks yet.</summary>
    public void CounterRefused(string op, string? reason)
    {
        if (op == PrizeCounter.PrizeCounterRelay.OpEquip && reason == "unavailable") NoteOff("the counter's equip answered unavailable");
    }

    /// <summary>The grants changed: keep the looks owned now for the next launch, and repaint what reads them.</summary>
    internal void OnGrantsChanged()
    {
        var account = SafeAccount();
        if (account == null) return;
        bool changed;
        lock (_gate)
        {
            var f = Load();
            if (f.Account != account) return;
            changed = RefreshOwned(f);
        }
        if (!changed) return;
        foreach (var s in new[] { WornLooks.Bubble, WornLooks.Card, WornLooks.Behind, WornLooks.Word, WornLooks.Fog, WornLooks.Mix, WornLooks.Fog2 })
            SafeRaise(s);
    }

    /// <summary>Keep the looks the grants say are owned now (nothing changes while no grants are held). True on a change.</summary>
    private bool RefreshOwned(LookCacheFile f)
    {
        var known = LookWardrobe.Catalogue.Where(e => e.Price > 0).Select(e => (e.Id, Has: SafeGrant(e.Id))).ToList();
        if (known.All(k => k.Has == null)) return false;
        var now = known.Where(k => k.Has == true).Select(k => k.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (now.SequenceEqual(f.Owned)) return false;
        f.Owned = now;
        Save(f);
        return true;
    }

    private bool? SafeGrant(string id) { try { return _grant("cos." + id); } catch { return null; } }

    /// <summary>Settle a server copy. Answers what this PC now wears (null = another account's answer, ignored).</summary>
    private LookSet? Adopt(string account, LookSet server, string? wrote)
    {
        if (SafeAccount() != account) return null;
        LookSet before, next;
        bool push;
        lock (_gate)
        {
            var f = Load();
            if (f.Account != account) StartFor(f, account);
            before = _worn;
            push = f.PushOwed;
            next = push ? Merge(_worn, server, wrote) : server;
            if (next != before || f.Looks == null) SetWorn(f, next, owed: push);
            RefreshOwned(f);
        }
        RaiseDiff(before, next);
        if (push) _ = PushAsync();
        return next;
    }

    /// <summary>The local picks with the one slot the server just wrote taken from its answer.</summary>
    private static LookSet Merge(LookSet local, LookSet server, string? wrote) => wrote switch
    {
        WornLooks.Bubble => local with { Bubble = server.Bubble },
        WornLooks.Card => local with { Card = server.Card },
        WornLooks.Behind => local with { Behind = server.Behind },
        WornLooks.Word => local with { Word = server.Word },
        WornLooks.Mix or WornLooks.Fog or WornLooks.Fog2 => local with { Mix = server.Mix, Fog2 = server.Fog2 },
        _ => local,
    };

    // ---------------------------------------------------------------- plumbing

    private void StartFor(LookCacheFile f, string account)
    {
        // A new account starts clean: another account's picks and owed push mean nothing here.
        f.Account = account; f.PushOwed = false; f.Owned = new(); f.Looks = null;
        _worn = LookSet.Default;
        _seq++;
    }

    private void SetWorn(LookCacheFile f, LookSet next, bool owed)
    {
        _worn = next;
        _seq++;
        f.Looks = LookEquipRules.ToJson(next);
        f.PushOwed = owed;
        Save(f);
    }

    private void RaiseDiff(LookSet before, LookSet after)
    {
        foreach (var s in LookSet.Diff(before, after)) SafeRaise(s);
    }

    private void SafeRaise(string slot)
    {
        try { Raise(slot); } catch (Exception ex) { App.Logger?.Debug("Looks: a listener threw: {E}", ex.Message); }
    }

    private bool IsOff() => _now() < _offUntil;

    private void NoteOff(string why)
    {
        bool first = !IsOff();
        _offUntil = _now() + OffBackoff;
        if (first) App.Logger?.Information("Looks: account equip not on the server yet ({Why}); free looks only, asking again in {Min} min", why, (int)OffBackoff.TotalMinutes);
    }

    private LookCacheFile Load()
    {
        if (_file != null) return _file;
        LookCacheFile? f = null;
        try
        {
            if (_path != null && File.Exists(_path)) f = JsonConvert.DeserializeObject<LookCacheFile>(File.ReadAllText(_path), NoDates);
        }
        catch (Exception ex) { App.Logger?.Debug("Looks: cache unreadable, starting clean: {E}", ex.Message); }
        f ??= new LookCacheFile();
        f.Owned ??= new();
        _file = f;
        _worn = LookEquipRules.Read(f.Looks) ?? LookSet.Default;
        return f;
    }

    private void Save(LookCacheFile f)
    {
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(f, Formatting.Indented), new UTF8Encoding(false));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) { App.Logger?.Debug("Looks: cache not saved: {E}", ex.Message); }
    }

    private string? SafeAccount() { try { return _account(); } catch { return null; } }
    private (string UnifiedId, string Token)? SafeIdentity() { try { return _identity(); } catch { return null; } }

    private static JObject? Parse(string text)
    {
        try { return JsonConvert.DeserializeObject(text, NoDates) as JObject; } catch { return null; }
    }
}
