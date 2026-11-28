using System.Globalization;
using System.Text;

namespace ConditioningControlPanel.Emblems;

/// <summary>A 2D affine map: x' = A x + C y + E, y' = B x + D y + F (SVG matrix order).</summary>
internal readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

    public static Affine Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static Affine Scale(double s) => new(s, 0, 0, s, 0, 0);

    /// <summary>SVG rotate(deg): clockwise on screen (y points down).</summary>
    public static Affine Rotate(double deg)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return new(c, s, -s, c, 0, 0);
    }

    /// <summary>SVG rotate(deg cx cy).</summary>
    public static Affine RotateAbout(double deg, double cx, double cy) =>
        Translate(-cx, -cy).Then(Rotate(deg)).Then(Translate(cx, cy));

    /// <summary>SVG transform="translate(x y) rotate(r) scale(s)": scale first, then rotate, then move.</summary>
    public static Affine Group(double x, double y, double r, double s) =>
        Scale(s).Then(Rotate(r)).Then(Translate(x, y));

    /// <summary>This map, then <paramref name="next"/>.</summary>
    public Affine Then(Affine next) => new(
        next.A * A + next.C * B,
        next.B * A + next.D * B,
        next.A * C + next.C * D,
        next.B * C + next.D * D,
        next.A * E + next.C * F + next.E,
        next.B * E + next.D * F + next.F);

    public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

    /// <summary>Uniform scale factor (the builder only makes rotate, translate and uniform scale).</summary>
    public double ScaleFactor => Math.Sqrt(A * A + B * B);
    public double AngleDeg => Math.Atan2(B, A) * 180 / Math.PI;
    public bool Mirrors => A * D - B * C < 0;
}

/// <summary>
/// Tiny SVG path toolkit for the emblem art: shape builders (rect with rounded corners, circle,
/// ellipse, polygon) and a normaliser that parses any path, resolves relative and shorthand
/// commands, applies an affine map and writes absolute M/L/C/Q/A/Z in invariant culture.
/// </summary>
internal static class EmblemPath
{
    // ---------- shape builders (local coordinates, SVG semantics) ----------

    public static string Rect(double x, double y, double w, double h, double rx = 0)
    {
        rx = Math.Min(Math.Max(rx, 0), Math.Min(w, h) / 2);
        var b = new StringBuilder();
        if (rx <= 0)
        {
            b.Append('M').Append(N(x)).Append(',').Append(N(y))
             .Append(" L").Append(N(x + w)).Append(',').Append(N(y))
             .Append(" L").Append(N(x + w)).Append(',').Append(N(y + h))
             .Append(" L").Append(N(x)).Append(',').Append(N(y + h)).Append(" Z");
            return b.ToString();
        }
        string arc = $" A{N(rx)},{N(rx)} 0 0 1 ";
        b.Append('M').Append(N(x + rx)).Append(',').Append(N(y))
         .Append(" L").Append(N(x + w - rx)).Append(',').Append(N(y))
         .Append(arc).Append(N(x + w)).Append(',').Append(N(y + rx))
         .Append(" L").Append(N(x + w)).Append(',').Append(N(y + h - rx))
         .Append(arc).Append(N(x + w - rx)).Append(',').Append(N(y + h))
         .Append(" L").Append(N(x + rx)).Append(',').Append(N(y + h))
         .Append(arc).Append(N(x)).Append(',').Append(N(y + h - rx))
         .Append(" L").Append(N(x)).Append(',').Append(N(y + rx))
         .Append(arc).Append(N(x + rx)).Append(',').Append(N(y)).Append(" Z");
        return b.ToString();
    }

    public static string Circle(double cx, double cy, double r) => Ellipse(cx, cy, r, r);

    public static string Ellipse(double cx, double cy, double rx, double ry) =>
        $"M{N(cx - rx)},{N(cy)} A{N(rx)},{N(ry)} 0 1 0 {N(cx + rx)},{N(cy)} A{N(rx)},{N(ry)} 0 1 0 {N(cx - rx)},{N(cy)} Z";

    public static string Polygon(IReadOnlyList<(double X, double Y)> pts)
    {
        var b = new StringBuilder();
        for (int i = 0; i < pts.Count; i++)
            b.Append(i == 0 ? "M" : " L").Append(N(pts[i].X)).Append(',').Append(N(pts[i].Y));
        return b.Append(" Z").ToString();
    }

    // ---------- normaliser ----------

    /// <summary>Parses <paramref name="d"/>, maps it through <paramref name="m"/>, writes absolute commands.</summary>
    public static string Transform(string d, Affine m)
    {
        var o = new StringBuilder();
        double s = m.ScaleFactor, rot = m.AngleDeg;
        bool mirror = m.Mirrors;
        Walk(d, (cmd, p) =>
        {
            if (o.Length > 0) o.Append(' ');
            switch (cmd)
            {
                case 'Z': o.Append('Z'); break;
                case 'A':
                    {
                        var (x, y) = m.Apply(p[5], p[6]);
                        double sweep = mirror ? 1 - p[4] : p[4];
                        o.Append('A').Append(N(p[0] * s)).Append(',').Append(N(p[1] * s)).Append(' ')
                         .Append(N(Norm360(p[2] + rot))).Append(' ').Append(p[3] != 0 ? '1' : '0').Append(' ')
                         .Append(sweep != 0 ? '1' : '0').Append(' ').Append(N(x)).Append(',').Append(N(y));
                        break;
                    }
                default:
                    o.Append(cmd);
                    for (int i = 0; i < p.Length; i += 2)
                    {
                        var (x, y) = m.Apply(p[i], p[i + 1]);
                        if (i > 0) o.Append(' ');
                        o.Append(N(x)).Append(',').Append(N(y));
                    }
                    break;
            }
        });
        return o.ToString();
    }

    /// <summary>Every absolute point (end points and control points) of a path, for bounds checks.</summary>
    public static List<(double X, double Y)> Points(string d)
    {
        var list = new List<(double, double)>();
        Walk(d, (cmd, p) =>
        {
            if (cmd == 'Z') return;
            if (cmd == 'A') { list.Add((p[5], p[6])); return; }
            for (int i = 0; i < p.Length; i += 2) list.Add((p[i], p[i + 1]));
        });
        return list;
    }

    /// <summary>
    /// Walks a path, calling <paramref name="emit"/> with absolute commands only: M (x,y), L (x,y),
    /// C (6), Q (4), A (rx, ry, rot, large, sweep, x, y) and Z. H/V become L, S/T become C/Q.
    /// Throws FormatException on malformed data.
    /// </summary>
    public static void Walk(string d, Action<char, double[]> emit)
    {
        int i = 0;
        double cx = 0, cy = 0, sx = 0, sy = 0;
        double lcx = 0, lcy = 0; // last control point for S / T reflection
        char prev = ' ';
        char cmd = ' ';
        while (true)
        {
            SkipSep(d, ref i);
            if (i >= d.Length) break;
            char ch = d[i];
            if (char.IsLetter(ch)) { cmd = ch; i++; }
            else if (cmd == ' ') throw new FormatException($"Path data starts with a number at {i}.");
            // else: implicit repeat of the previous command

            bool rel = char.IsLower(cmd);
            char up = char.ToUpperInvariant(cmd);
            double ox = rel ? cx : 0, oy = rel ? cy : 0;
            switch (up)
            {
                case 'Z':
                    emit('Z', Array.Empty<double>());
                    cx = sx; cy = sy;
                    prev = 'Z';
                    // Z takes no arguments; a following number would be malformed.
                    SkipSep(d, ref i);
                    if (i < d.Length && !char.IsLetter(d[i])) throw new FormatException($"Number after Z at {i}.");
                    continue;
                case 'M':
                    {
                        double x = Num(d, ref i) + ox, y = Num(d, ref i) + oy;
                        emit('M', new[] { x, y });
                        cx = sx = x; cy = sy = y;
                        cmd = rel ? 'l' : 'L'; // implicit pairs after M are line-tos
                        prev = 'M';
                        continue;
                    }
                case 'L':
                    {
                        double x = Num(d, ref i) + ox, y = Num(d, ref i) + oy;
                        emit('L', new[] { x, y }); cx = x; cy = y; prev = 'L'; continue;
                    }
                case 'H':
                    {
                        double x = Num(d, ref i) + ox;
                        emit('L', new[] { x, cy }); cx = x; prev = 'L'; continue;
                    }
                case 'V':
                    {
                        double y = Num(d, ref i) + oy;
                        emit('L', new[] { cx, y }); cy = y; prev = 'L'; continue;
                    }
                case 'C':
                    {
                        double x1 = Num(d, ref i) + ox, y1 = Num(d, ref i) + oy;
                        double x2 = Num(d, ref i) + ox, y2 = Num(d, ref i) + oy;
                        double x = Num(d, ref i) + ox, y = Num(d, ref i) + oy;
                        emit('C', new[] { x1, y1, x2, y2, x, y });
                        lcx = x2; lcy = y2; cx = x; cy = y; prev = 'C'; continue;
                    }
                case 'S':
                    {
                        double x1 = prev == 'C' ? 2 * cx - lcx : cx, y1 = prev == 'C' ? 2 * cy - lcy : cy;
                        double x2 = Num(d, ref i) + ox, y2 = Num(d, ref i) + oy;
                        double x = Num(d, ref i) + ox, y = Num(d, ref i) + oy;
                        emit('C', new[] { x1, y1, x2, y2, x, y });
                        lcx = x2; lcy = y2; cx = x; cy = y; prev = 'C'; continue;
                    }
                case 'Q':
                    {
                        double x1 = Num(d, ref i) + ox, y1 = Num(d, ref i) + oy;
                        double x = Num(d, ref i) + ox, y = Num(d, ref i) + oy;
                        emit('Q', new[] { x1, y1, x, y });
                        lcx = x1; lcy = y1; cx = x; cy = y; prev = 'Q'; continue;
                    }
                case 'T':
                    {
                        double x1 = prev == 'Q' ? 2 * cx - lcx : cx, y1 = prev == 'Q' ? 2 * cy - lcy : cy;
                        double x = Num(d, ref i) + ox, y = Num(d, ref i) + oy;
                        emit('Q', new[] { x1, y1, x, y });
                        lcx = x1; lcy = y1; cx = x; cy = y; prev = 'Q'; continue;
                    }
                case 'A':
                    {
                        double rx = Math.Abs(Num(d, ref i)), ry = Math.Abs(Num(d, ref i)), rot = Num(d, ref i);
                        double large = Flag(d, ref i), sweep = Flag(d, ref i);
                        double x = Num(d, ref i) + ox, y = Num(d, ref i) + oy;
                        emit('A', new[] { rx, ry, rot, large, sweep, x, y });
                        cx = x; cy = y; prev = 'A'; continue;
                    }
                default:
                    throw new FormatException($"Unknown path command '{cmd}' at {i}.");
            }
        }
    }

    private static void SkipSep(string d, ref int i)
    {
        while (i < d.Length && (d[i] == ' ' || d[i] == ',' || d[i] == '\t' || d[i] == '\r' || d[i] == '\n')) i++;
    }

    private static double Flag(string d, ref int i)
    {
        SkipSep(d, ref i);
        if (i < d.Length && (d[i] == '0' || d[i] == '1')) return d[i++] - '0';
        throw new FormatException($"Arc flag expected at {i}.");
    }

    private static double Num(string d, ref int i)
    {
        SkipSep(d, ref i);
        int start = i;
        if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
        bool digits = false, dot = false;
        while (i < d.Length)
        {
            char c = d[i];
            if (c >= '0' && c <= '9') { digits = true; i++; }
            else if (c == '.' && !dot) { dot = true; i++; }
            else break;
        }
        if (digits && i < d.Length && (d[i] == 'e' || d[i] == 'E'))
        {
            int save = i++;
            if (i < d.Length && (d[i] == '-' || d[i] == '+')) i++;
            int expStart = i;
            while (i < d.Length && d[i] >= '0' && d[i] <= '9') i++;
            if (i == expStart) i = save;
        }
        if (!digits) throw new FormatException($"Number expected at {start} in \"{d}\".");
        return double.Parse(d.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static double Norm360(double deg)
    {
        deg %= 360;
        if (deg < 0) deg += 360;
        return Math.Abs(deg - 360) < 1e-9 ? 0 : deg;
    }

    /// <summary>A coordinate in invariant culture, three decimals at most, never "-0".</summary>
    public static string N(double v)
    {
        v = Math.Round(v, 3);
        if (v == 0) v = 0; // drops negative zero
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
