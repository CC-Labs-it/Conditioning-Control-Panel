using System;
using System.IO;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Proof in pictures for the fog looks (lane J2), only when CCP_FOG_DUMP names a folder: Steam and the
/// flat pink filter in every worn colour, drawn with the real SteamDraw over a stand-in desktop.
/// </summary>
public class FogLooksStillsTests
{
    private const int W = 1280, H = 720;
    private const double Dt = 1 / 30.0;
    private static readonly (byte R, byte G, byte B) HotPink = (255, 105, 180);

    private static readonly (string Name, string Row)[] Looks =
    {
        ("pink", FogLooks.Pink), ("lilac", FogLooks.Lilac), ("mint", FogLooks.Mint), ("amber", FogLooks.Amber),
    };

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
        foreach (var (name, row) in Looks)
        {
            var c = FogLooks.Wear(row, HotPink);
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
}
