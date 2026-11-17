using System;
using System.IO;
using System.Reflection;
using ConditioningControlPanel.BubbleCore;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Bubble skins: Core rules (deterministic, capped, inside the glass, still under Off), the soap
/// seam (nothing changes) and offscreen Champagne stills (CCP_SKIN_DUMP=dir writes PNGs).</summary>
public class BubbleSkinsTests
{
    private const string Champ = BubbleSkinIds.Champagne;
    private static readonly double[] Radii = { 8, 27, 60, 140, 400 };

    [Fact]
    public void Choice_is_soap_unless_a_drawn_skin_is_named()
    {
        foreach (var s in new[] { null, "", "glitter", "ice" })
            Assert.Equal(BubbleSkinIds.Soap, BubbleSkinChoice.Resolve(s));
        Assert.Equal(Champ, BubbleSkinChoice.Resolve(" Champagne "));
        Assert.Equal(BubbleSkinIds.Heart, BubbleSkinChoice.Resolve("HEART"));
    }

    [Fact]
    public void Interior_is_deterministic_capped_inside_the_glass_and_still_under_off()
    {
        var a = new SkinPrim[BubbleSkins.MaxInterior];
        var b = new SkinPrim[BubbleSkins.MaxInterior];
        foreach (var r in Radii)
            for (int seed = 0; seed < 40; seed++)
                for (double t = 0; t < 12; t += 0.37)
                {
                    int n = BubbleSkins.Interior(Champ, seed * 7919, t, 1, r, a);
                    Assert.InRange(n, 1, BubbleSkins.MaxInterior);
                    Assert.Equal(n, BubbleSkins.Interior(Champ, seed * 7919, t, 1, r, b));
                    for (int i = 0; i < n; i++)
                    {
                        var p = a[i];
                        Assert.Equal(p, b[i]);
                        double reach = p.Kind == SkinPrimKind.Ring ? p.R * 1.1 : p.R;
                        Assert.True(Math.Sqrt(p.X * p.X + p.Y * p.Y) + reach <= 1.0 + 1e-6, $"r={r} seed={seed} t={t} i={i}");
                        Assert.InRange(p.Alpha, 0f, 1f);
                    }
                }
        Assert.Equal(0, BubbleSkins.Interior(BubbleSkinIds.Soap, 1, 1, 1, 60, a));

        int na = BubbleSkins.Interior(Champ, 5, 0.4, 0, 60, a);   // Off: one frame whatever the time
        BubbleSkins.Interior(Champ, 5, 9.1, 0, 60, b);
        for (int i = 0; i < na; i++) Assert.Equal(a[i], b[i]);
        BubbleSkins.Interior(Champ, 5, 4, .5, 60, a);              // Reduced: half speed
        BubbleSkins.Interior(Champ, 5, 2, 1, 60, b);
        for (int i = 0; i < na; i++) Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public void Burst_is_deterministic_capped_rises_and_dies()
    {
        var a = new SkinParticle[BubbleSkins.MaxBurst];
        var b = new SkinParticle[BubbleSkins.MaxBurst];
        foreach (var r in Radii)
        {
            int n = BubbleSkins.Burst(Champ, 42, r, 1, a);
            Assert.InRange(n, 1, BubbleSkins.MaxBurst);
            Assert.Equal(n, BubbleSkins.Burst(Champ, 42, r, 1, b));
            double y0 = 0, y1 = 0;
            for (int i = 0; i < n; i++)
            {
                Assert.Equal(a[i], b[i]);
                Assert.True(Math.Sqrt(a[i].X * a[i].X + a[i].Y * a[i].Y) <= 1.0);
                Assert.False(BubbleSkins.At(a[i], a[i].Life + .01).Alive);
                if (a[i].Kind == SkinParticleKind.Dot && a[i].Life > .9f)
                { y0 += BubbleSkins.At(a[i], 0).Y; y1 += BubbleSkins.At(a[i], .85).Y; }
            }
            Assert.True(y1 < y0, "the foam drifts up");
        }
        Assert.Equal(0, BubbleSkins.Burst(Champ, 42, 60, 0, a));   // Off bursts nothing
        Assert.Equal(0, BubbleSkins.Burst(BubbleSkinIds.Soap, 42, 60, 1, a));
    }

    /// <summary>Shared burst rules for every skin: deterministic for a seed, capped, born inside the glass, dies,
    /// Off bursts nothing, Reduced flies slower.</summary>
    private static void BurstRules(string skin, int maxCount)
    {
        var a = new SkinParticle[BubbleSkins.BurstCapacity];
        var b = new SkinParticle[BubbleSkins.BurstCapacity];
        Assert.True(maxCount <= BubbleSkins.BurstCapacity);
        foreach (var r in Radii)
            for (int seed = 1; seed < 30; seed += 7)
            {
                int n = BubbleSkins.Burst(skin, seed, r, 1, a);
                Assert.InRange(n, 1, maxCount);
                Assert.Equal(n, BubbleSkins.Burst(skin, seed, r, 1, b));
                for (int i = 0; i < n; i++)
                {
                    Assert.Equal(a[i], b[i]);
                    Assert.True(Math.Sqrt(a[i].X * a[i].X + a[i].Y * a[i].Y) <= 1.0, $"{skin} r={r} i={i}");
                    Assert.False(BubbleSkins.At(a[i], a[i].Life + .01).Alive);
                }
                Assert.Equal(n, BubbleSkins.Burst(skin, seed, r, .5, b));
                for (int i = 0; i < n; i++) Assert.True(Math.Abs(b[i].Vx) <= Math.Abs(a[i].Vx) + 1e-6);
            }
        Assert.Equal(0, BubbleSkins.Burst(skin, 42, 60, 0, a));
    }

    [Fact]
    public void Heart_beats_lub_dub_is_still_under_off_and_its_pop_squeezes_then_throws_rising_hearts()
    {
        const string H = BubbleSkinIds.Heart;
        Assert.True(BubbleSkins.OwnsBody(H));
        Assert.False(BubbleSkins.OwnsBody(Champ));
        Assert.InRange(BubbleSkins.PreBurst(H, false), .1, .3);
        Assert.Equal(0, BubbleSkins.PreBurst(Champ, false));

        var a = new SkinPrim[BubbleSkins.MaxInterior];
        var b = new SkinPrim[BubbleSkins.MaxInterior];
        double lo = 9, hi = 0;
        for (double t = 0; t < 3; t += 1 / 30.0)
        {
            Assert.Equal(2, BubbleSkins.Interior(H, 77, t, 1, 60, a));
            Assert.Equal(SkinPrimKind.HeartBody, a[0].Kind);
            Assert.InRange(a[0].R, 1f, (float)(1 + BubbleSkins.HeartPulse) + 1e-4f);
            Assert.InRange(a[1].R, .15f, .97f);
            lo = Math.Min(lo, a[0].R); hi = Math.Max(hi, a[0].R);
        }
        Assert.True(hi - lo > .06, "the heart swells on the beat");
        BubbleSkins.Interior(H, 77, .3, 0, 60, a);
        BubbleSkins.Interior(H, 77, 8.8, 0, 60, b);
        Assert.Equal(a[0], b[0]);
        BubbleSkins.Interior(H, 77, 2.2, .5, 60, a);
        BubbleSkins.Interior(H, 77, 1.1, 1, 60, b);
        Assert.Equal(a[0], b[0]);

        // The outline: plump, soft tip at the bottom, cleft at the top, inside the unit circle.
        var o = BubbleSkins.HeartOutline();
        double maxY = -9, cleft = 9;
        for (int i = 0; i < o.Length; i += 2)
        {
            Assert.True(Math.Sqrt(o[i] * o[i] + o[i + 1] * o[i + 1]) <= 1.0);
            maxY = Math.Max(maxY, o[i + 1]);
            if (Math.Abs(o[i]) < .05) cleft = Math.Min(cleft, o[i + 1]);
        }
        Assert.True(maxY > .7, "a tip at the bottom");
        Assert.True(cleft > -.6 && cleft < -.3, $"a cleft at the top: {cleft}");

        Assert.True(BubbleSkins.HeartSqueeze(.5) < BubbleSkins.HeartSqueeze(0));
        Assert.True(BubbleSkins.HeartSqueeze(1) > 1);
        BurstRules(H, 1 + BubbleSkins.MaxHearts + BubbleSkins.MaxHeartDust);
        var p = new SkinParticle[BubbleSkins.BurstCapacity];
        int n = BubbleSkins.Burst(H, 9, 60, 1, p), hearts = 0;
        double y0 = 0, y1 = 0;
        for (int i = 0; i < n; i++)
            if (p[i].Kind == SkinParticleKind.Heart && p[i].Life > 1.2f)
            { hearts++; y0 += BubbleSkins.At(p[i], .6).Y; y1 += BubbleSkins.At(p[i], 1.15).Y; }
        Assert.True(hearts > 0 && y1 < y0, "the hearts rise");
    }

    [Fact]
    public void Heart_stills_draw_a_heart_in_place_of_the_sprite_and_pop_outside_it()
        => Stills(BubbleSkinIds.Heart, new[] { .05, .12, .2, .34, .6, 1.0 }, popCell: 3, outerReach: .2);

    [Fact]
    public void Marble_vanes_wind_inside_the_glass_change_colour_and_its_pop_cracks_then_falls()
    {
        const string M = BubbleSkinIds.Marble;
        Assert.True(BubbleSkins.OwnsBody(M));
        Assert.InRange(BubbleSkins.PreBurst(M, false), .15, .25);
        Assert.True(BubbleSkins.PreBurst(M, true) >= .3, "under Gentle the flash is a glow rising over 0.3 s");

        var a = new SkinQuad[BubbleSkins.MaxMarbleQuads];
        var b = new SkinQuad[BubbleSkins.MaxMarbleQuads];
        foreach (var r in Radii)
            for (double t = 0; t < 20; t += 1.3)
            {
                int n = BubbleSkins.MarbleVanes(5, t, 1, r, a);
                Assert.Equal(BubbleSkins.MarbleVaneCount * BubbleSkins.VaneSteps(r), n);
                Assert.Equal(n, BubbleSkins.MarbleVanes(5, t, 1, r, b));
                for (int i = 0; i < n; i++)
                {
                    Assert.Equal(a[i], b[i]);
                    var q = a[i];
                    foreach (var (x, y) in new[] { (q.X0, q.Y0), (q.X1, q.Y1), (q.X2, q.Y2), (q.X3, q.Y3) })
                        Assert.True(Math.Sqrt(x * x + y * y) <= .9, $"r={r} t={t} i={i}");
                }
            }
        // The colours inside travel: the mean colour moves a long way over half a minute.
        static (double, double, double) Mean(SkinQuad[] q, int n)
        {
            double r = 0, g = 0, bl = 0;
            for (int i = 0; i < n; i++) { r += (q[i].Argb >> 16) & 255; g += (q[i].Argb >> 8) & 255; bl += q[i].Argb & 255; }
            return (r / n, g / n, bl / n);
        }
        int m0 = BubbleSkins.MarbleVanes(5, 0, 1, 60, a);
        var c0 = Mean(a, m0);
        double far = 0;
        for (double t = 2; t < 30; t += 2)
        {
            var c1 = Mean(a, BubbleSkins.MarbleVanes(5, t, 1, 60, a));
            far = Math.Max(far, Math.Abs(c1.Item1 - c0.Item1) + Math.Abs(c1.Item2 - c0.Item2) + Math.Abs(c1.Item3 - c0.Item3));
        }
        Assert.True(far > 60, $"the colours change ({far:0})");
        BubbleSkins.MarbleVanes(5, .4, 0, 60, a);
        BubbleSkins.MarbleVanes(5, 12, 0, 60, b);
        Assert.Equal(a[7], b[7]);
        Assert.Equal(BubbleSkins.MarbleTumble(5, .4, 0), BubbleSkins.MarbleTumble(5, 12, 0));

        var cracks = new float[BubbleSkins.MarbleCrackCount * BubbleSkins.MarbleCrackStops * 2];
        BubbleSkins.MarbleCracks(9, cracks);
        for (int i = 0; i < BubbleSkins.MarbleCrackCount; i++)
        {
            int o = i * BubbleSkins.MarbleCrackStops * 2, e = o + (BubbleSkins.MarbleCrackStops - 1) * 2;
            Assert.Equal(0f, cracks[o]);
            Assert.InRange(Math.Sqrt(cracks[e] * cracks[e] + cracks[e + 1] * cracks[e + 1]), .96, .98);
        }
        var pts = new float[8];
        Assert.Equal(3, BubbleSkins.MarbleShard(cracks, 0, pts));
        Assert.Equal(4, BubbleSkins.MarbleShard(cracks, 5, pts));

        BurstRules(M, 1 + BubbleSkins.MarbleShards + BubbleSkins.MaxMarbleDust + BubbleSkins.MaxMarbleGlints);
        var p = new SkinParticle[BubbleSkins.BurstCapacity];
        int count = BubbleSkins.Burst(M, 9, 60, 1, p), shards = 0;
        double y0 = 0, y1 = 0;
        for (int i = 0; i < count; i++)
            if (p[i].Kind == SkinParticleKind.Shard)
            { shards++; y0 += BubbleSkins.At(p[i], .1).Y; y1 += BubbleSkins.At(p[i], .8).Y; }
        Assert.Equal(BubbleSkins.MarbleShards, shards);
        Assert.True(y1 > y0, "the shards fall");
    }

    [Fact]
    public void Marble_stills_draw_solid_glass_in_place_of_the_sprite_and_shatter_outside_it()
        => Stills(BubbleSkinIds.Marble, new[] { .05, .12, .19, .26, .45, .8 }, popCell: 4, outerReach: .05);

    [Fact]
    public void Spiral_is_a_turning_loom_spiral_of_its_own_and_its_pop_winds_in_then_flings_along_the_arms()
    {
        const string S = BubbleSkinIds.Spiral;
        Assert.False(BubbleSkins.OwnsBody(S));
        Assert.True(BubbleSkins.UnderShell(S));
        Assert.InRange(BubbleSkins.PreBurst(S, false), .3, .6);

        // Different for each bubble, the same for a seed, and only the turning weaves.
        var looks = new System.Collections.Generic.HashSet<string>();
        for (int seed = 0; seed < 40; seed++)
        {
            var a = BubbleSkins.SpiralSpec(seed * 7919);
            var b = BubbleSkins.SpiralSpec(seed * 7919);
            Assert.Equal(a.Layer.Arms, b.Layer.Arms);
            Assert.Equal(a.Layer.Colors, b.Layer.Colors);
            Assert.InRange(a.Layer.Style, 0, 3);
            looks.Add($"{a.Layer.Arms}/{a.Layer.Turns}/{a.Layer.Style}/{string.Join(",", a.Layer.Colors)}");
        }
        Assert.True(looks.Count > 30, $"distinct spirals: {looks.Count}");

        // Baked once, fading toward the rim, nothing outside the circle.
        int size = BubbleSkins.SpiralArtSize(79);
        Assert.InRange(size, BubbleSkins.MinSpiralArt, BubbleSkins.MaxSpiralArt);
        Assert.Equal(BubbleSkins.MaxSpiralArt, BubbleSkins.SpiralArtSize(400));
        var rgba = new byte[size * size * 4];
        Assert.True(BubbleSkins.SpiralBake(BubbleSkins.SpiralSpec(3), false, size, rgba));
        int lit = 0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double px = (x + .5) / size * 2 - 1, py = (y + .5) / size * 2 - 1, r = Math.Sqrt(px * px + py * py);
                byte al = rgba[(y * size + x) * 4 + 3];
                if (r >= 1) Assert.Equal(0, al);
                else if (r > .995) Assert.True(al <= 12, $"the rim fades to nothing: {al}");
                else if (r > .97) Assert.True(al <= 60, $"rim alpha {al}");
                if (r < .7 && al > 128) lit++;
            }
        Assert.True(lit > size * size / 20, $"the spiral shows: {lit}");

        // Turning, still under Off, half speed when Reduced; the wind-in turns faster and shrinks it.
        Assert.NotEqual(BubbleSkins.SpiralTurn(3, 0, 1, 0, 0, false), BubbleSkins.SpiralTurn(3, 1, 1, 0, 0, false));
        Assert.Equal(BubbleSkins.SpiralTurn(3, .4, 0, 0, 0, false), BubbleSkins.SpiralTurn(3, 9, 0, 0, 0, false));
        Assert.Equal(BubbleSkins.SpiralTurn(3, 4, .5, 0, 0, true), BubbleSkins.SpiralTurn(3, 2, 1, 0, 0, true), 9);
        Assert.True(BubbleSkins.SpiralTurn(3, 1, 1, 1, 0, false) - BubbleSkins.SpiralTurn(3, 1, 1, 0, 0, false) > 15);
        Assert.True(BubbleSkins.SpiralScale(3, 1, 1, 1) < .06);
        Assert.True(BubbleSkins.SpiralEchoScale(1, .14) > BubbleSkins.SpiralScale(3, 1, 1, 1));

        BurstRules(S, 1 + BubbleSkins.MaxSpiralOrbits + BubbleSkins.MaxSpiralDust);
        var p = new SkinParticle[BubbleSkins.BurstCapacity];
        int n = BubbleSkins.Burst(S, 11, 60, 1, p), orbits = 0;
        var spec = BubbleSkins.SpiralSpec(11);
        for (int i = 0; i < n; i++)
        {
            if (p[i].Kind != SkinParticleKind.Orbit) continue;
            orbits++;
            var s0 = BubbleSkins.At(p[i], 0); var s1 = BubbleSkins.At(p[i], .3);
            Assert.True(s1.X * s1.X + s1.Y * s1.Y > s0.X * s0.X + s0.Y * s0.Y, "out along the arms");
            uint rgb = p[i].Argb & 0xFFFFFF;
            Assert.True(Array.IndexOf(spec.Layer.Colors, rgb) >= 0 || (spec.Layer2On && Array.IndexOf(spec.Layer2.Colors, rgb) >= 0));
        }
        Assert.True(orbits >= 32);

        // Dense and fine on a large bubble (the lab's 126 along the arms), fewer on a small one, all in the shared buffer.
        foreach (var (r, lo, hi) in new[] { (80.0, 100, 126), (27.0, 40, 60) })
            for (int seed = 1; seed < 60; seed += 3)
            {
                int count = BubbleSkins.Burst(S, seed, r, 1, p), along = 0;
                double sum = 0;
                for (int i = 0; i < count; i++)
                    if (p[i].Kind == SkinParticleKind.Orbit) { along++; sum += p[i].Size; }
                Assert.InRange(along, lo, hi);
                Assert.True(r < 40 || sum / along < .02, $"fine particles: mean {sum / along:0.000}");
            }
    }

    [Fact]
    public void Spiral_stills_turn_under_the_film_and_wind_in_before_the_burst()
        => Stills(BubbleSkinIds.Spiral, new[] { .1, .25, .38, .44, .62, .9 }, popCell: 4, outerReach: 0);

    /// <summary>A mod-like palette (Bambi's accent and secondary) for the Disco tests and stills.</summary>
    private static readonly DiscoPalette ModLike = DiscoPalette.FromMod(0xFF69B4, 0x9B59B6);

    [Fact]
    public void Disco_facets_are_sphere_patches_inside_the_ball_in_saturated_palette_hues()
    {
        const string D = BubbleSkinIds.Disco;
        Assert.Equal(D, BubbleSkinChoice.Resolve("Disco"));
        Assert.True(BubbleSkins.OwnsBody(D));
        Assert.InRange(BubbleSkins.PreBurst(D, false), .05, .15);

        var a = new SkinQuad[BubbleSkins.MaxDiscoFacets];
        var b = new SkinQuad[BubbleSkins.MaxDiscoFacets];
        double ball = BubbleSkins.DiscoBall + 1e-5;
        foreach (var pal in new[] { DiscoPalette.Fallback, ModLike })
            foreach (var r in Radii)
                for (double t = 0; t < 14; t += 1.7)
                {
                    int n = BubbleSkins.DiscoFacets(9, t, 1, r, a, pal);
                    Assert.InRange(n, BubbleSkins.DiscoRows(r) * BubbleSkins.DiscoCols(r) / 3, BubbleSkins.DiscoRows(r) * BubbleSkins.DiscoCols(r));
                    Assert.Equal(n, BubbleSkins.DiscoFacets(9, t, 1, r, b, pal));
                    double inner = 0, outer = 0; int ni = 0, no = 0, lit = 0, coloured = 0;
                    for (int i = 0; i < n; i++)
                    {
                        var q = a[i];
                        Assert.Equal(q, b[i]);
                        foreach (var (x, y) in new[] { (q.X0, q.Y0), (q.X1, q.Y1), (q.X2, q.Y2), (q.X3, q.Y3) })
                            Assert.True(Math.Sqrt(x * x + y * y) <= ball, $"r={r} t={t} i={i}: a facet pokes past the ball");
                        double mx = (q.X0 + q.X1 + q.X2 + q.X3) / 4, my = (q.Y0 + q.Y1 + q.Y2 + q.Y3) / 4, d = Math.Sqrt(mx * mx + my * my);
                        double area = Math.Abs((q.X0 * q.Y1 - q.X1 * q.Y0) + (q.X1 * q.Y2 - q.X2 * q.Y1) + (q.X2 * q.Y3 - q.X3 * q.Y2) + (q.X3 * q.Y0 - q.X0 * q.Y3)) / 2;
                        if (d < .4) { inner += area; ni++; } else if (d > .85) { outer += area; no++; }
                        var hsv = DiscoPalette.Hsv(q.Argb & 0xFFFFFF);
                        if (hsv.V > .5) { lit++; if (hsv.S > .3) coloured++; }
                    }
                    Assert.True(ni > 0 && no > 0 && outer / no < inner / ni * .7, $"facets narrow toward the edge (r={r} t={t})");
                    Assert.True(lit > n * .12, $"tiles catch the light ({lit} of {n})");
                    Assert.True(coloured >= lit * .6, $"lit tiles carry hue, not grey ({coloured} of {lit})");
                }
        // Off: one frame whatever the time; Reduced: half speed.
        BubbleSkins.DiscoFacets(9, .4, 0, 60, a, ModLike);
        BubbleSkins.DiscoFacets(9, 11, 0, 60, b, ModLike);
        Assert.Equal(a[5], b[5]);
        int nr = BubbleSkins.DiscoFacets(9, 4, .5, 60, a, ModLike);
        Assert.Equal(nr, BubbleSkins.DiscoFacets(9, 2, 1, 60, b, ModLike));
        // The tiles change in a smooth flow: no tile jumps far between two frames at 30 fps.
        double jump = 0;
        for (double t = 0; t < 6; t += 1 / 30.0)
        {
            int n0 = BubbleSkins.DiscoFacets(21, t, 1, 60, a, ModLike), n1 = BubbleSkins.DiscoFacets(21, t + 1 / 30.0, 1, 60, b, ModLike);
            // The same tile a frame later is the one whose middle moved least (the ball turns 0.02 rad a frame).
            for (int i = 0; i < n1; i++)
            {
                double bx = (b[i].X0 + b[i].X2) / 2, by = (b[i].Y0 + b[i].Y2) / 2, best = 9; int at = -1;
                for (int j = 0; j < n0; j++)
                {
                    double ax = (a[j].X0 + a[j].X2) / 2, ay = (a[j].Y0 + a[j].Y2) / 2, dd = (ax - bx) * (ax - bx) + (ay - by) * (ay - by);
                    if (dd < best) { best = dd; at = j; }
                }
                if (at < 0 || best > .02 * .02 || Math.Sqrt(bx * bx + by * by) > .8) continue;   // a tile coming round the limb
                uint ca = a[at].Argb, cb = b[i].Argb;
                jump = Math.Max(jump, Math.Max(Math.Abs((int)(ca >> 16 & 255) - (int)(cb >> 16 & 255)),
                                      Math.Max(Math.Abs((int)(ca >> 8 & 255) - (int)(cb >> 8 & 255)), Math.Abs((int)(ca & 255) - (int)(cb & 255)))));
            }
        }
        Assert.True(jump < 60, $"a tile jumped {jump} in one frame");
    }

    [Fact]
    public void Disco_glints_rays_and_spots_are_capped_smooth_and_never_leave_their_reach()
    {
        const string D = BubbleSkinIds.Disco;
        var p0 = new SkinPrim[BubbleSkins.MaxInterior];
        var p1 = new SkinPrim[BubbleSkins.MaxInterior];
        Assert.True(BubbleSkins.MaxDiscoGlints + BubbleSkins.MaxDiscoSpots + BubbleSkins.MaxDiscoRays <= BubbleSkins.MaxInterior);
        double steepest = 0;
        foreach (var r in new[] { 21.6, 80.0 })
            for (int seed = 1; seed < 200; seed += 13)
            {
                int n0 = BubbleSkins.Interior(D, seed, 0, 1, r, p0, ModLike);
                Assert.Equal(BubbleSkins.DiscoGlintCount(r) + BubbleSkins.DiscoSpotCount(r) + BubbleSkins.DiscoRayCount(r), n0);
                for (double t = 1 / 30.0; t < 30; t += 1 / 30.0)
                {
                    int n1 = BubbleSkins.Interior(D, seed, t, 1, r, p1, ModLike);
                    Assert.Equal(n0, n1);
                    for (int i = 0; i < n1; i++)
                    {
                        var p = p1[i];
                        double d = Math.Sqrt(p.X * p.X + p.Y * p.Y);
                        Assert.InRange(p.Alpha, 0f, 1f);
                        Assert.NotEqual(0u, p.Argb);
                        if (p.Kind == SkinPrimKind.Glint) Assert.True(d + p.R <= BubbleSkins.DiscoBall + 1e-5, "a glint stays on the ball");
                        else Assert.True(d + p.R <= BubbleSkins.DiscoFleckReach + 1e-5, $"{p.Kind} past its reach");
                        if (p.Kind == SkinPrimKind.Ray) Assert.True(d >= BubbleSkins.DiscoBall - .05, "a ray leaves from the limb");
                        // Rising slower than full over 0.3 s: smooth, never an on/off blink.
                        steepest = Math.Max(steepest, p.Alpha - p0[i].Alpha);
                    }
                    (p0, p1) = (p1, p0);
                }
            }
        Assert.True(steepest <= .95 / 30 / .3, $"something rose {steepest:0.000} in one frame");
        int na = BubbleSkins.Interior(D, 5, .4, 0, 60, p0, ModLike);
        BubbleSkins.Interior(D, 5, 9.1, 0, 60, p1, ModLike);
        for (int i = 0; i < na; i++) Assert.Equal(p0[i], p1[i]);
    }

    [Fact]
    public void Disco_pops_into_palette_shards_and_glint_stars_within_the_old_caps()
    {
        const string D = BubbleSkinIds.Disco;
        BurstRules(D, 1 + BubbleSkins.DiscoBeams + BubbleSkins.MaxDiscoSquares + BubbleSkins.MaxDiscoStars);
        var parts = new SkinParticle[BubbleSkins.BurstCapacity];
        int full = BubbleSkins.Burst(D, 4, 80, 1, parts, palette: ModLike), beams = 0, squares = 0, stars = 0;
        var hues = new System.Collections.Generic.HashSet<uint>();
        for (int i = 0; i < full; i++)
        {
            if (parts[i].Kind == SkinParticleKind.Beam) beams++;
            if (parts[i].Kind == SkinParticleKind.Star) stars++;
            if (parts[i].Kind == SkinParticleKind.Square) { squares++; hues.Add(parts[i].Argb & 0xFFFFFF); }
        }
        Assert.Equal(BubbleSkins.DiscoBeams, beams);
        Assert.Equal(BubbleSkins.MaxDiscoSquares, squares);
        Assert.Equal(BubbleSkins.MaxDiscoStars, stars);
        Assert.Equal(ModLike.Count, hues.Count);
        for (int i = 0; i < ModLike.Count; i++) Assert.Contains(ModLike[i], hues);
        int again = BubbleSkins.Burst(D, 4, 80, 1, new SkinParticle[BubbleSkins.BurstCapacity], palette: ModLike);
        Assert.Equal(full, again);
        // Gentle visuals: no beams, shards never catch the light, stars twinkle slowly.
        int gentle = BubbleSkins.Burst(D, 4, 80, 1, parts, gentle: true, palette: ModLike);
        Assert.Equal(full - BubbleSkins.DiscoBeams, gentle);
        for (int i = 0; i < gentle; i++)
        {
            Assert.NotEqual(SkinParticleKind.Beam, parts[i].Kind);
            if (parts[i].Kind == SkinParticleKind.Square) Assert.Equal(0f, parts[i].Grow);
            if (parts[i].Kind == SkinParticleKind.Star) Assert.True(parts[i].Grow <= 3f);
        }
    }

    [Fact]
    public void Disco_stills_draw_a_mirror_ball_in_place_of_the_sprite_and_scatter_mirrors()
    {
        Stills(BubbleSkinIds.Disco, new[] { .04, .08, .14, .24, .5, .9 }, popCell: 3, outerReach: .3, palette: DiscoPalette.Fallback);
        Stills(BubbleSkinIds.Disco, new[] { .04, .08, .14, .24, .5, .9 }, popCell: 3, outerReach: .3, palette: ModLike, tag: "-mod");
        Stills(BubbleSkinIds.Disco, new[] { .04, .08, .14, .24, .5, .9 }, popCell: 3, outerReach: .3,
               palette: DiscoPalette.FromMod(0x00FF41, 0x00B8C4), tag: "-drone");
    }

    [Fact]
    public void Frost_freezes_with_age_inside_the_glass_and_its_pop_snows_down()
    {
        const string F = BubbleSkinIds.Frost;
        Assert.Equal(F, BubbleSkinChoice.Resolve(" frost"));
        Assert.True(BubbleSkins.UnderShell(F));
        Assert.False(BubbleSkins.OwnsBody(F));
        Assert.InRange(BubbleSkins.PreBurst(F, false), .05, .15);
        Assert.True(BubbleSkins.PreBurst(F, true) >= .3, "under Gentle the cracks brighten over 0.3 s");
        Assert.NotNull(BubbleLayer.SkinPainter.ShellTint(F));

        // It freezes with age: more rime, a longer flake that branches; Reduced half as fast, Off frozen through and still.
        var a = new SkinPrim[BubbleSkins.MaxInterior];
        var b = new SkinPrim[BubbleSkins.MaxInterior];
        var seg = new float[BubbleSkins.MaxFrostSegments * 4];
        int lastSpecks = 0, lastSegs = 0;
        double lastArm = 0;
        foreach (var t in new[] { 0, 1.2, 2.4, 3.6, 4.5 })
        {
            int n = BubbleSkins.Interior(F, 31, t, 1, 60, a);
            Assert.True(n >= lastSpecks && n <= BubbleSkins.FrostSpeckCount(60));
            for (int i = 0; i < n; i++)
            {
                Assert.Equal(SkinPrimKind.Speck, a[i].Kind);
                Assert.True(Math.Sqrt(a[i].X * a[i].X + a[i].Y * a[i].Y) + a[i].R * 1.42 <= BubbleSkins.Inner);
            }
            int s = BubbleSkins.FrostFlake(31, t, 1, seg);
            double arm = Math.Sqrt(seg[2] * seg[2] + seg[3] * seg[3]);
            for (int i = 0; i < s * 4; i += 2) Assert.True(Math.Sqrt(seg[i] * seg[i] + seg[i + 1] * seg[i + 1]) <= .7);
            Assert.True(s >= lastSegs && arm >= lastArm);
            lastSpecks = n; lastSegs = s; lastArm = arm;
        }
        Assert.True(lastSpecks > 15 && lastSegs == BubbleSkins.MaxFrostSegments && lastArm > .6, "frozen through");
        Assert.Equal(BubbleSkins.FrostGrowth(4, .5), BubbleSkins.FrostGrowth(2, 1), 9);
        Assert.Equal(1, BubbleSkins.FrostGrowth(.3, 0));
        int na = BubbleSkins.Interior(F, 31, .2, 0, 60, a);
        Assert.Equal(na, BubbleSkins.Interior(F, 31, 9, 0, 60, b));
        for (int i = 0; i < na; i++) Assert.Equal(a[i], b[i]);
        Assert.Equal(BubbleSkins.FrostSpin(31, .2, 0), BubbleSkins.FrostSpin(31, 9, 0));

        BurstRules(F, 1 + BubbleSkins.FrostSticks + BubbleSkins.MaxFrostDots + BubbleSkins.MaxFrostFlakes);
        var p = new SkinParticle[BubbleSkins.BurstCapacity];
        int count = BubbleSkins.Burst(F, 9, 80, 1, p), flakes = 0;
        double y0 = 0, y1 = 0;
        for (int i = 0; i < count; i++)
            if (p[i].Kind == SkinParticleKind.Flake && p[i].Life > 1.6f)
            { flakes++; y0 += BubbleSkins.At(p[i], .3).Y; y1 += BubbleSkins.At(p[i], 1.5).Y; }
        Assert.True(flakes > 10 && y1 > y0, "the flakes drift down");
    }

    [Fact]
    public void Frost_stills_grow_a_snowflake_under_the_ice_film_and_crack_into_snow()
        => Stills(BubbleSkinIds.Frost, new[] { .05, .09, .15, .3, .7, 1.4 }, popCell: 4, outerReach: 0, times: new[] { .5, 1.6, 3, 5 });

    [Fact]
    public void Soap_and_reserved_skins_leave_the_item_untouched()
    {
        foreach (var skin in new[] { BubbleSkinIds.Soap, null, "", "nope" })
        {
            var fresh = new BubbleLayer.BubbleItem();
            var item = new BubbleLayer.BubbleItem();
            BubbleLayer.ApplySkin(item, skin, 12345);
            foreach (var f in typeof(BubbleLayer.BubbleItem).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Assert.Equal(f.GetValue(fresh), f.GetValue(item));
        }
        var champ = new BubbleLayer.BubbleItem();
        BubbleLayer.ApplySkin(champ, Champ, 12345);
        Assert.Equal(Champ, champ.Skin);
        Assert.Null(BubbleLayer.SkinPainter.ShellTint(BubbleSkinIds.Soap));
    }

    private const int Cell = 300;
    private const float Side = 220f;   // sprite side; the glass radius is 0.36 of it
    private static readonly SKColor Bg = new(22, 26, 38);

    [Fact]
    public void Champagne_stills_draw_inside_the_glass_and_pop_outside_it()
        => Stills(Champ, new[] { 2 / 30.0, 6 / 30.0, 12 / 30.0, 22 / 30.0, 34 / 30.0 }, popCell: 2, outerReach: 0);

    /// <summary>
    /// Offscreen stills of one skin on the real bubble.png from the tree (CCP_SKIN_SPRITE overrides it; the
    /// stand-in is used only when the tree holds an LFS pointer). CCP_SKIN_DUMP=&lt;dir&gt; writes
    /// &lt;dir&gt;/&lt;skin&gt;/&lt;skin&gt;-interior.png (four times, a small bubble, an Off frame) and -pop.png.
    /// <paramref name="outerReach"/> is how far past the glass the live body may draw (0 = inside it).
    /// </summary>
    private static void Stills(string skin, double[] popAges, int popCell, double outerReach, double[]? times = null,
                               DiscoPalette? palette = null, string tag = "")
    {
        using var sprite = LoadSprite();
        using var paint = new BubbleLayer.SkinPainter();
        const int seed = 20261117;
        times ??= new[] { 0, 0.6, 1.3, 2.5 };
        using var strip = Canvas(Cell * 6, out var c);
        for (int i = 0; i < times.Length; i++) Bubble(c, sprite, paint, skin, seed, times[i], 1, i, Side, 1f, 1f, palette);
        Bubble(c, sprite, paint, skin, seed + 1, 1.0, 1, 4, 90f, 1f, 1f, palette);
        Bubble(c, sprite, paint, skin, seed, 7.0, 0, 5, Side, 1f, 1f, palette);
        // Pop. Without a pre-burst the base pop grows 0.04 and fades 0.066 a step at 30 fps while the burst flies;
        // with one, the skin's burst draws the dying body for the pre-burst, then its particles fly.
        var parts = new SkinParticle[BubbleSkins.BurstCapacity];
        float glass = Side * 0.5f * BubbleLayer.GlassOfHalf;
        int n = BubbleSkins.Burst(skin, seed, glass, 1, parts, palette: palette);
        double pre = BubbleSkins.PreBurst(skin, false);
        using var popArt = skin == BubbleSkinIds.Spiral ? BubbleLayer.SkinPainter.SpiralArt(seed, glass) : null;
        using var pop = Canvas(Cell * popAges.Length, out var pc);
        for (int i = 0; i < popAges.Length; i++)
        {
            double age = popAges[i];
            float px = Cell * i + Cell / 2f, py = Cell / 2f;
            if (pre <= 0)
            {
                float f = (float)(age * 30);
                if (1 - .066f * f > 0) Bubble(pc, sprite, paint, skin, seed, 2.5 + age, 1, i, Side, 1 + .04f * f, 1 - .066f * f, palette);
                paint.DrawBurst(pc, parts.AsSpan(0, n), age, px, py, glass, 1f);
            }
            else if (age < pre) paint.DrawDying(pc, skin, seed, 2.5 + age, 1, age / pre, false, px, py, glass, sprite, popArt, palette);
            else paint.DrawBurst(pc, parts.AsSpan(0, n), age - pre, px, py, glass, 1f);
        }
        var dump = Environment.GetEnvironmentVariable("CCP_SKIN_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            var dir = Path.Combine(dump, skin);
            Directory.CreateDirectory(dir);
            Save(strip, Path.Combine(dir, skin + tag + "-interior.png"));
            Save(pop, Path.Combine(dir, skin + tag + "-pop.png"));
            // Shelf size: a 60 px bubble at six moments, drawn small and shown three times larger.
            using var small = new SKBitmap(new SKImageInfo(6 * 80, 80, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var sc0 = new SKCanvas(small))
            {
                sc0.Clear(Bg);
                for (int i = 0; i < 6; i++)
                {
                    sc0.Save(); sc0.Translate(80 * i - Cell * i + 40 - Cell / 2f, 40 - Cell / 2f);
                    Bubble(sc0, sprite, paint, skin, seed, .4 + i * .7, 1, i, 60f, 1f, 1f, palette);
                    sc0.Restore();
                }
            }
            using var big = small.Resize(new SKImageInfo(6 * 240, 240, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.None);
            Save(big, Path.Combine(dir, skin + tag + "-shelf.png"));
        }

        Assert.True(Diff(strip, 0, 1) > 200, "the interior moves");
        using var still = Canvas(Cell * 2, out var sc);
        Bubble(sc, sprite, paint, skin, seed, 0.2, 0, 0, Side, 1f, 1f, palette);
        Bubble(sc, sprite, paint, skin, seed, 5.2, 0, 1, Side, 1f, 1f, palette);
        // The real art and the shaders resample a few edge pixels per cell by a step or two of a channel.
        int offDiff = Diff(still, 0, 1, 3);
        Assert.True(offDiff <= 8, $"Off is a still frame ({offDiff} pixels, {Diff(still, 0, 1)} exact)");
        Assert.Equal(Bg, strip.GetPixel(Cell + 8, 8));   // nothing lands in the cell's corner
        float bound = glass * (float)(1 + outerReach) + 2;
        for (int y = 0; y < Cell; y += 3)
            for (int x = Cell; x < Cell * 2; x += 3)
            {
                float dx = x - Cell * 1.5f, dy = y - Cell / 2f;
                if (dx * dx + dy * dy > (Side / 2 + 2) * (Side / 2 + 2)) Assert.Equal(Bg, strip.GetPixel(x, y));   // never past the sprite
                else if (outerReach > 0 && dx * dx + dy * dy > bound * bound) Assert.Equal(Bg, strip.GetPixel(x, y));
            }
        int lit = 0;
        for (int y = 0; y < Cell; y += 2)
            for (int x = Cell * popCell; x < Cell * (popCell + 1); x += 2)
            {
                float dx = x - Cell * (popCell + .5f), dy = y - Cell / 2f;
                if (dx * dx + dy * dy > glass * glass * 1.6f && pop.GetPixel(x, y) != Bg) lit++;
            }
        Assert.True(lit > 20, $"burst pixels outside the glass: {lit}");
    }

    /// <summary>The order and transform of BubbleLayer.Render: the sprite (tinted, or none for a skin with its own
    /// body), then the interior under its scale.</summary>
    private static void Bubble(SKCanvas c, SKImage sprite, BubbleLayer.SkinPainter paint, string skin, int seed, double t, double motion,
                               int cell, float side, float scale, float opacity, DiscoPalette? palette = null)
    {
        float cx = Cell * cell + Cell / 2f, cy = Cell / 2f, half = side / 2f;
        int save = c.Save();
        c.Translate(cx, cy); c.Scale(scale, scale); c.Translate(-cx, -cy);
        using var art = skin == BubbleSkinIds.Spiral ? BubbleLayer.SkinPainter.SpiralArt(seed, half * BubbleLayer.GlassOfHalf) : null;
        bool under = BubbleSkins.UnderShell(skin);
        if (under) paint.DrawInterior(c, skin, seed, t, motion, cx, cy, half * BubbleLayer.GlassOfHalf, opacity, art);
        if (!BubbleSkins.OwnsBody(skin))
        {
            using var img = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium, Color = SKColors.White.WithAlpha((byte)(opacity * 255)),
                                          ColorFilter = BubbleLayer.SkinPainter.ShellTint(skin) };
            c.DrawImage(sprite, new SKRect(cx - half, cy - half, cx + half, cy + half), img);
            img.ColorFilter = null;   // the shared filter is app-lifetime: never disposed with this paint
        }
        if (!under) paint.DrawInterior(c, skin, seed, t, motion, cx, cy, half * BubbleLayer.GlassOfHalf, opacity, null, palette);
        c.RestoreToCount(save);
    }

    /// <summary>The shipped art from the tree (a plain git blob), else CCP_SKIN_SPRITE, else the stand-in.</summary>
    private static SKImage LoadSprite()
    {
        var path = Environment.GetEnvironmentVariable("CCP_SKIN_SPRITE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources", "bubble.png"))) dir = dir.Parent;
            path = dir == null ? null : Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources", "bubble.png");
        }
        SKImage? img = null;
        try { if (path != null) img = SKImage.FromEncodedData(path); } catch { }   // an LFS pointer decodes to null
        return img ?? StandInSprite();
    }

    /// <summary>A soap stand-in with the shipped art's proportions: a rim at 0.36 of the side and a highlight.</summary>
    private static SKImage StandInSprite()
    {
        using var surf = SKSurface.Create(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surf.Canvas;
        c.Clear(SKColors.Transparent);
        using (var sh = SKShader.CreateRadialGradient(new SKPoint(128, 128), 92,
                   new[] { new SKColor(255, 255, 255, 10), new SKColor(220, 230, 255, 40), new SKColor(255, 255, 255, 170) },
                   new[] { 0f, .8f, 1f }, SKShaderTileMode.Clamp))
        using (var p = new SKPaint { IsAntialias = true, Shader = sh })
            c.DrawCircle(128, 128, 92, p);
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, 150) })
            c.DrawOval(new SKRect(87, 70, 118, 87), p);
        return surf.Snapshot();
    }

    private static SKBitmap Canvas(int w, out SKCanvas canvas)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, Cell, SKColorType.Rgba8888, SKAlphaType.Premul));
        canvas = new SKCanvas(bmp);
        canvas.Clear(Bg);
        return bmp;
    }

    /// <summary>Pixels that differ between two cells by more than <paramref name="tol"/> in any channel.</summary>
    private static int Diff(SKBitmap bmp, int a, int b, int tol = 0)
    {
        int d = 0;
        for (int y = 0; y < Cell; y++)
            for (int x = 0; x < Cell; x++)
            {
                SKColor p = bmp.GetPixel(a * Cell + x, y), q = bmp.GetPixel(b * Cell + x, y);
                if (Math.Abs(p.Red - q.Red) > tol || Math.Abs(p.Green - q.Green) > tol || Math.Abs(p.Blue - q.Blue) > tol
                    || Math.Abs(p.Alpha - q.Alpha) > tol) d++;
            }
        return d;
    }

    private static void Save(SKBitmap bmp, string path)
    {
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(path, data.ToArray());
    }
}
