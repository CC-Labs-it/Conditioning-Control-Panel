using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>One frame of a sheet star's path: <see cref="Offset"/> 0..1, its centre in host pixels, scale (x 26 px),
/// turn in degrees and opacity.</summary>
public readonly record struct SheetStarKey(double Offset, double X, double Y, double Scale, double Angle, double Alpha);

/// <summary>One star of a sheet flight: its frames, delay and length (ms from the flight's start).</summary>
public sealed record SheetStarTrack(IReadOnlyList<SheetStarKey> Keys, double DelayMs, double DurMs)
{
    public double EndMs => DelayMs + DurMs;
}

/// <summary>A page flight handed to the host: a spend (<see cref="Out"/>, stars leave the chip) or a gain.</summary>
public sealed record SheetStarFlight(bool Out, int Amount, double X1, double Y1, double X2, double Y2, IReadOnlyList<SheetStarTrack> Stars);

/// <summary>
/// THE LAST LEG (CONTRACT.md "Sheet additions", <c>wallet-stars</c>), as numbers. The sheet's page cannot draw over the
/// host's bar, so it hands the host every star's whole path; the host draws the same path in its own layer (the part
/// over the page is hidden under the page's HWND) and the two read as one motion. Pure: reading the frame (bounded,
/// every number checked), the pose at a moment (Web Animations: one cubic-bezier over the whole star, then linear
/// between frames) and the numbers the chip counts between.
/// </summary>
public static class SheetStarPlan
{
    public const int MaxStars = 48;
    public const int MaxKeys = 8;
    /// <summary>No star may still be flying after this (the director's watchdog is longer).</summary>
    public const double MaxEndMs = 4000;
    /// <summary>A page point more than this far off is junk.</summary>
    private const double MaxCoord = 20000;

    /// <summary>Reads a <c>wallet-stars</c> frame, page pixels moved by (<paramref name="ox"/>, <paramref name="oy"/>)
    /// into the host layer. Null = unreadable or nothing to fly; a bad star is dropped, the rest fly.</summary>
    public static SheetStarFlight? Read(JObject? m, double ox, double oy)
    {
        if (m == null || !double.IsFinite(ox) || !double.IsFinite(oy) || m["stars"] is not JArray arr) return null;
        bool @out = (string?)m["dir"] == "out";
        int amount = m["n"] is JValue { Type: JTokenType.Integer or JTokenType.Float } n ? (int)Math.Clamp(n.Value<double>(), 0, 1_000_000) : 0;
        double[] e = { 0.42, 0, 0.3, 1 };
        if (m["e"] is JArray ea && ea.Count == 4)
        {
            var got = new double[4];
            bool ok = true;
            for (int i = 0; i < 4 && ok; i++) ok = Num(ea[i], out got[i]);
            // A CSS cubic-bezier: both x in 0..1.
            if (ok && got[0] is >= 0 and <= 1 && got[2] is >= 0 and <= 1 && Math.Abs(got[1]) < 4 && Math.Abs(got[3]) < 4) e = got;
        }
        var stars = new List<SheetStarTrack>();
        foreach (var t in arr)
        {
            if (stars.Count >= MaxStars) break;
            if (t is not JObject so || !Num(so["d"], out var d) || !Num(so["ms"], out var ms) || so["k"] is not JArray ka) continue;
            if (d < 0 || ms < 1 || d + ms > MaxEndMs || ka.Count < 2 || ka.Count > MaxKeys) continue;
            var keys = new List<SheetStarKey>(ka.Count);
            double last = -1;
            foreach (var kt in ka)
            {
                if (kt is not JArray k || k.Count < 6) { keys = null; break; }
                if (!Num(k[0], out var o) || !Num(k[1], out var x) || !Num(k[2], out var y) || !Num(k[3], out var s) || !Num(k[4], out var r) || !Num(k[5], out var a)
                    || o < last || o > 1 || Math.Abs(x) > MaxCoord || Math.Abs(y) > MaxCoord || s < 0 || s > 4) { keys = null; break; }
                last = o;
                keys.Add(new SheetStarKey(o, x + ox, y + oy, s, r, Math.Clamp(a, 0, 1)));
            }
            if (keys == null || keys[0].Offset != 0 || keys[^1].Offset != 1) continue;
            stars.Add(new SheetStarTrack(keys, d, ms));
        }
        return stars.Count == 0 ? null : new SheetStarFlight(@out, amount, e[0], e[1], e[2], e[3], stars);
    }

    private static bool Num(JToken? t, out double v)
    {
        v = 0;
        if (t is not JValue { Type: JTokenType.Integer or JTokenType.Float } j) return false;
        v = j.Value<double>();
        return double.IsFinite(v);
    }

    /// <summary>Where a star is <paramref name="ms"/> after its flight started; null before it leaves or after it ended.</summary>
    public static SparkleStarPose? Pose(SheetStarFlight f, SheetStarTrack s, double ms)
    {
        var raw = (ms - s.DelayMs) / s.DurMs;
        if (raw < 0 || raw > 1) return null;
        var p = SparkleFlightPlan.Ease(raw, f.X1, f.Y1, f.X2, f.Y2);
        var keys = s.Keys;
        // Overshoot from the easing past either end holds on the end frame's segment (extrapolated, as CSS does).
        int i = 0;
        while (i < keys.Count - 2 && p > keys[i + 1].Offset) i++;
        var a = keys[i]; var b = keys[i + 1];
        var span = b.Offset - a.Offset;
        var t = span <= 0 ? 1 : (p - a.Offset) / span;
        return new SparkleStarPose(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, Math.Max(0, a.Scale + (b.Scale - a.Scale) * t),
            a.Angle + (b.Angle - a.Angle) * t, Math.Clamp(a.Alpha + (b.Alpha - a.Alpha) * t, 0, 1));
    }

    /// <summary>The numbers the chip counts between when no ledger change names them: <paramref name="truth"/> is what
    /// the chip reads on release. In the DEBUG preview the pretend wallet has already moved (<paramref name="truthIsAfter"/>);
    /// on a real account the ledger change may still be on its way, so the count starts at the truth.</summary>
    public static (int From, int To) Counts(bool @out, int amount, int truth, bool truthIsAfter)
    {
        amount = Math.Max(0, amount);
        truth = Math.Max(0, truth);
        int delta = @out ? -amount : amount;
        return truthIsAfter ? (Math.Max(0, truth - delta), truth) : (truth, Math.Max(0, truth + delta));
    }
}
