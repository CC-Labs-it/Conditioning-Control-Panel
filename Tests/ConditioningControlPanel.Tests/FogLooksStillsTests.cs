using System;
using System.IO;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Proof in pictures for the fog look (lane J2, owner notes 2026-11-17), only when CCP_FOG_DUMP names a
/// folder: Steam and the flat pink filter in the user's own colour, and Two-tone over two different user
/// colours, drawn with the real SteamDraw over a stand-in desktop.
/// </summary>
public class FogLooksStillsTests
{
    private const int W = 1280, H = 720;
    private const double Dt = 1 / 30.0;
    private static readonly (byte R, byte G, byte B) HotPink = (255, 105, 180), Teal = (64, 200, 190);

    /// <summary>Two users' own filter colours (the fog wears these, never a shop colour).</summary>
    private static readonly (string Name, (byte R, byte G, byte B) Own)[] Owns = { ("hotpink", HotPink), ("teal", Teal) };

    internal static void Desktop(SKCanvas canvas)
    {
        canvas.Clear(new SKColor(24, 28, 40));
        using var p = new SKPaint { Color = new SKColor(70, 82, 110) };
        for (int k = 0; k < 5; k++) canvas.DrawRect(80 + k * 240, 140 + (k % 2) * 260, 170, 200, p);
        using var light = new SKPaint { Color = new SKColor(210, 214, 224) };
        canvas.DrawRect(0, H - 40, W, 40, light);
    }

    internal static SteamField Fog()
    {
        var f = new SteamField(W, H);
        f.Settle(1.5, 1);
        for (int i = 0; i < 60; i++) f.Step(Dt, 1.5, 1);
        // A wiped lane so the clear desktop shows through, as under a cursor.
        for (int i = 0; i < 20; i++) f.Wipe(300 + i * 30, 360 + Math.Sin(i * 0.4) * 80, 50, Dt, mark: true);
        return f;
    }

    internal static void Save(SKBitmap bmp, string dir, string name)
    {
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), data.ToArray());
    }

    [Fact]
    public void Dump_each_colour_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_FOG_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        using var draw = new SteamDraw();
        var field = Fog();
        foreach (var (name, c) in Owns)
        {
            Desktop(canvas);
            draw.Draw(canvas, new SKRect(0, 0, W, H), field, new SKColor(c.R, c.G, c.B), 0.62, Array.Empty<string>(), null, SKTypeface.Default, 4);
            canvas.Flush();
            Save(bmp, dir, "steam-" + name);

            Desktop(canvas);
            using var tint = new SKPaint { Color = new SKColor(c.R, c.G, c.B, (byte)(0.2 * 255)) };
            canvas.DrawRect(0, 0, W, H, tint);
            canvas.Flush();
            Save(bmp, dir, "tint-" + name);
        }
    }

    /// <summary>Two-tone: five frames three seconds apart for two users (hot pink picking lilac; teal picking
    /// mint, which is too close and steps on to amber), on Steam and on the flat tint, plus the cost of a
    /// frame with and without the mix.</summary>
    [Fact]
    public void Dump_two_tone_when_asked()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_FOG_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        using var bmp = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        using var steam = new SteamDraw();
        using var tint = new FogMixDraw();
        var mix = new FogMixField();
        var bounds = new SKRect(0, 0, W, H);
        var pairs = new[] { ("hotpink-lilac", HotPink, FogLooks.Lilac), ("teal-mint", Teal, FogLooks.Mint) };
        foreach (var (name, main, fog2) in pairs)
        {
            var second = FogLooks.SecondColour(fog2, main);
            var ramp = FogMix.Ramp(main, second);
            var field = Fog();
            double t = 9;
            for (int k = 0; k < 5; k++)
            {
                Desktop(canvas);
                mix.Fill(48, 27, W / (double)H, t);
                steam.Draw(canvas, bounds, field, new SKColor(main.R, main.G, main.B), 0.62, Array.Empty<string>(), null, SKTypeface.Default, t, mix, ramp);
                canvas.Flush();
                Save(bmp, dir, $"two-steam-{name}-{k + 1}");

                Desktop(canvas);
                tint.Draw(canvas, bounds, main, second, 0.3, t);
                canvas.Flush();
                Save(bmp, dir, $"two-tint-{name}-{k + 1}");
                for (int i = 0; i < 90; i++) field.Step(Dt, 1.5, 1);
                t += 3;
            }
        }

        // Cost at 1920x1080 on a CPU canvas: Steam one colour, Steam two-tone (fill + draw), tint flat, tint two-tone.
        using var big = new SKBitmap(new SKImageInfo(1920, 1080, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var bc = new SKCanvas(big);
        var bb = new SKRect(0, 0, 1920, 1080);
        var bf = new SteamField(1920, 1080);
        bf.Settle(1.5, 1);
        using var flat = new SKPaint { Color = new SKColor(255, 105, 180, 60) };
        double Time(Action<int> frame)
        {
            for (int i = 0; i < 5; i++) frame(i);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 60; i++) frame(i);
            bc.Flush();
            return sw.Elapsed.TotalMilliseconds / 60;
        }
        double steamOne = Time(i => steam.Draw(bc, bb, bf, new SKColor(255, 105, 180), 0.62, Array.Empty<string>(), null, SKTypeface.Default, i / 30.0));
        double steamTwo = Time(i =>
        {
            mix.Fill(48, 27, 16 / 9.0, i / 30.0);
            steam.Draw(bc, bb, bf, new SKColor(255, 105, 180), 0.62, Array.Empty<string>(), null, SKTypeface.Default, i / 30.0, mix, FogMix.Ramp(HotPink, (95, 224, 184)));
        });
        double tintOne = Time(i => bc.DrawRect(bb, flat));
        double tintTwo = Time(i => tint.Draw(bc, bb, HotPink, (95, 224, 184), 0.24, i / 30.0));
        File.WriteAllText(Path.Combine(dir, "draw-cost.txt"), FormattableString.Invariant(
            $"CPU canvas 1920x1080, ms a frame over 60 frames: steam one colour {steamOne:F2}, steam two-tone {steamTwo:F2}, tint flat {tintOne:F2}, tint two-tone {tintTwo:F2}{Environment.NewLine}"));
    }
}
