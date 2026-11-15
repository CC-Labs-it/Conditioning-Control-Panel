using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ConditioningControlPanel.SuperCore;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Super;

/// <summary>
/// Reads a saved Loom spiral's params sidecar (schema v2, or the flat v1) into a
/// <see cref="LoomSpec"/>, clamped the way the Loom's own normalizer clamps it.
/// </summary>
public static class LoomSpecJson
{
    private static readonly string[] Styles = { "log", "arch", "ribbon", "golden", "tunnel", "petal" };

    public static LoomSpec? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var o = JObject.Parse(json);
            var q = new LoomSpec();
            if (o["layer"] is JObject l1)
            {
                Layer(l1, q.Layer);
                if (o["layer2"] is JObject l2) { Layer(l2, q.Layer2); q.Layer2On = l2.Value<bool?>("enabled") == true; }
                q.Glow = Math.Clamp(Num(o["glow"], 0), 0, 1);
                q.PulseAmp = Math.Clamp(Num(o["pulse"]?["amp"], 0), 0, 0.25);
                q.PulseCycles = Math.Clamp(Math.Round(Num(o["pulse"]?["cycles"], 1)), 1, 4);
                q.WobAmp = Math.Clamp(Num(o["wobble"]?["amp"], 0), 0, 0.35);
                q.WobFreq = Math.Clamp(Math.Round(Num(o["wobble"]?["freq"], 2)), 1, 6);
                q.WobCycles = Math.Clamp(Math.Round(Num(o["wobble"]?["cycles"], 1)), 1, 3);
            }
            else Layer(o, q.Layer);     // schema v1: one flat weave
            return q;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("Loom sidecar: {E}", ex.Message);
            return null;
        }
    }

    private static double Num(JToken? t, double fallback)
        => t != null && (t.Type == JTokenType.Float || t.Type == JTokenType.Integer) ? t.Value<double>() : fallback;

    private static void Layer(JObject o, LoomLayer l)
    {
        l.Arms = (int)Math.Clamp(Math.Round(Num(o["arms"], 4)), 1, 12);
        l.Turns = Math.Clamp(Num(o["turns"], 2), 0.5, 6);
        l.Duty = Math.Clamp(Num(o["duty"], 0.5), 0.2, 0.8);
        l.Style = Math.Max(0, Array.IndexOf(Styles, o.Value<string>("style") ?? "log"));
        l.Direction = Num(o["direction"], 1) < 0 ? -1 : 1;
        l.Gradient = o.Value<string>("bandMode") == "gradient";
        l.SpeedMul = (int)Math.Clamp(Math.Round(Num(o["speedMul"], 1)), 1, 3);
        var colors = new List<uint>();
        if (o["colors"] is JArray arr)
            foreach (var c in arr)
            {
                var s = c.Type == JTokenType.String ? c.Value<string>() : null;
                if (s != null && s.Length == 7 && s[0] == '#'
                    && uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) && !colors.Contains(v))
                    colors.Add(v);
                if (colors.Count >= 6) break;
            }
        if (colors.Count > 0) l.Colors = colors.ToArray();
    }
}
