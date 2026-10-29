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
        bool gentleOn, double gentleFadeSeconds, double gentleSpeed)
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
            },
            ["identity"] = signedIn
                ? new JObject { ["unifiedId"] = unifiedId, ["tier"] = Math.Clamp(tier, 0, 2),
                    ["displayName"] = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName }
                : JValue.CreateNull(),
        };
    }

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
}
