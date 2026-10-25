using System;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Descent;

/// <summary>WPF SpiralEmbedView §9 wire payload and origin policy.</summary>
public static class SpiralEmbedState
{
    public static bool Allows(Uri uri) => uri.Scheme == "https" && uri.Host.Equals("app.cclabs.app", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);

    public static JObject Build(DescentBlock? block, bool reducedMotion, PerformanceTier tier)
    {
        var state = new JObject {
            ["type"] = "spiral:state", ["devotion_days"] = block?.DevotionDays ?? 0,
            ["stage"] = block?.Stage?.N ?? 0, ["relapse_multiplier"] = block?.Relapse?.Multiplier ?? 1,
            ["vat_fill_pct"] = block?.Vat?.FillPct ?? 0, ["reduced_motion"] = reducedMotion,
            ["perf_tier"] = tier == PerformanceTier.Performance ? "low" : tier == PerformanceTier.Balanced ? "mid" : "high" };
        if (!string.IsNullOrWhiteSpace(block?.RawJson))
            try
            {
                var raw = JsonConvert.DeserializeObject<JToken>(block.RawJson,
                    new JsonSerializerSettings { DateParseHandling = DateParseHandling.None });
                if (raw != null && raw.Type != JTokenType.Null) state["descent"] = raw;
            }
            catch (JsonException) { }
        return state;
    }
}
