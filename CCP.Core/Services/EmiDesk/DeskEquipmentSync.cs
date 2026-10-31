using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.EmiDesk;

public sealed class DeskEquipmentSync : IDisposable
{
    public const string Family = "desk";
    public const string Path = "/v2/equip";

    private readonly HttpClient Http;
    private readonly Func<ConditioningControlPanel.Models.AppSettings> Settings;
    private readonly Action SaveSettings;
    private readonly Func<(string UnifiedId, string Token)?> Identity;
    private readonly Func<System.Collections.Generic.IEnumerable<string>?> Grants;
    private readonly Action<Action> Post;
    private readonly string BaseUrl;
    private CancellationTokenSource _stop = new();
    public DeskEquipmentSync(HttpClient http, Func<ConditioningControlPanel.Models.AppSettings> settings,
        Action save, Func<(string UnifiedId, string Token)?> identity,
        Func<System.Collections.Generic.IEnumerable<string>?> grants, Action<Action> post, string baseUrl)
    { Http = http; Settings = settings; SaveSettings = save; Identity = identity; Grants = grants; Post = post; BaseUrl = baseUrl; }
    public void Stop() { _stop.Cancel(); }
    public void Resume() { if (_stop.IsCancellationRequested) { _stop.Dispose(); _stop = new(); } }
    public void Dispose() { Stop(); _stop.Dispose(); }
    private string? _pulledFor;
    private static readonly JsonSerializerSettings NoDates = new() { DateParseHandling = DateParseHandling.None };

    public event Action? Adopted;

    public static JObject BuildPut(string unifiedId, DeskEquipmentLook look) => new()
    {
        ["unified_id"] = unifiedId,
        ["family"] = Family,
        ["value"] = new JObject { ["hat"] = OrNull(look.Hat), ["toy"] = OrNull(look.Toy) },
    };

    public static (DeskEquipmentLook Look, long UpdatedMs)? ReadDesk(JObject? reply)
    {
        try
        {
            if (reply == null || reply.Value<bool?>("ok") != true) return null;
            if (reply["equip"] is not JObject equip || equip["desk"] is not JObject desk) return null;
            var look = new DeskEquipmentLook(Str(desk["hat"]), Str(desk["toy"]));
            return (look, Stamp(equip["updatedAt"]));
        }
        catch { return null; }
    }

    public static bool ShouldAdopt(long serverMs, long localMs) => serverMs > 0 && serverMs > localMs;

    public Task SaveAsync(DeskEquipmentLook pick)
    {
        try
        {
            var s = Settings();
            if (s == null) return Task.CompletedTask;
            var clean = DeskEquipment.Clamp(pick, Grants());
            var stamp = Math.Max(s.EmiDeskEquipUpdatedMs + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            s.EmiDeskHat = clean.Hat;
            s.EmiDeskToy = clean.Toy;
            s.EmiDeskEquipUpdatedMs = stamp;
            s.EmiDeskEquipPushOwed = true;
            SaveSettings();
            return PutAsync(clean, stamp, Post, _stop.Token);
        }
        catch (Exception ex) { Serilog.Log.Debug("DeskEquipmentSync save failed: {E}", ex.Message); return Task.CompletedTask; }
    }

    private async Task PutAsync(DeskEquipmentLook look, long stamp, Action<Action> onUi, CancellationToken ct)
    {
        var id = Identity();
        if (ct.IsCancellationRequested || id == null) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, BaseUrl + Path)
            {
                Content = new StringContent(BuildPut(id.Value.UnifiedId, look).ToString(Formatting.None),
                    Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                Serilog.Log.Debug("DeskEquipmentSync PUT answered {Status}; keeping the local pick", (int)res.StatusCode);
                return;
            }
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JObject? o = null;
            try { o = JsonConvert.DeserializeObject(body, NoDates) as JObject; } catch { }
            if (ReadDesk(o) == null) return;
            // Stored: the push is no longer owed, unless she was re-dressed while this PUT was out.
            onUi(() =>
            {
                try
                {
                    var s = Settings();
                    if (ct.IsCancellationRequested || Identity()?.UnifiedId != id.Value.UnifiedId || s == null || s.EmiDeskEquipUpdatedMs != stamp || !s.EmiDeskEquipPushOwed) return;
                    s.EmiDeskEquipPushOwed = false;
                    SaveSettings();
                }
                catch (Exception ex) { Serilog.Log.Debug("DeskEquipmentSync PUT settle failed: {E}", ex.Message); }
            });
        }
        catch (Exception ex) { Serilog.Log.Debug("DeskEquipmentSync PUT failed soft: {E}", ex.Message); }
    }

    public async Task PullOnceAsync()
    {
        var ct = _stop.Token;
        var onUi = Post;
        var id = Identity();
        if (ct.IsCancellationRequested || id == null || _pulledFor == id.Value.UnifiedId) return;
        _pulledFor = id.Value.UnifiedId;
        try
        {
            // A pick that never reached the server goes up first instead of being judged against a server
            // stamp: updatedAt is shared by every family, so another family's PUT can make an empty desk
            // copy look newer than a pick made while the route was missing. UI thread here (the desk's
            // refresh), so the settings read is safe.
            var s = Settings();
            if (s != null && s.EmiDeskEquipPushOwed)
            {
                var owed = DeskEquipment.Clamp(new DeskEquipmentLook(s.EmiDeskHat, s.EmiDeskToy), Grants());
                await PutAsync(owed, s.EmiDeskEquipUpdatedMs, onUi, ct).ConfigureAwait(false);
                return;
            }

            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}{Path}?unified_id={Uri.EscapeDataString(id.Value.UnifiedId)}");
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return;
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            JObject? o = null;
            try { o = JsonConvert.DeserializeObject(body, NoDates) as JObject; } catch { }
            if (ReadDesk(o) is not { } server) return;
            onUi(() => { if (!ct.IsCancellationRequested) Adopt(id.Value.UnifiedId, server.Look, server.UpdatedMs); });
        }
        catch (Exception ex) { Serilog.Log.Debug("DeskEquipmentSync GET failed soft: {E}", ex.Message); }
    }

    private void Adopt(string account, DeskEquipmentLook look, long updatedMs)
    {
        try
        {
            var s = Settings();
            if (s == null || Identity()?.UnifiedId != account) return;
            // A pick made while the GET was out owes its own push and wins.
            if (s.EmiDeskEquipPushOwed || !ShouldAdopt(updatedMs, s.EmiDeskEquipUpdatedMs)) return;
            var clean = DeskEquipment.Clamp(look, Grants());
            s.EmiDeskHat = clean.Hat;
            s.EmiDeskToy = clean.Toy;
            s.EmiDeskEquipUpdatedMs = updatedMs;
            SaveSettings();
            Adopted?.Invoke();
        }
        catch (Exception ex) { Serilog.Log.Debug("DeskEquipmentSync adopt failed: {E}", ex.Message); }
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
