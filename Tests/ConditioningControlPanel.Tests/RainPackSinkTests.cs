using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Small RGBA frames with clear, half and solid alpha for the Rain pack tests.</summary>
internal static class RainStripPixels
{
    public const int W = 6, H = 4;

    public static byte[] Frame(byte seed)
    {
        var px = new byte[W * H * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = seed; px[i + 1] = (byte)(i % 251); px[i + 2] = 200;
            px[i + 3] = (byte)((i / 4) % 3 == 0 ? 0 : (i / 4) % 3 == 1 ? 128 : 255);   // clear, half, solid
        }
        return px;
    }

    public static byte[] EncodePng(byte[] rgba, int w = W, int h = H)
    {
        using var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bmp.GetPixels(), rgba.Length);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

/// <summary>Lane R7 (RAIN-V2-PLAN 2.3): the look hash, the clip strips, the machine-local pack setting.</summary>
public sealed class RainPackSinkTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-rainstrip-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string Hash(int n) => RainLookHash.Compute($"{{\"items\":{{\"dress\":\"d{n}\"}}}}");

    [Fact]
    public void The_look_hash_is_stable_across_key_order_and_lowercase_hex()
    {
        const string look = "{\"rev\":1,\"items\":{\"hair\":\"h1\",\"dress\":\"d1\"},\"colors\":{\"fabric\":\"#ff00aa\"}}";
        var a = RainLookHash.Compute(look);
        Assert.Equal(a, RainLookHash.Compute("{ \"colors\": { \"fabric\": \"#ff00aa\" }, \"items\": { \"dress\": \"d1\", \"hair\": \"h1\" }, \"rev\": 1 }"));
        Assert.Equal(a, RainLookHash.Compute("{\"items\":{\"dress\":\"d1\",\"hair\":\"h1\"},\"rev\":1,\"colors\":{\"fabric\":\"#ff00aa\"}}"));
        Assert.True(RainLookHash.IsWellFormed(a));
        Assert.NotEqual(a, RainLookHash.Compute(look.Replace("h1", "h2")));
        Assert.NotEqual(a, RainLookHash.Compute(look, RainLookHash.PackRev + 1, RainLookHash.ClipSetVersion));
        Assert.NotEqual(a, RainLookHash.Compute(look, RainLookHash.PackRev, RainLookHash.ClipSetVersion + 1));
        Assert.Equal("{\"a\":\"2026-10-30T00:00:00Z\"}", RainLookHash.Normalize("{\"a\":\"2026-10-30T00:00:00Z\"}"));
        Assert.Equal("{}", RainLookHash.Normalize("not json"));
    }

    [Fact]
    public void A_strip_round_trip_keeps_frame_count_and_alpha()
    {
        Directory.CreateDirectory(_dir);
        var frames = Enumerable.Range(0, 5).Select(i => RainStripPixels.Frame((byte)(i * 50))).ToList();
        var path = Path.Combine(_dir, "s.strip.png");
        RainPackSink.WriteRgbaStrip(path, RainStripPixels.W, RainStripPixels.H, frames);

        var back = RainPackSink.ReadStrip(path, 5);
        Assert.Equal((RainStripPixels.W, RainStripPixels.H, 5), (back.Width, back.Height, back.Frames.Count));
        for (int i = 0; i < 5; i++) Assert.Equal(frames[i], back.Frames[i]);   // straight alpha, byte exact

        // PNG frames in, the same pixels out.
        var path2 = Path.Combine(_dir, "p.strip.png");
        RainPackSink.WriteStrip(path2, RainStripPixels.W, RainStripPixels.H, frames.Select(f => RainStripPixels.EncodePng(f)).ToList());
        var back2 = RainPackSink.ReadStrip(path2, 5);
        for (int i = 0; i < 5; i++) Assert.Equal(frames[i], back2.Frames[i]);
        Assert.Contains(back2.Frames[0].Where((_, k) => k % 4 == 3), a => a == 0);
        Assert.Contains(back2.Frames[0].Where((_, k) => k % 4 == 3), a => a == 128);

        Assert.Throws<InvalidDataException>(() => RainPackSink.ReadStrip(path, 3));   // 20 rows do not split in 3
        Assert.Throws<ArgumentException>(() => RainPackSink.WriteStrip(Path.Combine(_dir, "x.png"), RainStripPixels.W, RainStripPixels.H,
            new[] { RainStripPixels.EncodePng(new byte[(RainStripPixels.W + 1) * RainStripPixels.H * 4], RainStripPixels.W + 1) }));
    }

    [Fact]
    public void The_pack_hash_is_machine_local()
    {
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.RainPackHash)));
        var current = new AppSettings { RainPackHash = Hash(1) };
        var restored = new AppSettings { RainPackHash = Hash(2) };
        ProfileSyncService.PreserveLocalOnlyFields(current, restored);
        Assert.Equal(Hash(1), restored.RainPackHash);
        Assert.Equal("", new AppSettings { RainPackHash = null! }.RainPackHash);
    }
}
