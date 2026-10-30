using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// EMI desk equip sync (lanes brief, fixed contract 3): <c>GET /v2/equip</c> answers
/// <c>{ ok, equip: { desk: { hat, toy }, updatedAt, ... } }</c>; <c>PUT /v2/equip { unified_id, family: 'desk', value }</c>
/// stores a clamped copy and answers the whole <c>equip</c>. The desk writes locally first, PUTs in the
/// background, and adopts the server copy at launch only when its <c>updatedAt</c> is newer.
///
/// <para><b>Fail soft.</b> The route deploys after this client ships: a 404, an offline app, a signed-out
/// account or any junk answer leaves the local pick exactly as it is and says nothing.</para>
/// </summary>
public static class DeskEquipSync
{
    public const string Family = "desk";
    public const string Path = "/v2/equip";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string? _pulledFor;
    private static readonly JsonSerializerSettings NoDates = new() { DateParseHandling = DateParseHandling.None };

    /// <summary>Raised on the calling thread after a server copy was adopted into settings.</summary>
    public static event Action? Adopted;

    /// <summary>The PUT body for a desk pick. Pure.</summary>
    public static JObject BuildPut(string unifiedId, DeskLook look) => new()
    {
        ["unified_id"] = unifiedId,
        ["family"] = Family,
        ["value"] = new JObject { ["hat"] = OrNull(look.Hat), ["toy"] = OrNull(look.Toy) },
    };

    /// <summary>
    /// The desk copy and its stamp out of a GET or PUT reply, or null when the reply carries none. Accepts
    /// <c>updatedAt</c> as unix ms or an ISO time. Pure, never throws.
    /// </summary>
    public static (DeskLook Look, long UpdatedMs)? ReadDesk(JObject? reply)
    {
        try
        {
            if (reply == null || reply.Value<bool?>("ok") != true) return null;
            if (reply["equip"] is not JObject equip || equip["desk"] is not JObject desk) return null;
            var look = new DeskLook(Str(desk["hat"]), Str(desk["toy"]));
            return (look, Stamp(equip["updatedAt"]));
        }
        catch { return null; }
    }

    /// <summary>True when a server copy stamped <paramref name="serverMs"/> should replace the local pick.</summary>
    public static bool ShouldAdopt(long serverMs, long localMs) => serverMs > 0 && serverMs > localMs;

    /// <summary>Write the pick locally (clamped) and send it. The caller is on the UI thread.</summary>
    public static void Save(DeskLook pick)
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            var clean = EmiDeskItems.Clamp(pick, EmiDeskItems.KnownGrants());
            s.EmiDeskHat = clean.Hat;
            s.EmiDeskToy = clean.Toy;
            s.EmiDeskEquipUpdatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            App.Settings?.Save();
            _ = PutAsync(clean);
        }
        catch (Exception ex) { App.Logger?.Debug("DeskEquipSync save failed: {E}", ex.Message); }
    }

    private static async Task PutAsync(DeskLook look)
    {
        var id = BackRoomApi.AppIdentity();
        if (id == null) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, BackRoomApi.BaseUrl + Path)
            {
                Content = new StringContent(BuildPut(id.Value.UnifiedId, look).ToString(Formatting.None),
                    Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await Http.SendAsync(req).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                App.Logger?.Debug("DeskEquipSync PUT answered {Status}; keeping the local pick", (int)res.StatusCode);
        }
        catch (Exception ex) { App.Logger?.Debug("DeskEquipSync PUT failed soft: {E}", ex.Message); }
    }

    /// <summary>
    /// Once per account per launch: read the server copy and adopt it when newer. Settings are written on the
    /// dispatcher the caller supplies (the desk passes its own). Never throws.
    /// </summary>
    public static async Task PullOnceAsync(Action<Action> onUi, CancellationToken ct = default)
    {
        var id = BackRoomApi.AppIdentity();
        if (id == null || _pulledFor == id.Value.UnifiedId) return;
        _pulledFor = id.Value.UnifiedId;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{BackRoomApi.BaseUrl}{Path}?unified_id={Uri.EscapeDataString(id.Value.UnifiedId)}");
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return;
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JObject? o = null;
            try { o = JsonConvert.DeserializeObject(body, NoDates) as JObject; } catch { }
            if (ReadDesk(o) is not { } server) return;
            onUi(() => Adopt(id.Value.UnifiedId, server.Look, server.UpdatedMs));
        }
        catch (Exception ex) { App.Logger?.Debug("DeskEquipSync GET failed soft: {E}", ex.Message); }
    }

    private static void Adopt(string account, DeskLook look, long updatedMs)
    {
        try
        {
            var s = App.Settings?.Current;
            if (s == null || BackRoomApi.AppIdentity()?.UnifiedId != account) return;
            if (!ShouldAdopt(updatedMs, s.EmiDeskEquipUpdatedMs)) return;
            var clean = EmiDeskItems.Clamp(look, EmiDeskItems.KnownGrants());
            s.EmiDeskHat = clean.Hat;
            s.EmiDeskToy = clean.Toy;
            s.EmiDeskEquipUpdatedMs = updatedMs;
            App.Settings?.Save();
            Adopted?.Invoke();
        }
        catch (Exception ex) { App.Logger?.Debug("DeskEquipSync adopt failed: {E}", ex.Message); }
    }

    private static JToken OrNull(string? id) => id == null ? JValue.CreateNull() : new JValue(id);

    private static string? Str(JToken? t)
    {
        if (t is not JValue { Type: JTokenType.String } v) return null;
        var s = ((string?)v)?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    private static long Stamp(JToken? t)
    {
        if (t is JValue { Type: JTokenType.Integer or JTokenType.Float } n) return (long)n.Value<double>();
        if (t is JValue { Type: JTokenType.Date, Value: DateTime dt })
            return new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime())
                .ToUnixTimeMilliseconds();
        if (t is JValue { Type: JTokenType.Date, Value: DateTimeOffset dto }) return dto.ToUnixTimeMilliseconds();
        if (t is JValue { Type: JTokenType.String } s
            && DateTimeOffset.TryParse((string?)s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            return at.ToUnixTimeMilliseconds();
        return 0;
    }
}
