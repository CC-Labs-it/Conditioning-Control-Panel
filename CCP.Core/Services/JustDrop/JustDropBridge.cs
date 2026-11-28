using System;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// The pure half of the Just Drop host bridge (handoff-1121/JUSTDROP-WIRING-BRIEF.md, the fixed
    /// contract): who may ask, what a <c>haptic</c> or <c>assets-request</c> says once clamped, and the
    /// <c>ccp-host</c> envelope that goes back. No WPF, no WebView2, so the suite can pin all of it.
    /// </summary>
    internal static class JustDropBridge
    {
        public const string PageSource = "justdrop";
        public const string HostSource = "ccp-host";
        public const int Version = 1;
        public const string SiteOrigin = "https://" + JustDropHandoff.SiteHost;

        public const int MinHapticMs = 50, MaxHapticMs = 10000;
        public const int MinAssets = 1, MaxAssets = 300;

        public enum HapticKind { Pulse, Wave, Stop }

        public readonly record struct HapticRequest(string Item, HapticKind Kind, double Intensity, int DurationMs);

        public readonly record struct AssetsRequest(bool Video, int Max);

        /// <summary>The sender is the live site itself: https, exactly app.cclabs.app, default port, no
        /// user info. Judged on the parsed url, never a string prefix.</summary>
        public static bool IsSiteSource(string? source)
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var u)) return false;
            return u.Scheme == Uri.UriSchemeHttps
                && string.Equals(u.Host, JustDropHandoff.SiteHost, StringComparison.OrdinalIgnoreCase)
                && u.IsDefaultPort
                && u.UserInfo.Length == 0;
        }

        /// <summary>A well-formed <c>{source:'justdrop', v:1}</c> envelope, or false.</summary>
        public static bool IsPageEnvelope(JObject? o)
            => o != null
            && string.Equals((string?)o["source"], PageSource, StringComparison.Ordinal)
            && o["v"] is { Type: JTokenType.Integer } v && (int)v == Version;

        /// <summary><c>haptic</c> payload, clamped; null when anything required is missing or not a
        /// number. A stop needs no intensity or length.</summary>
        public static HapticRequest? ParseHaptic(JObject? payload)
        {
            if (payload == null) return null;
            var item = (payload["item"] as JValue)?.Value as string;
            if (string.IsNullOrWhiteSpace(item) || item.Length > 40) return null;
            HapticKind kind;
            switch ((payload["kind"] as JValue)?.Value as string)
            {
                case "pulse": kind = HapticKind.Pulse; break;
                case "wave": kind = HapticKind.Wave; break;
                case "stop": return new HapticRequest(item, HapticKind.Stop, 0, 0);
                default: return null;
            }
            if (Number(payload["intensity"]) is not { } intensity || Number(payload["durationMs"]) is not { } ms)
                return null;
            return new HapticRequest(item, kind, Math.Clamp(intensity, 0, 1),
                (int)Math.Clamp(ms, MinHapticMs, MaxHapticMs));
        }

        /// <summary><c>assets-request</c> payload: kind image or video, max clamped to 1..300.</summary>
        public static AssetsRequest? ParseAssets(JObject? payload)
        {
            var kind = (payload?["kind"] as JValue)?.Value as string;
            if (kind is not ("image" or "video")) return null;
            var max = Number(payload!["max"]) is { } m ? (int)Math.Clamp(m, MinAssets, MaxAssets) : MaxAssets;
            return new AssetsRequest(kind == "video", max);
        }

        /// <summary>The host envelope the page listens for.</summary>
        public static JObject Envelope(string type, object payload) => new()
        {
            ["source"] = HostSource,
            ["v"] = Version,
            ["type"] = type,
            ["payload"] = JToken.FromObject(payload),
        };

        /// <summary><c>caps</c>: haptics = access open AND a toy connected right now.</summary>
        public static JObject Caps(bool hapticsOpen, bool toyConnected, int images, int videos, string mediaSource)
            => Envelope("caps", new
            {
                haptics = hapticsOpen && toyConnected,
                assets = new { images = Math.Max(0, images), videos = Math.Max(0, videos) },
                mediaSource,
            });

        /// <summary>The app-wide media source as the app names it (<c>local</c>, <c>online</c>,
        /// <c>mixed</c>), with the consent collapse every other surface applies: no remote consent
        /// means <c>local</c>.</summary>
        public static string EffectiveMediaSource(string? mediaSource, bool remoteConsent)
        {
            var s = mediaSource is "online" or "mixed" ? mediaSource : "local";
            return s != "local" && !remoteConsent ? "local" : s;
        }

        private static double? Number(JToken? t)
        {
            if (t is not { Type: JTokenType.Float or JTokenType.Integer }) return null;
            var v = (double)t;
            return double.IsFinite(v) ? v : null;
        }
    }
}
