using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion;

internal static class RainPackEmotesFold
{
    public static string Resolve(string clip, IReadOnlySet<string> have)
    {
        if (have.Contains(clip)) return clip;
        string? near = clip.StartsWith("talk", StringComparison.Ordinal) || clip.StartsWith("shy", StringComparison.Ordinal)
                       || clip is "listening" or "smirk" ? "idle"
                     : clip is "giggle" or "surprise" ? "greeting"
                     : clip == "joy" ? "jump"
                     : null;
        if (near != null && have.Contains(near)) return near;
        if (have.Contains("idle")) return "idle";
        return have.OrderBy(c => c, StringComparer.Ordinal).First();
    }

    public static JObject FoldEmotes(JObject template, IReadOnlySet<string> have)
    {
        var j = (JObject)template.DeepClone();
        string R(string c) => Resolve(c, have);

        if (j["idleRotation"] is JArray idle)
        {
            var merged = new List<(string clip, int weight)>();
            foreach (var it in idle.OfType<JObject>())
            {
                var c = R((string?)it["clip"] ?? "idle");
                var w = (int?)it["weight"] ?? 1;
                var at = merged.FindIndex(m => m.clip == c);
                if (at >= 0) merged[at] = (c, merged[at].weight + w); else merged.Add((c, w));
            }
            j["idleRotation"] = new JArray(merged.Select(m => new JObject { ["clip"] = m.clip, ["weight"] = m.weight }));
        }
        if (j["talking"] is JObject talking) FoldList(talking, "pool", R);
        if (j["expressive"] is JObject expressive) FoldList(expressive, "pool", R);
        FoldList(j, "clickEmotes", R);
        foreach (var map in new[] { "stemPrefix", "mood" })
            if (j[map] is JObject o)
                foreach (var p in o.Properties().Where(p => p.Value.Type == JTokenType.String))
                    p.Value = R((string)p.Value!);
        if (j["clipScale"] is JObject scale)
            foreach (var p in scale.Properties().Where(p => !p.Name.StartsWith("_") && !have.Contains(p.Name)).ToList())
                p.Remove();
        return j;
    }

    public static JObject FoldTiming(JObject template, IReadOnlySet<string> have)
    {
        var j = (JObject)template.DeepClone();
        foreach (var p in j.Properties().Where(p => !p.Name.StartsWith("_") && !have.Contains(p.Name)).ToList())
            p.Remove();
        return j;
    }

    private static void FoldList(JObject owner, string key, Func<string, string> resolve)
    {
        if (owner[key] is not JArray arr) return;
        owner[key] = new JArray(arr.Where(t => t.Type == JTokenType.String).Select(t => resolve((string)t!)).Distinct());
    }
}
