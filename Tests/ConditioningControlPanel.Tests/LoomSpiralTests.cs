using System;
using System.IO;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Whirlpool's Loom spirals: rolled, melted into each other, or pinned from a sidecar.
/// Set CCP_LOOM_DUMP to a folder to write a sheet of them.</summary>
public sealed class LoomSpiralTests
{
    [Fact]
    public void Every_style_draws_something_and_stays_inside_the_disc()
    {
        var pic = new LoomSpiral(96);
        for (int style = 0; style < 6; style++)
        {
            var q = new LoomSpec(); q.Layer.Style = style;
            pic.Clear(); pic.Add(q, 1.3, 0.2, 1);
            double sum = 0;
            for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++) sum += pic.AlphaAt(x, y);
            Assert.True(sum > 300, $"style {style} drew {sum}");
            Assert.Equal(0, pic.AlphaAt(0, 0));
        }
    }

    [Fact]
    public void A_melt_is_a_weighted_sum_and_never_overshoots()
    {
        var rng = new Random(7);
        var pic = new LoomSpiral(64);
        pic.Add(LoomSpec.Random(rng), 0.4, 0, 0.5); pic.Add(LoomSpec.Random(rng), 0.4, 0, 0.5);
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) Assert.InRange(pic.AlphaAt(x, y), 0, 1.0001);
    }

    [Fact]
    public void The_morph_holds_then_melts_into_a_new_spiral_and_a_pin_stays()
    {
        var m = new LoomMorph(new Random(3));
        var first = m.From;
        for (int i = 0; i < (int)(LoomMorph.HoldSec * 30) - 5; i++) m.Step(1 / 30.0);
        Assert.Null(m.To);
        for (int i = 0; i < 30; i++) m.Step(1 / 30.0);
        Assert.NotNull(m.To); Assert.InRange(m.Mix, 0.001, 0.999);
        for (int i = 0; i < (int)(LoomMorph.MorphSec * 30) + 5; i++) m.Step(1 / 30.0);
        Assert.NotSame(first, m.From); Assert.Null(m.To);

        var pin = new LoomSpec();
        m.Pin(pin);
        for (int i = 0; i < 30 * 60; i++) m.Step(1 / 30.0);
        Assert.Same(pin, m.From); Assert.Null(m.To);
        m.Pin(null);
        for (int i = 0; i < 30 * 8; i++) m.Step(1 / 30.0);
        Assert.NotSame(pin, m.From);
    }

    [Fact]
    public void A_sidecar_reads_into_a_spec_v2_and_v1()
    {
        var q = LoomSpecJson.Parse("{\"schema\":2,\"layer\":{\"arms\":5,\"turns\":3,\"duty\":0.4,\"style\":\"petal\",\"direction\":-1,\"colors\":[\"#00e5ff\",\"#ffffff\"],\"bandMode\":\"gradient\"},\"layer2\":{\"enabled\":true,\"arms\":3,\"colors\":[\"#ffd94d\"]},\"glow\":0.5,\"wobble\":{\"amp\":0.2,\"freq\":3,\"cycles\":1}}");
        Assert.NotNull(q);
        Assert.Equal(5, q!.Layer.Arms); Assert.Equal(5, q.Layer.Style); Assert.Equal(-1, q.Layer.Direction);
        Assert.Equal(new uint[] { 0x00e5ff, 0xffffff }, q.Layer.Colors); Assert.True(q.Layer.Gradient);
        Assert.True(q.Layer2On); Assert.Equal(0.5, q.Glow); Assert.Equal(0.2, q.WobAmp);
        var v1 = LoomSpecJson.Parse("{\"schema\":1,\"arms\":3,\"turns\":2,\"style\":\"arch\",\"duty\":0.5,\"colors\":[\"#ff69b4\"]}");
        Assert.Equal(3, v1!.Layer.Arms); Assert.Equal(1, v1.Layer.Style);
        Assert.Null(LoomSpecJson.Parse("not json")); Assert.Null(LoomSpecJson.Parse(null));
    }

    [Fact]
    public void Dump_a_sheet()
    {
        var dir = Environment.GetEnvironmentVariable("CCP_LOOM_DUMP");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var rng = new Random(11);
        using var bmp = new SKBitmap(new SKImageInfo(1600, 800, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(30, 26, 44));
        using var draw = new WhirlpoolDraw();
        var pool = new Whirlpool();
        for (int i = 0; i < 400; i++) pool.Step(1 / 30.0, 0, 0, 0, 1, 1080, false);
        LoomSpec prev = LoomSpec.Random(rng);
        for (int k = 0; k < 8; k++)
        {
            var next = LoomSpec.Random(rng);
            canvas.Save();
            canvas.Translate(200 + (k % 4) * 400 - (float)pool.X, 200 + (k / 4) * 400 - (float)pool.Y);
            canvas.Scale(190f / (float)pool.Radius, 190f / (float)pool.Radius, (float)pool.X, (float)pool.Y);
            draw.Draw(canvas, pool, 1, prev, k == 3 || k == 7 ? next : null, 0.5, 0.3);
            canvas.Restore();
            prev = next;
        }
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 90);
        File.WriteAllBytes(Path.Combine(dir, "loom-sheet.png"), data.ToArray());
    }
}
