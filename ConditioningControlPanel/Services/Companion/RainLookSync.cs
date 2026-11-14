using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>The server's Rain copy in a <c>/v2/equip</c> reply. <see cref="Present"/> is false while the
/// account never stored one (null or missing <c>equip.rain</c>); <see cref="Look"/> is the
/// <see cref="RainLookCodec"/> form ("" = Rain's default look).</summary>
internal readonly record struct RainEquipCopy(bool Present, string Look, DateTime? UpdatedUtc);

internal enum RainSyncStep { None, Push, Adopt }

/// <summary>
/// RAIN LOOK SYNC (72-PR-LANES-BRIEF contract 3; owner 2026-10-30 "sync everything"): Rain's look follows
/// the account through <c>GET /v2/equip</c> and <c>PUT /v2/equip {unified_id, family:"rain", value}</c>.
/// The baked clip packs never sync (machine-local, <see cref="RainPackStore"/>): an adopted look is
/// re-baked on this PC the next time the dressing room opens.
///
/// <para><b>The rules.</b> The wardrobe's Apply writes locally first and marks a push owed
/// (<c>RainLookPushOwed</c>); the push runs in the background. At sign-in / launch (the first validate
/// prizes block of a run) the sync pulls: a push still owed goes up first, otherwise a server copy
/// whose <c>updatedAt</c> is newer than the last agreement is adopted. Every failure is SOFT: a missing
/// route (404 until lane L6 deploys), a network error or a timeout keeps the local look and the owed
/// flag, and the next run tries again.</para>
///
/// <para>Continuations run on the caller's context: the app calls from the UI thread, so the settings
/// hooks run there too. Tests have no context and run inline.</para>
/// </summary>
internal sealed class RainLookSync
{
    public const string Family = "rain";
    public const string Route = "/v2/equip";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(12);

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    internal sealed class Hooks
    {
        public required Func<(string UnifiedId, string Token)?> Identity { get; init; }
        /// <summary>The stored look, the push-owed flag and the last agreement, or null (no settings).</summary>
        public required Func<(string Look, bool PushOwed, DateTime? AgreedUtc)?> Local { get; init; }
        /// <summary>The two sides agree on <c>look</c> at <c>updatedAt</c>: clear the owed flag, stamp.</summary>
        public required Action<string, DateTime?> Agreed { get; init; }
        /// <summary>Take the server copy (clamp, pack, tube refresh).</summary>
        public required Action<string, DateTime?> Adopt { get; init; }
        public Action<string>? Log { get; init; }
        public string BaseUrl { get; init; } = BackRoomApi.BaseUrl;
    }

    private readonly HttpClient _http;
    private readonly Hooks _h;
    private bool _running, _again;

    public RainLookSync(HttpClient? http, Hooks hooks)
    {
        _http = http ?? SharedHttp;
        _h = hooks ?? throw new ArgumentNullException(nameof(hooks));
    }

    // ------------------------------------------------------------------ pure

    /// <summary>The Rain copy from a reply body, or null when the body is not an ok equip reply.</summary>
    internal static RainEquipCopy? ReadEquip(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        JObject o;
        try
        {
            using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None };
            if (JToken.ReadFrom(reader) is not JObject parsed) return null;
            o = parsed;
        }
        catch (JsonException) { return null; }
        if (o.Value<bool?>("ok") != true || o["equip"] is not JObject equip) return null;
        var at = ReadTime(equip["updatedAt"]);
        return equip[Family] is JObject rain
            ? new RainEquipCopy(true, RainLookCodec.Normalize(rain.ToString(Formatting.None)), at)
            : new RainEquipCopy(false, "", at);
    }

    /// <summary>ISO 8601 text or epoch milliseconds, as UTC; anything else is null.</summary>
    internal static DateTime? ReadTime(JToken? t)
    {
        if (t == null) return null;
        if (t.Type == JTokenType.Integer)
        {
            var ms = t.Value<long>();
            return ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;
        }
        if (t.Type == JTokenType.String && DateTimeOffset.TryParse((string?)t, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;
        return null;
    }

    /// <summary>What one sync does. A push owed always goes up first; a server that never stored a look
    /// gets ours (when ours is not the default); a different server copy newer than the last agreement
    /// (or with no agreement yet) is adopted; an older one is overwritten by ours.
    /// The server stores Rain's DEFAULT look as null (CCP-Server equip.js clampRain), so a null copy is
    /// "never set" only until this PC has agreed once: after an agreement, a null copy newer than it means
    /// another device chose the default, and that is adopted like any other newer look.</summary>
    internal static RainSyncStep Decide(string? localLook, bool pushOwed, DateTime? agreedUtc, RainEquipCopy server)
    {
        var local = RainLookCodec.Normalize(localLook);
        if (pushOwed) return RainSyncStep.Push;
        if (!server.Present)
        {
            if (local.Length == 0) return RainSyncStep.None;
            return agreedUtc != null && server.UpdatedUtc != null && server.UpdatedUtc > agreedUtc
                ? RainSyncStep.Adopt
                : RainSyncStep.Push;
        }
        if (string.Equals(server.Look, local, StringComparison.Ordinal)) return RainSyncStep.None;
        if (agreedUtc == null || server.UpdatedUtc == null || server.UpdatedUtc > agreedUtc) return RainSyncStep.Adopt;
        return RainSyncStep.Push;
    }

    /// <summary>The look as the wire's <c>value</c>: always an object (the default is empty items). The
    /// server answers the default as <c>rain: null</c>; <see cref="Decide"/> and the push receipt read it so.</summary>
    internal static JObject LookValue(string? look)
    {
        var n = RainLookCodec.Normalize(look);
        return n.Length > 0
            ? JObject.Parse(n)
            : new JObject { ["rev"] = RainLookCodec.Rev, ["items"] = new JObject(), ["colors"] = new JObject() };
    }

    internal static JObject PutBody(string unifiedId, string? look) => new()
    {
        ["unified_id"] = unifiedId,
        ["family"] = Family,
        ["value"] = LookValue(look),
    };

    // ------------------------------------------------------------------ the run

    /// <summary>One sync, never throws. A call while one runs makes that run go round once more.</summary>
    public async Task SyncAsync(CancellationToken ct = default)
    {
        if (_running) { _again = true; return; }
        _running = true;
        try
        {
            do
            {
                _again = false;
                await OnceAsync(ct);
            } while (_again && !ct.IsCancellationRequested);
        }
        catch (Exception ex) { _h.Log?.Invoke("sync failed: " + ex.Message); }
        finally { _running = false; }
    }

    private async Task OnceAsync(CancellationToken ct)
    {
        var id = SafeIdentity();
        var local = _h.Local();
        if (id == null || local == null) return;
        if (local.Value.PushOwed) { await PushAsync(id.Value, local.Value.Look, ct); return; }

        var body = await SendAsync(HttpMethod.Get, id.Value, null, ct);
        if (body == null || SafeIdentity()?.UnifiedId != id.Value.UnifiedId) return;
        if (ReadEquip(body) is not { } server) { _h.Log?.Invoke("pull: reply without an equip block"); return; }

        local = _h.Local();
        if (local == null) return;
        switch (Decide(local.Value.Look, local.Value.PushOwed, local.Value.AgreedUtc, server))
        {
            case RainSyncStep.Push:
                await PushAsync(id.Value, local.Value.Look, ct);
                break;
            case RainSyncStep.Adopt:
                _h.Log?.Invoke("pull: adopting the server's newer look");
                _h.Adopt(server.Look, server.UpdatedUtc);
                break;
            default:
                if (server.Present) _h.Agreed(local.Value.Look, server.UpdatedUtc);
                break;
        }
    }

    private async Task PushAsync((string UnifiedId, string Token) id, string look, CancellationToken ct)
    {
        var body = await SendAsync(HttpMethod.Put, id, PutBody(id.UnifiedId, look), ct);
        if (body == null || SafeIdentity()?.UnifiedId != id.UnifiedId) return;   // owed stays: next run
        // An ok equip reply with rain: null is the server's default look (a default push, or a look the
        // clamp emptied): it compares as "" below. Only a reply with no equip block keeps the push owed.
        if (ReadEquip(body) is not { } server) { _h.Log?.Invoke("push: reply without an equip block"); return; }
        var now = _h.Local();
        if (now == null) return;
        // Changed again while the PUT was out: that change owes its own push.
        if (!string.Equals(RainLookCodec.Normalize(now.Value.Look), RainLookCodec.Normalize(look), StringComparison.Ordinal)) { _again = true; return; }
        if (string.Equals(server.Look, RainLookCodec.Normalize(look), StringComparison.Ordinal)) _h.Agreed(look, server.UpdatedUtc);
        else
        {
            _h.Log?.Invoke("push: the server clamped the look; adopting its copy");
            _h.Adopt(server.Look, server.UpdatedUtc);
        }
    }

    /// <summary>The reply body on a 2xx, else null (route missing, refused, offline, timed out).</summary>
    private async Task<string?> SendAsync(HttpMethod method, (string UnifiedId, string Token) id, JObject? body, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            using var req = method == HttpMethod.Get
                ? new HttpRequestMessage(HttpMethod.Get, $"{_h.BaseUrl}{Route}?unified_id={Uri.EscapeDataString(id.UnifiedId)}")
                : new HttpRequestMessage(method, _h.BaseUrl + Route)
                {
                    Content = new StringContent(body!.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                };
            req.Headers.Add("X-Auth-Token", id.Token);
            using var res = await _http.SendAsync(req, budget.Token);
            var text = await res.Content.ReadAsStringAsync(budget.Token);
            if (res.IsSuccessStatusCode) return text;
            _h.Log?.Invoke($"{method} {Route}: HTTP {(int)res.StatusCode}, keeping the local look");
            return null;
        }
        catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException)
        {
            _h.Log?.Invoke($"{method} {Route}: {ex.GetType().Name}, keeping the local look");
            return null;
        }
    }

    private (string UnifiedId, string Token)? SafeIdentity()
    {
        try { return _h.Identity(); } catch { return null; }
    }

    // ------------------------------------------------------------------ re-bake rule

    /// <summary>True when the stored look has no matching pack on this PC (adopted from another device,
    /// restored from a backup, or a clip-set bump): the dressing room bakes it on its next open.</summary>
    internal static bool RebakeOwed(string? look, string? packHash, Func<string, bool> hasPack)
    {
        if (RainLookCodec.IsDefault(look)) return false;
        var hash = RainLookHash.Compute(look);
        return !string.Equals(hash, packHash, StringComparison.Ordinal) || !hasPack(hash);
    }

    /// <summary>The pack an adopted look plays now: its own when this PC has it, else the embedded
    /// clips ("") until the re-bake.</summary>
    internal static string PackFor(string? look, Func<string, bool> hasPack)
    {
        if (RainLookCodec.IsDefault(look)) return "";
        var hash = RainLookHash.Compute(look);
        return hasPack(hash) ? hash : "";
    }

    // ------------------------------------------------------------------ the app's one

    private static RainLookSync? _app;
    private static string? _pulledFor;

    private static RainLookSync App_ => _app ??= new RainLookSync(null, new Hooks
    {
        Identity = BackRoomApi.AppIdentity,
        Local = () => App.Settings?.Current is { } s ? (s.RainLook, s.RainLookPushOwed, s.RainLookAgreedUtc) : null,
        Agreed = (look, at) =>
        {
            if (App.Settings?.Current is not { } s) return;
            if (string.Equals(RainLookCodec.Normalize(s.RainLook), RainLookCodec.Normalize(look), StringComparison.Ordinal))
                s.RainLookPushOwed = false;
            s.RainLookAgreedUtc = at;
            App.Settings.Save();
        },
        Adopt = AdoptOnThisPc,
        Log = line => App.Logger?.Information("RainLookSync: {Line}", line),
    });

    private static bool HasPack(string hash)
    {
        try { return RainPackStore.CreateDefault().TryGetFolder(hash) != null; }
        catch (Exception) { return false; }
    }

    /// <summary>The account is known (a validate prizes block landed, grants applied): pull once per
    /// account per run. UI thread.</summary>
    public static void OnAccountReady(string? account)
    {
        if (string.IsNullOrEmpty(account) || string.Equals(account, _pulledFor, StringComparison.Ordinal)) return;
        if (!string.Equals(account, BackRoomApi.AppIdentity()?.UnifiedId, StringComparison.Ordinal)) return;
        _pulledFor = account;
        _ = App_.SyncAsync();
    }

    /// <summary>The wardrobe's Apply committed a look on this PC: owe a push and send it. UI thread.</summary>
    public static void NoteLocalChange()
    {
        if (App.Settings?.Current is not { } s) return;
        s.RainLookPushOwed = true;
        App.Settings.Save();
        _ = App_.SyncAsync();
    }

    /// <summary>The live re-bake rule for the dressing room's ready frame: the clamped look to bake, or null.</summary>
    internal static string? LookOwingABake()
    {
        var s = App.Settings?.Current;
        if (s == null) return null;
        var look = RainWardrobeCatalog.Clamp(s.RainLook, App.Ownership?.Grants);
        return RebakeOwed(look, s.RainPackHash, HasPack) ? look : null;
    }

    private static void AdoptOnThisPc(string serverLook, DateTime? at)
    {
        if (App.Settings?.Current is not { } s) return;
        // A dressing room on screen owns the look until it closes.
        if (RainWardrobeHostService.IsActive)
        {
            App.Logger?.Information("RainLookSync: adopt deferred, the dressing room is open");
            _pulledFor = null;   // the next validate prizes block pulls again
            return;
        }
        var look = RainWardrobeCatalog.Clamp(serverLook, App.Ownership?.Grants);
        s.RainLook = look;
        s.RainPackHash = PackFor(look, HasPack);
        s.RainLookPushOwed = false;
        s.RainLookAgreedUtc = at;
        App.Settings.Save();
        App.AvatarWindow?.RefreshRainLook();
        // RainV2Gate off: the look is adopted silently, never a toast about a wardrobe nobody can open.
        if (RainV2Gate.On && RebakeOwed(look, s.RainPackHash, HasPack))
            App.Notifications?.Show(Loc.Get("rain_look_synced_rebake"), NotificationType.Info, TimeSpan.FromSeconds(8));
    }
}
