using System;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Glassroad;

/// <summary>
/// The pure half of the Glassroad host (<see cref="GlassroadHostService"/>): the hidden door, the
/// host init frame and the net relay's signing rule. No I/O, no App statics, so every rule here is
/// tested without a window.
/// </summary>
internal static class GlassroadRules
{
    /// <summary>The env switch that turns the Racing Thoughts doors into Glassroad doors. Preview only:
    /// without it nothing changes for anyone.</summary>
    public const string DoorEnv = "CCP_GLASSROAD";

    /// <summary>The only prefix the relay forwards (the race lobby). Judged on the built Uri by
    /// <see cref="HostNetPathRule"/>.</summary>
    public const string AllowedPathPrefix = HostNetPathRule.RacePrefix;

    /// <summary>The web courier's body cap, kept the same here.</summary>
    public const int MaxBodyBytes = 16 * 1024;

    /// <summary>True when the env value opens the Glassroad door: exactly "1".</summary>
    public static bool DoorOpen(string? envValue) => string.Equals(envValue?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>The live read of the door switch. Never throws.</summary>
    public static bool DoorOpenNow()
    {
        try { return DoorOpen(Environment.GetEnvironmentVariable(DoorEnv)); }
        catch { return false; }
    }

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
            },
            ["identity"] = signedIn
                ? new JObject { ["unifiedId"] = unifiedId, ["tier"] = Math.Clamp(tier, 0, 2),
                    ["displayName"] = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName }
                : JValue.CreateNull(),
        };
    }

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
