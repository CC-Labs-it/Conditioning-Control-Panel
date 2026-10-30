using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Glassroad;

/// <summary>
/// The pure half of the Glassroad host (<see cref="GlassroadHostService"/>): the host init frame
/// and the net relay's signing rule. No I/O, no App statics, so every rule here is tested without
/// a window. The door is open for everyone since 7.2 (LauncherCatalogue "race").
/// </summary>
internal static class GlassroadRules
{
    /// <summary>The only prefix the relay forwards (the race lobby). Judged on the built Uri by
    /// <see cref="HostNetPathRule"/>.</summary>
    public const string AllowedPathPrefix = HostNetPathRule.RacePrefix;

    /// <summary>The web courier's body cap, kept the same here.</summary>
    public const int MaxBodyBytes = 16 * 1024;

    /// <summary>
    /// The host init frame (host protocol v2, glassroad CONTRACT.md section 7). <c>caps.net</c> is
    /// always true on the desktop: a signed-out player gets <c>identity: null</c> and the lobby asks
    /// them to sign in. <c>songs</c> stays empty: the private race song never ships in the app.
    /// </summary>
    public static JObject InitFrame(string? unifiedId, string? displayName, int tier,
        bool gentleOn, double gentleFadeSeconds, double gentleSpeed, IEnumerable<string>? grants = null)
    {
        bool signedIn = !string.IsNullOrWhiteSpace(unifiedId);
        return new JObject
        {
            ["type"] = "init",
            ["v"] = 2,
            ["gentle"] = GentleFrame(gentleOn, gentleFadeSeconds, gentleSpeed),
            ["caps"] = new JObject
            {
                ["platform"] = "desktop", ["net"] = true, ["relayBase"] = null, ["texturableRemote"] = false,
                ["gamepad"] = true, ["touch"] = false, ["forceRenderer"] = null, ["hostSfx"] = false,
                ["trackPick"] = false, ["canSurface"] = false, ["mediaControls"] = false, ["songs"] = new JArray(),
                // the pre-race room's "Invite a friend" asks this host (host/friendInvite.js)
                ["friendInvite"] = true,
                // the garage's Prize Counter row opens the counter window (host/prizeCounter.js)
                ["prizeCounter"] = true,
            },
            ["identity"] = signedIn
                ? new JObject { ["unifiedId"] = unifiedId, ["tier"] = Math.Clamp(tier, 0, 2),
                    ["displayName"] = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName,
                    // the garage offers only the kart bodies the account owns (meta/owned.js)
                    ["grants"] = KartGrants(grants) }
                : JValue.CreateNull(),
        };
    }

    /// <summary>The Prize Counter grants the garage reads: <c>cos.kart.*</c> plus the two kart Prime families
    /// (<c>cos.prime.paint.*</c>, <c>cos.prime.trail.*</c>), sorted, at most 64 (glassroad CONTRACT.md section 7
    /// "Owned kart looks"). Everything else the account owns stays home.</summary>
    public static JArray KartGrants(IEnumerable<string>? grants)
    {
        var list = new SortedSet<string>(StringComparer.Ordinal);
        if (grants != null)
            foreach (var g in grants)
                if (g is { Length: <= 64 } && IsKartGrant(g)) list.Add(g);
        var rows = new JArray();
        foreach (var g in list) { if (rows.Count >= 64) break; rows.Add(g); }
        return rows;
    }

    private static bool IsKartGrant(string g)
        => g.StartsWith("cos.kart.", StringComparison.Ordinal)
        || g.StartsWith("cos.prime.paint.", StringComparison.Ordinal)
        || g.StartsWith("cos.prime.trail.", StringComparison.Ordinal);

    /// <summary>The Prize Counter tab the garage row asks for (<c>open-prize-counter { tab, from }</c>, 7.2
    /// contract 1): Glassroad only ever opens on <c>kart</c>, whatever the page says.</summary>
    public const string CounterTab = "kart";
    public const string CounterFrom = "glassroad";

    /// <summary>The host -> page push when the account's grants change while the window is up.</summary>
    public static JObject OwnedFrame(IEnumerable<string>? grants)
        => new JObject { ["type"] = "owned", ["grants"] = KartGrants(grants) };

    /// <summary>The page's invite list (<c>friends-list-reply</c>): online friends with a name, as
    /// <c>{ uid, name }</c>, at most 50. Offline friends are left out: the server refuses them anyway.</summary>
    public static JArray FriendRows(IEnumerable<Friend>? friends)
    {
        var rows = new JArray();
        if (friends == null) return rows;
        foreach (var f in friends)
        {
            if (rows.Count >= 50) break;
            if (f == null || !f.Online || string.IsNullOrEmpty(f.Id) || string.IsNullOrWhiteSpace(f.Name)) continue;
            rows.Add(new JObject { ["uid"] = f.Id, ["name"] = f.Name });
        }
        return rows;
    }

    /// <summary>The loc key that words a race invite's outcome: the room's fixed reason
    /// (<c>friends_race_&lt;reason&gt;</c>), else the friends drawer's usual send result.</summary>
    public static string InviteNoteKey(SendOutcome outcome)
        => outcome.Reason != null && InviteDestination.RaceReasons.Contains(outcome.Reason)
            ? "friends_race_" + outcome.Reason
            : Controls.Friends.FriendsDrawerRules.SendResultKey(outcome.Result);

    /// <summary>The page shows the note as a sentence: first letter up (the drawer's lines are lower case).</summary>
    public static string Sentence(string text)
        => string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);

    /// <summary>The <c>gentle</c> block: the player's fade (off = the page's own 600 ms), the
    /// 300 ms rise floor, the playback speed.</summary>
    public static JObject GentleFrame(bool on, double fadeSeconds, double speed) => new()
    {
        ["on"] = on,
        ["fadeMs"] = on ? (int)Math.Round(GentleVisuals.ClampFade(fadeSeconds) * 1000) : 600,
        ["riseMs"] = GentleVisuals.RiseFloorMs,
        ["speed"] = GentleVisuals.ClampSpeed(speed),
    };

    /// <summary>
    /// Sign one page call for the desktop door of the race lobby: the server reads
    /// <c>unified_id</c> from the body (POST) or the query (GET) beside the X-Auth-Token header. The
    /// host decides who is calling, never the page: a page that names its own <c>unified_id</c> is
    /// refused, and so is the room-signed <c>/v2/race/result</c> route and an oversized body.
    /// Returns false when the call must not leave; the signed path is then judged by
    /// <see cref="HostNetPathRule.TryBuild"/> as usual.
    /// </summary>
    public static bool TrySign(string method, string? path, string? body, string? unifiedId,
        out string signedPath, out string signedBody)
    {
        signedPath = path ?? "";
        signedBody = body ?? "";
        if (string.IsNullOrEmpty(path) || !path.StartsWith(AllowedPathPrefix, StringComparison.Ordinal)) return false;
        var route = path.Split('?')[0];
        if (route == AllowedPathPrefix + "result" || route.StartsWith(AllowedPathPrefix + "result/", StringComparison.Ordinal)) return false;
        if (path.Contains("unified_id", StringComparison.OrdinalIgnoreCase)) return false;
        if (signedBody.Length > MaxBodyBytes) return false;
        if (string.IsNullOrWhiteSpace(unifiedId)) return true;   // signed out: sent bare, the lobby answers auth

        if (method == "POST")
        {
            JObject o;
            try { o = string.IsNullOrWhiteSpace(signedBody) ? new JObject() : JObject.Parse(signedBody); }
            catch { return false; }
            if (o.Property("unified_id", StringComparison.OrdinalIgnoreCase) != null) return false;
            o["unified_id"] = unifiedId;
            signedBody = o.ToString(Newtonsoft.Json.Formatting.None);
            return true;
        }
        signedPath = path + (path.Contains('?') ? "&" : "?") + "unified_id=" + Uri.EscapeDataString(unifiedId);
        return true;
    }

    // ============================ Sparkles by place (B1 W4) ============================

    /// <summary>The server's game id for Racing Thoughts 3D.</summary>
    public const string SparklesGame = "rt3d";

    /// <summary>
    /// The Sparkles claim a page <c>race-result</c> frame earns, or null when it earns nothing
    /// (unfinished, flagged, malformed, a short Time Trial, an online result with no room).
    /// Page modes: <c>race</c> = solo vs bots (place, the server pays laps/3), <c>trial</c> =
    /// medal, full length only (glass counts as gold), <c>ghost</c> = a duel against a friend's
    /// ghost, full length only (no page sends it yet), <c>online</c> = place plus <c>proof.roomId</c>;
    /// the server reads the room's signed result and ignores the client place. Pure: the host
    /// only fires what this returns.
    /// </summary>
    public static GlassroadSparklesClaim? SparklesClaimFor(JObject? m)
    {
        if (m == null || (string?)m["type"] != "race-result") return null;
        if (m["finished"]?.Type != JTokenType.Boolean || !(bool)m["finished"]!) return null;
        if (m["flagged"]?.Type == JTokenType.Boolean && (bool)m["flagged"]!) return null;
        int laps = ReadInt(m, "laps"), fullLaps = ReadInt(m, "fullLaps"), place = ReadInt(m, "place"), of = ReadInt(m, "of");
        double timeS = m["timeS"]?.Type is JTokenType.Float or JTokenType.Integer ? (double)m["timeS"]! : 0;
        if (laps < 1 || !(timeS > 0) || double.IsInfinity(timeS)) return null;
        // Exactly full length: the medal times are the course's full-length times, so a longer
        // Time Trial or ghost race cannot be judged and claims nothing.
        bool fullLength = fullLaps > 0 && laps == fullLaps;
        bool placed = of >= 1 && place >= 1 && place <= of;

        // A solo race set longer than full length pays full length (the server caps laps/3 too).
        var detail = new JObject { ["laps"] = fullLaps > 0 ? Math.Min(laps, fullLaps) : laps };
        if (m["track"]?.Type == JTokenType.String) detail["track"] = (string?)m["track"];
        var duration = TimeSpan.FromSeconds(Math.Min(timeS, TimeSpan.FromDays(1).TotalSeconds));

        switch ((string?)m["mode"] ?? "race")
        {
            case "race":
                return placed ? new("solo", Sparkles.SparklesResult.ForPlace(place, of), duration, detail, null) : null;
            case "trial":
                if (!fullLength) return null;
                var medal = ((string?)m["medal"] ?? "") switch
                {
                    "glass" or "gold" => "gold",
                    "silver" => "silver",
                    "bronze" => "bronze",
                    _ => "none",
                };
                return new("trial", Sparkles.SparklesResult.ForMedal(medal), duration, detail, null);
            case "ghost":
                var outcome = (string?)m["ghost"];
                if (!fullLength || outcome is not ("win" or "loss")) return null;
                return new("ghost", Sparkles.SparklesResult.ForDuel(outcome), duration, detail, null);
            case "online":
                var roomId = m["roomId"]?.Type == JTokenType.String ? (string?)m["roomId"] : null;
                if (!placed || !IsRoomId(roomId)) return null;
                return new("online", Sparkles.SparklesResult.ForPlace(place, of), duration, detail,
                    new JObject { ["roomId"] = roomId });
            default:
                return null;
        }
    }

    private static bool IsRoomId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
        foreach (var c in id)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-')) return false;
        return true;
    }

    private static int ReadInt(JObject m, string key) =>
        m[key] is JValue { Type: JTokenType.Integer or JTokenType.Float } v && v.Value<double>() is var d
            && d >= 0 && d <= 10_000 && d == Math.Floor(d) ? (int)d : 0;
}

/// <summary>What one finished race claims (game <see cref="GlassroadRules.SparklesGame"/>).</summary>
internal sealed record GlassroadSparklesClaim(string Mode, Sparkles.SparklesResult Result, TimeSpan Duration,
    JObject Detail, JObject? Proof);
