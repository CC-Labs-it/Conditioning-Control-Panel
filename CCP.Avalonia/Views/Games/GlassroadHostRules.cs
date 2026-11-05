// PORTED from ConditioningControlPanel/Services/Glassroad/GlassroadRules.cs (feat/glassroad-a 5121933ee): the pure
// half of the Glassroad host. Kept in the head under its own name so the WPF copy (which sees Core internals) never
// meets a twin when feat/glassroad-a lands on port/7.2. The window glue is GameWindow.Glassroad.cs.
using System;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>The hidden door, the host init frame (protocol v2) and the relay's signing rule. No I/O, no
    /// statics beyond the env read, so every rule is tested without a window.</summary>
    internal static class GlassroadHostRules
    {
        /// <summary>The env switch that turns the Racing Thoughts door into the Glassroad door. Preview only.</summary>
        public const string DoorEnv = "CCP_GLASSROAD";

        /// <summary>The only prefix the relay forwards (the race lobby), judged on the built Uri by
        /// <see cref="HostNetPathRule"/>. WPF reads it as HostNetPathRule.RacePrefix.</summary>
        public const string AllowedPathPrefix = "/v2/race/";

        /// <summary>The web courier's body cap, kept the same here.</summary>
        public const int MaxBodyBytes = 16 * 1024;

        /// <summary>True when the env value opens the door: exactly "1".</summary>
        public static bool DoorOpen(string? envValue) => string.Equals(envValue?.Trim(), "1", StringComparison.Ordinal);

        /// <summary>The host init frame (glassroad CONTRACT.md section 7). <c>caps.net</c> is always true on the
        /// desktop: a signed-out player gets <c>identity: null</c> and the lobby asks them to sign in. <c>songs</c>
        /// stays empty: the private race song never ships in the app.</summary>
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
                    ? new JObject
                    {
                        ["unifiedId"] = unifiedId, ["tier"] = Math.Clamp(tier, 0, 2),
                        ["displayName"] = string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName,
                    }
                    : JValue.CreateNull(),
            };
        }

        /// <summary>The <c>gentle</c> block: the player's fade (off = the page's own 600 ms), the 300 ms rise
        /// floor, the playback speed.</summary>
        public static JObject GentleFrame(bool on, double fadeSeconds, double speed) => new()
        {
            ["on"] = on,
            ["fadeMs"] = on ? (int)Math.Round(GentleVisuals.ClampFade(fadeSeconds) * 1000) : 600,
            ["riseMs"] = GentleVisuals.RiseFloorMs,
            ["speed"] = GentleVisuals.ClampSpeed(speed),
        };

        // ---- watchdog (WPF GlassroadRules.Watch, 7.2.2 75aa68b92, ccp-bugs #1543) ----
        // A course build (Boudoir) holds the page's main thread, so its heartbeat goes silent while it works.
        // WPF also counts a WebView2 "render process unresponsive" report as busy; this head's WebView has no
        // such event, so only the page's own `loading` frame marks it busy here.

        /// <summary>The longest silence a busy page gets. Panic and the title-bar X still close at once.</summary>
        public static readonly TimeSpan BusyLimit = TimeSpan.FromSeconds(90);

        /// <summary>How long a page <c>loading</c> frame counts as "busy" after it lands.</summary>
        public static readonly TimeSpan LoadingWindow = TimeSpan.FromSeconds(120);

        public static bool IsBusy(TimeSpan? sinceLoadingFrame)
            => sinceLoadingFrame is { } s && s >= TimeSpan.Zero && s < LoadingWindow;

        /// <summary>One 5 s tick. Before ready: close after <see cref="PbpHostRules.BootDeadline"/> with no page
        /// message at all (<see cref="BusyLimit"/> while busy). After ready: silent past 20 s gets one ping, then a
        /// close on the next tick, unless the page is busy and silent for less than <see cref="BusyLimit"/>.</summary>
        public static PbpHostRules.WatchStep Watch(bool ready, TimeSpan sinceProgress, TimeSpan sinceHeartbeat, bool pinged, bool busy)
        {
            if (!ready) return sinceProgress < (busy ? BusyLimit : PbpHostRules.BootDeadline) ? PbpHostRules.WatchStep.Idle : PbpHostRules.WatchStep.Close;
            if (sinceHeartbeat.TotalSeconds <= PbpHostRules.HeartbeatSilenceSeconds) return PbpHostRules.WatchStep.Ok;
            if (!pinged) return PbpHostRules.WatchStep.Ping;
            return busy && sinceHeartbeat < BusyLimit ? PbpHostRules.WatchStep.Ok : PbpHostRules.WatchStep.Close;
        }

        /// <summary>
        /// Sign one page call for the desktop door of the race lobby: the server reads <c>unified_id</c> from the
        /// body (POST) or the query (GET) beside the X-Auth-Token header. The host names the caller, never the
        /// page: a page that names its own <c>unified_id</c> is refused, and so is the room-signed
        /// <c>/v2/race/result</c> route and an oversized body. False = the call must not leave; the signed path is
        /// then judged by <see cref="HostNetPathRule.TryBuild"/> as usual.
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
}
