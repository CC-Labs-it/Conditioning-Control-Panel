using System;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Descent
{
    /// <summary>The Spiral §9 wire contract, shared by both browser heads.</summary>
    public sealed class SpiralEmbedBridge
    {
        public const string EmbedHost = "app.cclabs.app";
        private string? _pending;
        public bool IsReady { get; private set; }

        public static string BuildUrl(string mode) =>
            $"https://{EmbedHost}/embed/spiral?mode={(mode == "map" ? "map" : "mini")}";

        public static bool AllowsNavigation(Uri uri) => uri.IsAbsoluteUri &&
            uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
            string.Equals(uri.Host, EmbedHost, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(uri.UserInfo);

        public static JObject BuildState(DescentBlock? block, bool reducedMotion, PerformanceTier tier)
        {
            var state = new JObject
            {
                ["type"] = "spiral:state",
                ["devotion_days"] = block?.DevotionDays ?? 0,
                ["stage"] = block?.Stage?.N ?? 0,
                ["relapse_multiplier"] = block?.Relapse?.Multiplier ?? 1.0,
                ["vat_fill_pct"] = block?.Vat?.FillPct ?? 0.0,
                ["reduced_motion"] = reducedMotion,
                ["perf_tier"] = tier == PerformanceTier.Performance ? "low" :
                    tier == PerformanceTier.Balanced ? "mid" : "high",
            };
            // Preserve server tokens, including ISO-shaped strings: desktop authors no descent data.
            if (!string.IsNullOrWhiteSpace(block?.RawJson))
            {
                try
                {
                    var raw = JsonConvert.DeserializeObject<JToken>(block.RawJson,
                        new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                    if (raw is not null) state["descent"] = raw;
                }
                catch (JsonException) { /* unusable raw falls back to the v1 contract */ }
            }
            return state;
        }

        /// <summary>Returns a message to send now, or retains only the latest pre-ready state.</summary>
        public string? Post(string json)
        {
            if (IsReady) return json;
            _pending = json;
            return null;
        }

        /// <summary>Only Spiral's own handshake releases state; malformed messages are harmless.</summary>
        public string? Receive(string json)
        {
            try
            {
                var type = JObject.Parse(json)["type"];
                if (type?.Type != JTokenType.String || (string?)type != "spiral:ready") return null;
            }
            catch (JsonException) { return null; }
            IsReady = true;
            var latest = _pending;
            _pending = null;
            return latest;
        }

        public void Reset() { IsReady = false; _pending = null; }
    }
}
