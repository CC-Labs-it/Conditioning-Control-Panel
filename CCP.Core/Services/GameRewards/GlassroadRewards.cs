using System;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.GameRewards;
public static class GlassroadRewards
{
    public const string SparklesGame = "rt3d";
    public static GlassroadSparklesClaim? SparklesClaimFor(JObject? m)
    {
        if (m == null || (string?)m["type"] != "race-result") return null;
        if (m["finished"]?.Type != JTokenType.Boolean || !(bool)m["finished"]!) return null;
        if (m["flagged"]?.Type == JTokenType.Boolean && (bool)m["flagged"]!) return null;
        int laps = ReadInt(m, "laps"), fullLaps = ReadInt(m, "fullLaps"), place = ReadInt(m, "place"), of = ReadInt(m, "of");
        double timeS = m["timeS"]?.Type is JTokenType.Float or JTokenType.Integer ? (double)m["timeS"]! : 0;
        if (laps < 1 || !(timeS > 0) || double.IsInfinity(timeS)) return null;
        bool fullLength = fullLaps > 0 && laps == fullLaps;
        bool placed = of >= 1 && place >= 1 && place <= of;
        var detail = new JObject { ["laps"] = fullLaps > 0 ? Math.Min(laps, fullLaps) : laps };
        if (m["track"]?.Type == JTokenType.String) detail["track"] = (string?)m["track"];
        var duration = TimeSpan.FromSeconds(Math.Min(timeS, TimeSpan.FromDays(1).TotalSeconds));
        switch ((string?)m["mode"] ?? "race")
        {
            case "race":
                return placed ? new("solo", SparklesResult.ForPlace(place, of), duration, detail, null) : null;
            case "trial":
                if (!fullLength) return null;
                var medal = ((string?)m["medal"] ?? "") switch
                {
                    "glass" or "gold" => "gold",
                    "silver" => "silver",
                    "bronze" => "bronze",
                    _ => "none",
                };
                return new("trial", SparklesResult.ForMedal(medal), duration, detail, null);
            case "ghost":
                var outcome = (string?)m["ghost"];
                if (!fullLength || outcome is not ("win" or "loss")) return null;
                return new("ghost", SparklesResult.ForDuel(outcome), duration, detail, null);
            case "online":
                var roomId = m["roomId"]?.Type == JTokenType.String ? (string?)m["roomId"] : null;
                if (!placed || !IsRoomId(roomId)) return null;
                return new("online", SparklesResult.ForPlace(place, of), duration, detail,
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
public sealed record GlassroadSparklesClaim(string Mode, SparklesResult Result, TimeSpan Duration,
    JObject Detail, JObject? Proof);
