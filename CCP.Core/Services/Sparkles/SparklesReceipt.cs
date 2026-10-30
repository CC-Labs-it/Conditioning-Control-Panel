using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Sparkles.Claims;
public sealed record SparklesDaySide(int Paid, int Ceil, int Fees, int Jar);
public sealed record SparklesDay(SparklesDaySide Solo, SparklesDaySide Online, IReadOnlyDictionary<string, int> Games);
public sealed record SparklesReceipt
{
    public string? Id { get; init; }
    public string? ClaimId { get; init; }
    public string? Kind { get; init; }
    public string? Game { get; init; }
    public string? Mode { get; init; }
    public JObject? Result { get; init; }
    public int Place { get; init; }
    public int Of { get; init; }
    public int Base { get; init; }
    public double Online { get; init; } = 1;
    public double Taper { get; init; } = 1;
    public double LapScale { get; init; } = 1;
    public bool Fee { get; init; }
    public int Paid { get; init; }
    public int Credited { get; init; }
    public string? Reason { get; init; }
    public int? Sp { get; init; }
    public bool Shadow { get; init; }
    public long At { get; init; }
    public SparklesDay? Day { get; init; }
    public long ResetInMs { get; init; }
    public static SparklesReceipt? Parse(JToken? token)
    {
        if (token is not JObject o) return null;
        if (o["receipt"] is JObject inner) o = inner;
        var result = o["result"] as JObject;
        var placeSrc = result ?? o;
        return new SparklesReceipt
        {
            Id = Str(o["id"]),
            ClaimId = Str(o["claimId"]),
            Kind = Str(o["kind"]) ?? Str(result?["kind"]),
            Game = Str(o["game"]),
            Mode = Str(o["mode"]),
            Result = result,
            Place = Int(placeSrc["place"]) ?? Int(o["place"]) ?? 0,
            Of = Int(placeSrc["of"]) ?? Int(o["of"]) ?? 0,
            Base = Int(o["base"]) ?? 0,
            Online = Num(o["online"]) ?? 1,
            Taper = Num(o["taper"]) ?? 1,
            LapScale = Num(o["lapScale"]) ?? 1,
            Fee = o["fee"]?.Type == JTokenType.Boolean && o.Value<bool>("fee"),
            Paid = Int(o["paid"]) ?? 0,
            Credited = Math.Max(0, Int(o["credited"]) ?? 0),
            Reason = Str(o["reason"]),
            Sp = Int(o["sp"]),
            Shadow = o["shadow"]?.Type == JTokenType.Boolean && o.Value<bool>("shadow"),
            At = Long(o["at"]) ?? 0,
            Day = ParseDay(o["day"] as JObject),
            ResetInMs = Long(o["resetInMs"]) ?? 0,
        };
    }
    private static SparklesDay? ParseDay(JObject? d)
    {
        if (d == null) return null;
        var games = new Dictionary<string, int>(StringComparer.Ordinal);
        if (d["games"] is JObject g)
            foreach (var p in g.Properties())
                if (Int(p.Value) is int n) games[p.Name] = n;
        return new SparklesDay(Side(d["solo"] as JObject), Side(d["online"] as JObject), games);
    }
    private static SparklesDaySide Side(JObject? s) => s == null
        ? new SparklesDaySide(0, 0, 0, 0)
        : new SparklesDaySide(Int(s["paid"]) ?? 0, Int(s["ceil"]) ?? 0, Int(s["fees"]) ?? 0, Int(s["jar"]) ?? 0);
    private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;
    private static double? Num(JToken? t) =>
        t is JValue { Type: JTokenType.Integer or JTokenType.Float } v ? v.Value<double>() : null;
    private static int? Int(JToken? t) =>
        Num(t) is double d && !double.IsNaN(d) && d is > int.MinValue and < int.MaxValue ? (int)Math.Round(d) : null;
    private static long? Long(JToken? t) =>
        Num(t) is double d && !double.IsNaN(d) && d is > long.MinValue and < long.MaxValue ? (long)Math.Round(d) : null;
}
