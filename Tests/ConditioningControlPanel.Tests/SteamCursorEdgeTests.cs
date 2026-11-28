using System;
using System.Collections.Generic;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Tier-2 2026-11-18: "the mouse cursor bubble has a bit too hard of an edge". A cursor held still
/// in the fog leaves a clear middle that fades into the fog over a band, not a cut-out circle.
/// </summary>
public class SteamCursorEdgeTests
{
    private const double Dt = 1 / 30.0, Radius = 58;

    private static List<double> RestingProfile(out double cellPx)
    {
        var f = new SteamField(1920, 1080) { Drift = 0 };
        f.Settle(1.5, 1);
        for (int i = 0; i < 30 * 4; i++) { f.Wipe(960, 540, Radius, Dt, mark: false); f.Step(Dt, 1.5, 1); }
        cellPx = 1920.0 / f.W;
        int cy = (int)(540 / cellPx), cx = (int)(960 / cellPx);
        var row = new List<double>();
        for (int gx = cx; gx < f.W && gx < cx + (int)(Radius * 2 / cellPx); gx++) row.Add(f.Fog[cy * f.W + gx]);
        return row;
    }

    [Fact]
    public void A_resting_cursor_clears_the_middle()
    {
        var row = RestingProfile(out _);
        Assert.True(row[0] < 0.15, $"middle still holds {row[0]:0.00}");
    }

    [Fact]
    public void The_hole_fades_into_the_fog_over_a_band()
    {
        var row = RestingProfile(out var cellPx);
        double rest = row[^1];
        int band = 0;
        foreach (var v in row) if (v > rest * 0.15 && v < rest * 0.85) band++;
        // At least a fifth of the radius between mostly clear and mostly fog.
        Assert.True(band * cellPx >= Radius * 0.2, $"edge band {band * cellPx:0} px of a {Radius} px wipe: {string.Join(" ", row.ConvertAll(v => v.ToString("0.00")))}");
    }

    [Fact]
    public void The_half_fog_line_stays_near_the_asked_radius()
    {
        var row = RestingProfile(out var cellPx);
        double rest = row[^1];
        int half = row.FindIndex(v => v >= rest * 0.5);
        double px = half * cellPx;
        Assert.InRange(px, Radius * 0.75, Radius * 1.2);
    }
    [Fact]
    public void Dump_a_resting_cursor_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_FOG_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        System.IO.Directory.CreateDirectory(dir);
        const int w = 960, h = 540;
        var f = new SteamField(w, h) { Drift = 0 };
        f.Settle(1.5, 1);
        for (int i = 0; i < 30 * 4; i++) { f.Wipe(480, 270, Radius, Dt, mark: false); f.Step(Dt, 1.5, 1); }
        using var bmp = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(w, h, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        canvas.Clear(new SkiaSharp.SKColor(24, 28, 40));
        using var p = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(70, 82, 110) };
        canvas.DrawRect(380, 200, 200, 140, p);
        using var draw = new ConditioningControlPanel.Services.Super.Draw.SteamDraw();
        draw.Draw(canvas, new SkiaSharp.SKRect(0, 0, w, h), f, new SkiaSharp.SKColor(255, 105, 180), 0.62, Array.Empty<string>(), null, SkiaSharp.SKTypeface.Default, 4);
        canvas.Flush();
        using var data = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "cursor-rest.png"), data.ToArray());
    }
}
