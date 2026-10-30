using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>An open Racing Thoughts 3D (Glassroad) room off <c>GET /v2/race/open</c>.
/// <paramref name="StartsInSec"/> is null while the room waits for no countdown.</summary>
public sealed record RaceOpenRoom(string RoomId, string Track, string HostName, int Seats, int Filled,
    int Laps, bool Staked, int? StartsInSec, bool Friend)
{
    /// <summary>The host's frame/avatar off an optional <c>cosmetics</c> (server-clamped); none when absent.</summary>
    public PlayerLook Look { get; init; } = PlayerLook.None;
}

/// <summary>The race answer. <paramref name="Error"/> is the server's code (<c>tier</c>,
/// <c>preview</c>, <c>auth</c>, <c>rate</c>) or null. A <c>tier</c> refusal is an answer, not a fault.</summary>
public sealed record RaceOpenReply(IReadOnlyList<RaceOpenRoom> Rooms, bool Ok, string? Error = null)
{
    public static readonly RaceOpenReply Empty = new(Array.Empty<RaceOpenRoom>(), false);
    public bool TierLocked => Error == "tier";
}

/// <summary>
/// The open Glassroad races, read with the chess lobby's desktop auth:
/// <c>GET /v2/race/open?unified_id=</c> plus the account token. A Join opens Racing Thoughts in the
/// app on the room (<see cref="RaceJoinLink"/>). Never throws; any fault reads as
/// <see cref="RaceOpenReply.Empty"/>.
/// </summary>
public sealed class RaceOpenApi
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    public const int MaxRows = 30;

    private static readonly HttpClient SharedHttp = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    private static readonly JsonSerializerSettings ReadSettings = new() { DateParseHandling = DateParseHandling.None };

    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public RaceOpenApi(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? SharedHttp;
        _identity = identity ?? BackRoomApi.AppIdentity;
        _baseUrl = baseUrl ?? BackRoomApi.BaseUrl;
    }

    public async Task<RaceOpenReply> FetchAsync(CancellationToken ct = default)
    {
        var id = _identity();
        if (id == null) return RaceOpenReply.Empty;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(Timeout);
        try
        {
            var url = $"{_baseUrl}/v2/race/open?unified_id={Uri.EscapeDataString(id.Value.UnifiedId)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-Auth-Token", id.Value.Token);
            using var res = await _http.SendAsync(req, budget.Token).ConfigureAwait(false);
            // Refusals carry { error: code } on a 4xx: read the body either way.
            var text = await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            return Parse(text);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("RaceOpen: fetch failed: {E}", ex.Message);
            return RaceOpenReply.Empty;
        }
    }

    /// <summary>Pure. <c>{ error }</c> is a refusal; otherwise <c>rows</c> must be an array. A row
    /// whose roomId is not a well-formed room id is dropped (it could never be joined).</summary>
    internal static RaceOpenReply Parse(string? text)
    {
        JObject? o = null;
        try { o = JsonConvert.DeserializeObject<JToken>(text ?? "", ReadSettings) as JObject; } catch { }
        if (o == null) return RaceOpenReply.Empty;
        if (Str(o["error"]) is { Length: > 0 } err)
            return new RaceOpenReply(Array.Empty<RaceOpenRoom>(), err == "tier", err);
        if (o["rows"] is not JArray rows) return RaceOpenReply.Empty;

        var list = new List<RaceOpenRoom>();
        foreach (var t in rows)
        {
            if (list.Count >= MaxRows) break;
            if (t is not JObject r) continue;
            var roomId = Str(r["roomId"]);
            if (!RaceJoinLink.IsRoomId(roomId)) continue;
            var host = r["host"] is JObject h ? Str(h["name"]) : null;
            int seats = (int)Math.Clamp(Long(r["seats"]), 0, 99);
            int filled = (int)Math.Clamp(Long(r["filled"]), 0, 99);
            int? startsIn = r["startsIn"]?.Type is JTokenType.Integer or JTokenType.Float
                ? (int)Math.Clamp(Long(r["startsIn"]), 0, 86400)
                : null;
            list.Add(new RaceOpenRoom(roomId!, Str(r["track"]) ?? "", string.IsNullOrEmpty(host) ? "someone" : host!,
                seats, filled, (int)Math.Clamp(Long(r["laps"]), 0, 99), Bool(r["staked"]), startsIn, Bool(r["friend"]))
                { Look = PlayerLook.From(r["cosmetics"]) });
        }
        return new RaceOpenReply(list, true);
    }

    /// <summary>Track id to its course name; an unknown id shows as nothing.</summary>
    public static string TrackName(string? track) => track switch
    {
        "threshold" => "Threshold",
        "mainframe" => "Mainframe",
        "keep" => "The Keep",
        "boudoir" => "Boudoir",
        "goonworks" => "GOONWORKS",
        _ => "",
    };

    private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;
    private static bool Bool(JToken? t) => t?.Type == JTokenType.Boolean && t.Value<bool>();

    private static long Long(JToken? t) => t?.Type switch
    {
        JTokenType.Integer => t.Value<long>(),
        JTokenType.Float => (long)t.Value<double>(),
        _ => 0,
    };
}

/// <summary>The in-app door for a race room: the Glassroad page on <c>?join=&lt;roomId&gt;</c> (the page
/// takes it once, boot.js joinParam). Refuses anything that is not a 12-character lowercase room id.</summary>
public static class RaceJoinLink
{
    public const string Base = "https://ccp.game/glassroad/index.html";
    // \z, not $: a .NET $ also matches before a trailing newline.
    private static readonly Regex RoomId = new(@"^[a-z0-9]{12}\z", RegexOptions.CultureInvariant);

    public static bool IsRoomId(string? id) => id != null && RoomId.IsMatch(id);

    public static Uri? For(string? roomId) =>
        IsRoomId(roomId) ? new Uri(Base + "?join=" + roomId, UriKind.Absolute) : null;
}
