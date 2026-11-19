namespace ConditioningControlPanel.FrameCore;

/// <summary>An ellipse (centre, radii, rotation in degrees) or a path (x,y pairs).</summary>
public enum FramePrimKind { Ellipse, Path }

/// <summary>
/// One drawing step of a profile frame, in canvas units (the frame canvas is 0..1 square, the avatar circle sits
/// centred with radius <see cref="ProfileFrames.Window"/>). Heads only draw these: WPF in
/// Services/Profile/ProfileFrameArt.cs, the shop page in prizecounter sheet/frames.js (a line-for-line port, pinned
/// by tests/fixtures/frame-probes.json from both sides).
/// </summary>
public sealed class FramePrim
{
    public FramePrimKind Kind;
    public double X, Y, Rx, Ry, Rot;
    /// <summary>Path points as x0,y0,x1,y1...</summary>
    public double[]? Pts;
    public bool Closed, Smooth;
    /// <summary>A round hole at the canvas centre (even-odd), so a filled card never paints over the avatar. 0 = none.</summary>
    public double Hole;
    /// <summary>Fill colour (#RRGGBB) or null. With <see cref="Fill2"/> a vertical gradient from y=G0 (Fill) to y=G1 (Fill2).</summary>
    public string? Fill, Fill2;
    public double G0, G1;
    public string? Stroke;
    public double Width;
    public double Alpha = 1;
    /// <summary>True when it changes with time. False steps are the same at every t, so a head may cache them.</summary>
    public bool Live;
}

/// <summary>How a frame moves: speed and amplitude multipliers, and the Gentle Visuals switch.</summary>
public readonly record struct FrameMotion(double Speed, double Amp, bool Gentle)
{
    public static FrameMotion Full => new(1, 1, false);
    public static FrameMotion Still => new(0, 0, false);

    /// <summary>Motion level 0 = full, 1 = reduced (half speed, half amplitude), 2 = off (the rest pose). Gentle caps
    /// the speed at half and softens the marquee bulbs; nothing in any frame blinks at any level.</summary>
    public static FrameMotion Of(int level, bool gentle)
    {
        var s = level >= 2 ? 0 : level == 1 ? 0.5 : 1;
        var a = s;
        if (gentle) s = Math.Min(s, 0.5);
        return new FrameMotion(s, a, gentle);
    }

    public bool Moves => Speed > 0 && Amp > 0;
}

/// <summary>
/// The Prize Counter profile frames (owner 2026-11-17: "make the profile frames actually decent and animated"). Each
/// is its own shape and material in the shop's cartoon hand: thick ink outline, glossy highlight, bold colour.
/// Pure rules: <see cref="Build"/> returns the drawing steps for one moment; heads draw them.
/// </summary>
public static class ProfileFrames
{
    public const string Lace = "profile.frame.lace", InkStamp = "profile.frame.ink-stamp", Gilt = "profile.frame.gilt",
        Polaroid = "profile.frame.polaroid", RoomHost = "prime.frame.room-host";

    public static readonly IReadOnlyList<string> Ids = new[] { Lace, InkStamp, Gilt, Polaroid, RoomHost };

    /// <summary>Canvas centre and the avatar circle's radius (ShopCosmetics.FrameWindowRatio / 2).</summary>
    public const double C = 0.5, Window = 0.39;

    /// <summary>Nothing of a frame comes nearer the centre than this: it may lie over the avatar's own ring, never the picture.</summary>
    public const double Keep = 0.365;

    public const string Ink = "#2B1136";
    public const double K = 0.018;

    public static bool Knows(string? id) => id is Lace or InkStamp or Gilt or Polaroid or RoomHost;

    /// <summary>Detail by drawn canvas size in pixels: 0 small (list rows), 1 medium (profile card), 2 large (shop stage).</summary>
    public static int DetailFor(double canvasPx) => canvasPx <= 48 ? 0 : canvasPx < 150 ? 1 : 2;

    /// <summary>The frame at time <paramref name="t"/> seconds. Unknown ids give an empty list. The number of steps and
    /// their Live flags never depend on t, so a head can cache the still steps and redraw only the live ones.</summary>
    public static List<FramePrim> Build(string? id, double t, FrameMotion m, int detail)
    {
        var b = new B(t * m.Speed, m.Amp, m.Gentle, Math.Clamp(detail, 0, 2));
        switch (id)
        {
            case Lace: b.LaceFrame(); break;
            case InkStamp: b.StampFrame(); break;
            case Gilt: b.GiltFrame(); break;
            case Polaroid: b.PolaroidFrame(); break;
            case RoomHost: b.HostFrame(); break;
        }
        return b.Out;
    }

    private sealed class B
    {
        private const double Tau = Math.PI * 2;
        public readonly List<FramePrim> Out = new();
        private readonly double tt, amp;
        private readonly bool gentle;
        private readonly int d;

        public B(double tt, double amp, bool gentle, int d) { this.tt = tt; this.amp = amp; this.gentle = gentle; this.d = d; }

        // ---- steps ----
        private void E(double x, double y, double rx, double ry, double rot, string? fill, string? stroke, double w, double alpha, bool live)
            => Out.Add(new FramePrim { Kind = FramePrimKind.Ellipse, X = x, Y = y, Rx = rx, Ry = ry, Rot = rot, Fill = fill, Stroke = stroke, Width = w, Alpha = alpha, Live = live });

        private FramePrim P(double[] pts, bool closed, bool smooth, string? fill, string? stroke, double w, double alpha, bool live)
        {
            var p = new FramePrim { Kind = FramePrimKind.Path, Pts = pts, Closed = closed, Smooth = smooth, Fill = fill, Stroke = stroke, Width = w, Alpha = alpha, Live = live };
            Out.Add(p);
            return p;
        }

        private void Ring(double r, double width, string colour, bool live)
        {
            E(C, C, r, r, 0, null, Ink, width + 2 * K, 1, live);
            E(C, C, r, r, 0, null, colour, width, 1, live);
        }

        private static double Px(double r, double a) => C + r * Math.Cos(a);
        private static double Py(double r, double a) => C + r * Math.Sin(a);

        /// <summary>Local points (x,y pairs) turned by <paramref name="rot"/> radians and scaled, then moved to (ox, oy).</summary>
        private static double[] Place(double[] local, double ox, double oy, double rot, double s)
        {
            var o = new double[local.Length];
            double c = Math.Cos(rot), si = Math.Sin(rot);
            for (var i = 0; i < local.Length; i += 2)
            {
                double x = local[i] * s, y = local[i + 1] * s;
                o[i] = ox + x * c - y * si;
                o[i + 1] = oy + x * si + y * c;
            }
            return o;
        }

        /// <summary>A four-point sparkle (eight corners).</summary>
        private void Sparkle(double x, double y, double s, double alpha, bool live)
        {
            var pts = new double[16];
            for (var i = 0; i < 8; i++)
            {
                var a = i * Math.PI / 4 - Math.PI / 2;
                var r = i % 2 == 0 ? s : s * 0.3;
                pts[2 * i] = x + r * Math.Cos(a);
                pts[2 * i + 1] = y + r * Math.Sin(a);
            }
            P(pts, true, false, "#FFFBE6", null, 0, alpha, live);
        }

        /// <summary>A five-point star.</summary>
        private double[] Star(double x, double y, double r, double rot)
        {
            var pts = new double[20];
            for (var i = 0; i < 10; i++)
            {
                var a = rot - Math.PI / 2 + i * Math.PI / 5;
                var rr = i % 2 == 0 ? r : r * 0.48;
                pts[2 * i] = x + rr * Math.Cos(a);
                pts[2 * i + 1] = y + rr * Math.Sin(a);
            }
            return pts;
        }

        private static double[] Heart(double x, double y, double s)
        {
            double[] local = { 0, 0.9, -0.55, 0.35, -0.95, -0.1, -0.8, -0.65, -0.35, -0.8, 0, -0.45, 0.35, -0.8, 0.8, -0.65, 0.95, -0.1, 0.55, 0.35 };
            return Place(local, x, y, 0, s);
        }

        /// <summary>A rounded rectangle as points (corner arcs of <paramref name="seg"/> steps).</summary>
        private static double[] RoundRect(double x0, double y0, double x1, double y1, double r, int seg)
        {
            var pts = new List<double>();
            void Arc(double cx, double cy, double a0)
            {
                for (var i = 0; i <= seg; i++)
                {
                    var a = a0 + i * (Math.PI / 2) / seg;
                    pts.Add(cx + r * Math.Cos(a));
                    pts.Add(cy + r * Math.Sin(a));
                }
            }
            Arc(x1 - r, y0 + r, -Math.PI / 2);
            Arc(x1 - r, y1 - r, 0);
            Arc(x0 + r, y1 - r, Math.PI / 2);
            Arc(x0 + r, y0 + r, Math.PI);
            return pts.ToArray();
        }

        private static double Smooth01(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

        // ================= Lace: a pink doily whose scallops ripple round, with a satin bow that sways =================
        public void LaceFrame()
        {
            const string pink = "#FF7EB9", lace = "#FFF7FB", eyelet = "#FF9FCB", ribbon = "#FF3D8B", ribbon2 = "#D81E6C";
            int n = d == 0 ? 12 : d == 1 ? 22 : 26;
            double rr = d == 0 ? 0.432 : 0.44, rs = d == 0 ? 0.048 : d == 1 ? 0.044 : 0.04;
            // scallops: a wave of size runs round the doily
            for (var i = 0; i < n; i++)
            {
                var a = i * Tau / n - Math.PI / 2;
                var w = Math.Sin(Tau * (tt / 3.2) - i * Tau * 2 / n);
                var r = rs * (1 + 0.12 * amp * w);
                E(Px(rr + 0.004 * amp * w, a), Py(rr + 0.004 * amp * w, a), r, r, 0, lace, Ink, K * 0.8, 1, true);
                if (d > 0) E(Px(rr + 0.012, a), Py(rr + 0.012, a), r * 0.3, r * 0.3, 0, eyelet, null, 0, 1, true);
            }
            Ring(0.414, 0.05, pink, false);
            if (d > 0)
            {
                // a white ribbon threaded through the band
                int m = n / 2;
                for (var i = 0; i < m; i++)
                {
                    var a = (i + 0.5) * Tau / m - Math.PI / 2;
                    E(Px(0.414, a), Py(0.414, a), 0.024, 0.009, a * 180 / Math.PI + 90, lace, null, 0, 1, false);
                }
            }
            E(Px(0.414, -1.95), Py(0.414, -1.95), 0.05, 0.012, -1.95 * 180 / Math.PI + 90, "#FFFFFF", null, 0, 0.75, false);   // gloss on the band, top left
            // the bow sits on the lower right and sways from its knot
            var sway = amp * 0.16 * Math.Sin(Tau * tt / 2.6);
            double bx = Px(0.47, Math.PI / 4), by = Py(0.47, Math.PI / 4), rot = -Math.PI / 4 + sway, s = d == 0 ? 1.15 : d == 1 ? 1.2 : 1.1;
            var flutter = amp * 0.012 * Math.Sin(Tau * tt / 1.3);
            if (d > 0)
            {
                P(Place(new[] { -0.01, 0.01, -0.035, 0.06, -0.05, 0.105 + flutter, -0.03, 0.095, -0.012, 0.11 - flutter, 0.0, 0.03 }, bx, by, rot, s), true, false, ribbon2, Ink, K * 0.8, 1, true);
                P(Place(new[] { 0.01, 0.01, 0.035, 0.06, 0.05, 0.105 - flutter, 0.03, 0.095, 0.012, 0.11 + flutter, 0.0, 0.03 }, bx, by, rot, s), true, false, ribbon2, Ink, K * 0.8, 1, true);
            }
            P(Place(new[] { 0, 0, -0.06, -0.055, -0.125, -0.035, -0.125, 0.035, -0.065, 0.045 }, bx, by, rot, s), true, true, ribbon, Ink, K, 1, true);
            P(Place(new[] { 0, 0, 0.06, -0.055, 0.125, -0.035, 0.125, 0.035, 0.065, 0.045 }, bx, by, rot, s), true, true, ribbon, Ink, K, 1, true);
            var g = Place(new[] { -0.085, -0.025, 0.085, -0.025 }, bx, by, rot, s);
            E(g[0], g[1], 0.024 * s, 0.009 * s, rot * 180 / Math.PI - 20, "#FFFFFF", null, 0, 0.8, true);
            E(g[2], g[3], 0.024 * s, 0.009 * s, rot * 180 / Math.PI + 20, "#FFFFFF", null, 0, 0.8, true);
            E(bx, by, 0.028 * s, 0.03 * s, rot * 180 / Math.PI, ribbon2, Ink, K, 1, true);
        }

        // ================= Ink stamp: a perforated postage stamp, mint field, a turning postmark, a heart that thumps =================
        public void StampFrame()
        {
            const string paper = "#FFF3D6", paper2 = "#F6DFAE", field = "#7FE7C9", field2 = "#3FBF9C", postInk = "#3B1A5C", red = "#FF4F7B";
            int n = d == 0 ? 6 : d == 1 ? 9 : 11;
            double x0 = 0.035, x1 = 0.965, L = x1 - x0, rb = L / n * 0.3;
            var pts = new List<double>();
            void Side(double sx, double sy, double dx, double dy, double nx, double ny)
            {
                pts.Add(sx); pts.Add(sy);
                for (var k = 0; k < n; k++)
                {
                    double cx = sx + dx * (k + 0.5) * L / n, cy = sy + dy * (k + 0.5) * L / n;
                    for (var u = 0; u <= 6; u++)
                    {
                        var a = u * Math.PI / 6;
                        pts.Add(cx - rb * Math.Cos(a) * dx + rb * Math.Sin(a) * nx);
                        pts.Add(cy - rb * Math.Cos(a) * dy + rb * Math.Sin(a) * ny);
                    }
                }
            }
            Side(x0, x0, 1, 0, 0, 1);
            Side(x1, x0, 0, 1, -1, 0);
            Side(x1, x1, -1, 0, 0, -1);
            Side(x0, x1, 0, -1, 1, 0);
            var p = P(pts.ToArray(), true, false, paper, Ink, K, 1, false);
            p.Hole = Window; p.Fill2 = paper2; p.G0 = 0; p.G1 = 1;
            var f = P(RoundRect(0.095, 0.095, 0.905, 0.905, 0.04, d == 0 ? 2 : 5), true, false, field, Ink, K * 0.8, 1, false);
            f.Hole = Window; f.Fill2 = field2; f.G0 = 0.1; f.G1 = 0.9;
            if (d > 0)
            {
                // little corner stars printed on the field
                P(Star(0.165, 0.165, 0.035, 0), true, false, "#FFFFFF", Ink, K * 0.6, 1, false);
                E(0.2, 0.86, 0.045, 0.012, -45, "#FFFFFF", null, 0, 0.55, false);
            }
            // the heart stamp, bottom left: a slow thump every 2.8 s
            var beat = Math.Pow(Math.Max(0, Math.Sin(Tau * tt / 2.8)), 6);
            var hs = (d == 0 ? 0.075 : 0.055) * (1 + 0.16 * amp * beat);
            P(Heart(0.16, 0.835, hs), true, true, red, Ink, K * 0.8, 1, true);
            E(0.14, 0.81, hs * 0.28, hs * 0.16, -35, "#FFFFFF", null, 0, 0.8, true);
            // the postmark over the top right corner, its dashed ring turning
            double pcx = 0.845, pcy = 0.155, pr = d == 0 ? 0.1 : 0.105;
            E(pcx, pcy, pr * 0.62, pr * 0.62, 0, null, postInk, K * 0.7, 0.85, false);
            int dashes = d == 0 ? 8 : 14;
            var turn = amp > 0 ? Tau * tt / 16 : 0;
            for (var i = 0; i < dashes; i++)
            {
                var a = i * Tau / dashes + turn;
                E(pcx + pr * 0.86 * Math.Cos(a), pcy + pr * 0.86 * Math.Sin(a), pr * 0.16, pr * 0.07, a * 180 / Math.PI + 90, postInk, null, 0, 0.85, true);
            }
            if (d > 0)
            {
                // three wavy cancel lines through the mark, drifting
                var drift = amp * 0.02 * Math.Sin(Tau * tt / 5);
                for (var j = -1; j <= 1; j++)
                {
                    var line = new double[14];
                    for (var u = 0; u < 7; u++)
                    {
                        var x = pcx - pr * 0.45 + u * pr * 0.15;
                        line[2 * u] = x;
                        line[2 * u + 1] = pcy + j * pr * 0.28 + 0.008 * Math.Sin(u * 1.6 + Tau * tt / 2.2 * (amp > 0 ? 1 : 0)) + drift * 0.2;
                    }
                    P(line, false, true, null, postInk, K * 0.45, 0.85, true);
                }
            }
        }

        // ================= Gilt: a gold laurel wreath, a shimmer sweeping up the leaves, a twinkle, a ruby and a star =================
        public void GiltFrame()
        {
            const string gold = "#FFD75A", gold2 = "#E0931A", vein = "#A8600E", ruby = "#FF3D6E", ruby2 = "#B0123F";
            int n = d == 0 ? 5 : d == 1 ? 9 : 11;
            double gap = 0.42, len = d == 0 ? 0.12 : d == 1 ? 0.1 : 0.094, wid = d == 0 ? 0.06 : d == 1 ? 0.05 : 0.046;
            E(C, C, 0.418, 0.418, 0, null, Ink, 0.022 + 2 * K * 0.8, 1, false);
            E(C, C, 0.418, 0.418, 0, null, gold2, 0.022, 1, false);
            var tips = new List<double>();
            var mids = new List<double>();
            for (var side = -1; side <= 1; side += 2)
            {
                for (var k = 0; k < n; k++)
                {
                    var u = (k + 0.5) / n;
                    var a = Math.PI / 2 - side * u * (Math.PI - gap / 2);   // from the bottom up each side
                    var r = 0.448 + (k % 2 == 0 ? 0.004 : -0.004);
                    double cx = Px(r, a), cy = Py(r, a);
                    // along the stem toward the top, leaning out then in by turns
                    var dir = Math.Atan2(-Math.Cos(a) * side, Math.Sin(a) * side) + (k % 2 == 0 ? 0.42 : -0.18) * side;
                    var leaf = new double[28];
                    for (var i = 0; i < 14; i++)
                    {
                        double q = i < 7 ? -1 + i / 3.0 : 1 - (i - 7) / 3.0;
                        double half = wid / 2 * Math.Pow(Math.Max(0, 1 - q * q), 0.7) * (i < 7 ? 1 : -1);
                        double lx = q * len / 2, ly = half;
                        leaf[2 * i] = cx + lx * Math.Cos(dir) - ly * Math.Sin(dir);
                        leaf[2 * i + 1] = cy + lx * Math.Sin(dir) + ly * Math.Cos(dir);
                    }
                    var lp = P(leaf, true, false, gold, Ink, K * 0.75, 1, false);
                    lp.Fill2 = gold2; lp.G0 = cy - len / 2; lp.G1 = cy + len / 2;
                    if (d > 0) P(new[] { cx - len * 0.38 * Math.Cos(dir), cy - len * 0.38 * Math.Sin(dir), cx + len * 0.3 * Math.Cos(dir), cy + len * 0.3 * Math.Sin(dir) }, false, false, null, vein, 0.006, 0.8, false);
                    tips.Add(cx + len * 0.32 * Math.Cos(dir)); tips.Add(cy + len * 0.32 * Math.Sin(dir));
                    mids.Add(cx); mids.Add(cy); mids.Add(u); mids.Add(dir);
                }
            }
            // the shimmer: a bright band climbs both branches in 2.4 s, then rests (5 s loop). Still: a soft light on the top leaves.
            var ph = tt % 5 / 2.4 * 1.5 - 0.25;
            for (var i = 0; i < mids.Count; i += 4)
            {
                var u = mids[i + 2];
                var lit = amp > 0 ? amp * Math.Exp(-Math.Pow((u - ph) / 0.13, 2)) : 0.35 * u;
                E(mids[i] - 0.004, mids[i + 1] - 0.006, len * 0.26, wid * 0.17, mids[i + 3] * 180 / Math.PI, "#FFFBE0", null, 0, 0.25 + 0.75 * lit, true);
            }
            // the ruby where the stems cross, and the star at the top
            P(new[] { 0.45, 0.88, 0.5, 0.93, 0.55, 0.88, 0.535, 0.975, 0.5, 0.945, 0.465, 0.975 }, true, false, gold2, Ink, K * 0.75, 1, false);
            E(0.5, 0.915, 0.04, 0.036, 0, ruby, Ink, K, 1, false);
            if (d > 0) E(0.5, 0.925, 0.026, 0.018, 0, ruby2, null, 0, 0.6, false);
            E(0.488, 0.902, 0.014, 0.008, -30, "#FFFFFF", null, 0, 0.9, false);
            var spin = amp * 0.25 * Math.Sin(Tau * tt / 6);
            var sp = P(Star(0.5, 0.062, d == 0 ? 0.05 : 0.042, spin), true, false, gold, Ink, K * 0.8, 1, true);
            sp.Fill2 = gold2; sp.G0 = 0.02; sp.G1 = 0.1;
            // a twinkle that fades in and out on one leaf tip after another
            var tw = tt / 1.7;
            var which = ((int)Math.Floor(tw) * 5 + 3) % (tips.Count / 2);
            var fade = amp > 0 ? Math.Pow(Math.Sin(Math.PI * (tw - Math.Floor(tw))), 2) : 0;
            Sparkle(tips[2 * which], tips[2 * which + 1], d == 0 ? 0 : 0.034, fade, true);
        }

        // ================= Polaroid: an instant photo taped up, sky corners, tape that lifts, a marker heart and a scribble =================
        public void PolaroidFrame()
        {
            const string card = "#FFFEF8", card2 = "#EFE6D2", photo = "#8FD8FF", photo2 = "#C7B5FF", tape = "#FF9EC7", marker = "#FF4F8B";
            var c = P(RoundRect(0.05, 0.035, 0.95, 0.985, 0.03, d == 0 ? 2 : 4), true, false, card, Ink, K, 1, false);
            c.Hole = Window; c.Fill2 = card2; c.G0 = 0; c.G1 = 1;
            var ph = P(new[] { 0.1, 0.085, 0.9, 0.085, 0.9, 0.9, 0.1, 0.9 }, true, false, photo, Ink, K * 0.6, 1, false);
            ph.Hole = Window; ph.Fill2 = photo2; ph.G0 = 0.085; ph.G1 = 0.9;
            if (d > 0)
            {
                // a sun and a cloud in the photo's corners
                E(0.165, 0.79, 0.038, 0.038, 0, "#FFE36B", Ink, K * 0.55, 1, false);
                P(new[] { 0.75, 0.16, 0.79, 0.13, 0.84, 0.14, 0.87, 0.17, 0.83, 0.19, 0.77, 0.19 }, true, true, "#FFFFFF", Ink, K * 0.5, 1, false);
            }
            // the scribble writes itself under the photo, rests, and writes again
            var ws = amp > 0 ? 1 - amp * (1 - Smooth01(tt % 6 / 2.2)) : 1;
            int sn = d == 0 ? 6 : 12, shown = Math.Max(2, (int)Math.Floor(sn * ws + 0.5));
            var scr = new double[shown * 2];
            for (var i = 0; i < shown; i++)
            {
                scr[2 * i] = 0.13 + i * 0.36 / (sn - 1);
                scr[2 * i + 1] = 0.94 + (i % 2 == 0 ? -0.012 : 0.012);
            }
            if (d > 0) P(scr, false, true, null, marker, 0.012, 1, true);
            else P(scr, false, false, null, marker, 0.02, 1, true);
            // the marker heart beats twice, then rests
            var t4 = tt % 2.4;
            var beat = amp * (Math.Pow(Math.Max(0, Math.Sin(Tau * t4 / 0.5)), 2) * (t4 < 1 ? 1 : 0));
            var hs = (d == 0 ? 0.042 : 0.034) * (1 + 0.18 * beat);
            P(Heart(0.79, 0.945, hs), true, true, marker, Ink, K * 0.7, 1, true);
            // the gold star sticker turns slowly
            var st = P(Star(0.875, 0.94, d == 0 ? 0.04 : 0.033, amp * Tau * tt / 14), true, false, "#FFD75A", Ink, K * 0.7, 1, true);
            st.Fill2 = "#F2A51F"; st.G0 = 0.9; st.G1 = 0.98;
            // two strips of tape that lift a little at their loose ends
            for (var side = -1; side <= 1; side += 2)
            {
                var lift = amp * 0.07 * Math.Sin(Tau * tt / 3.4 + (side > 0 ? 1.3 : 0));
                double tx = 0.5 + side * 0.37, ty = 0.11, tr = side * 0.72 + lift * side;
                double[] strip = { -0.11, -0.035, -0.095, -0.02, -0.11, -0.005, -0.095, 0.01, -0.11, 0.025, -0.1, 0.035, 0.11, 0.035, 0.095, 0.02, 0.11, 0.005, 0.095, -0.01, 0.11, -0.025, 0.1, -0.035 };
                P(Place(strip, tx, ty, tr, 1), true, false, tape, Ink, K * 0.6, 0.92, true);
                if (d > 0)
                    for (var j = -1; j <= 1; j++)
                        P(Place(new[] { j * 0.05 - 0.012, -0.03, j * 0.05 + 0.006, -0.03, j * 0.05 + 0.018, 0.03, j * 0.05, 0.03 }, tx, ty, tr, 1), true, false, "#FFFFFF", null, 0, 0.55, true);
            }
        }

        // ================= Room host (Prime): a velvet marquee ring with chasing bulbs, a crown that bobs, gold rays turning behind =================
        public void HostFrame()
        {
            const string velvet = "#6A23A0", velvet2 = "#3A1160", gold = "#FFD24A", gold2 = "#E0931A", bulb = "#FFF6C2", dim = "#C79A3E", glow = "#FFD65A", gem = "#FF4FA0";
            // layer one: gold rays turning slowly behind everything (30 s a turn)
            int rays = d == 0 ? 0 : d == 1 ? 12 : 16;
            var spin = amp > 0 ? Tau * tt / 30 : 0;
            for (var i = 0; i < rays; i++)
            {
                var a = i * Tau / rays + spin;
                // sunburst spikes, long and short by turns, stopping short of the canvas edge
                var edge = 0.5 / Math.Max(Math.Abs(Math.Cos(a)), Math.Abs(Math.Sin(a))) - 0.012;
                var tip = Math.Min(i % 2 == 0 ? 0.66 : 0.57, edge);
                double a0 = a - 0.12, a1 = a + 0.12;
                P(new[] { Px(0.43, a0), Py(0.43, a0), Px(tip, a), Py(tip, a), Px(0.43, a1), Py(0.43, a1) }, true, false, i % 2 == 0 ? "#FFE27A" : "#FF8FC8", Ink, K * 0.5, 0.9, true);
            }
            // the velvet band with gold rims
            E(C, C, 0.44, 0.44, 0, null, Ink, 0.075 + 2 * K, 1, false);
            E(C, C, 0.44, 0.44, 0, null, velvet, 0.075, 1, false);
            if (d > 0) E(C, C, 0.425, 0.425, 0, null, velvet2, 0.02, 0.6, false);
            E(C, C, 0.4035, 0.4035, 0, null, gold, 0.011, 1, false);
            E(C, C, 0.4765, 0.4765, 0, null, gold, 0.011, 1, false);
            // the bulbs: three soft waves of light chase round (a smooth swell, never a blink; Gentle swells less)
            int nb = d == 0 ? 8 : d == 1 ? 14 : 18;
            var contrast = gentle ? 0.4 : 0.8;
            for (var i = 0; i < nb; i++)
            {
                var a = i * Tau / nb - Math.PI / 2 + Math.PI / nb;
                double bx = Px(0.44, a), by = Py(0.44, a);
                var raw = 0.5 + 0.5 * Math.Cos(Tau * (i * 3.0 / nb - tt / 2.4));
                var g = 1 - contrast * amp * (1 - raw);
                double br = d == 0 ? 0.026 : 0.018;
                if (d > 0) E(bx, by, br * 2, br * 2, 0, glow, null, 0, 0.5 * g, true);
                E(bx, by, br, br, 0, dim, Ink, K * 0.55, 1, false);
                E(bx, by, br * 0.85, br * 0.85, 0, bulb, null, 0, g, true);
            }
            // layer two: the crown on top, bobbing and tipping a little
            var tilt = amp * 0.07 * Math.Sin(Tau * tt / 3.6);
            var bob = amp * 0.006 * Math.Sin(Tau * tt / 1.8);
            double kx = 0.5, ky = 0.092 + bob, ks = 0.92;
            var crown = P(Place(new[] { -0.1, 0.035, -0.105, -0.045, -0.055, -0.005, 0, -0.065, 0.055, -0.005, 0.105, -0.045, 0.1, 0.035 }, kx, ky, tilt, ks), true, false, gold, Ink, K, 1, true);
            crown.Fill2 = gold2; crown.G0 = ky - 0.06; crown.G1 = ky + 0.04;
            P(Place(new[] { -0.098, 0.012, 0.098, 0.012, 0.1, 0.035, -0.1, 0.035 }, kx, ky, tilt, ks), true, false, gold2, Ink, K * 0.6, 1, true);
            var pearls = Place(new[] { -0.105, -0.05, 0, -0.072, 0.105, -0.05 }, kx, ky, tilt, ks);
            for (var i = 0; i < 6; i += 2) E(pearls[i], pearls[i + 1], 0.013 * ks, 0.013 * ks, 0, "#FFFFFF", Ink, K * 0.5, 1, true);
            var gp = Place(new[] { 0, 0.012 }, kx, ky, tilt, ks);
            E(gp[0], gp[1], 0.019 * ks, 0.016 * ks, 0, gem, Ink, K * 0.6, 1, true);
            E(gp[0] - 0.005, gp[1] - 0.005, 0.006, 0.004, -30, "#FFFFFF", null, 0, 0.9, true);
            // a twinkle by the crown, a slow fade
            var tw = amp > 0 ? Math.Pow(Math.Max(0, Math.Sin(Tau * tt / 3.1)), 2) : 0;
            Sparkle(0.655, 0.05, d == 0 ? 0 : 0.03, tw, true);
            Sparkle(0.35, 0.94, d == 0 ? 0 : 0.026, amp > 0 ? Math.Pow(Math.Max(0, Math.Sin(Tau * tt / 3.1 + 2.2)), 2) : 0, true);
        }
    }
}
