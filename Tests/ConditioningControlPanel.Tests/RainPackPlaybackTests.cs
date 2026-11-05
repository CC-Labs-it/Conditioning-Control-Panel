using System;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Services.Companion;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Lane R8 (RAIN-V2-PLAN 2.3): the tube plays baked look packs. Strip decode, the folder the
/// tube picks (pack, embedded fallback, V1 untouched) and the manifest clip lookup.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public sealed class RainPackPlaybackTests : IDisposable
{
    private const int W = 4, H = 3, Frames = 5;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-rainplay-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>A vertical strip drawn with SkiaSharp: frame i is filled with blue = 40 * i, its first
    /// pixel is fully clear and its second solid red.</summary>
    private string SyntheticStrip()
    {
        Directory.CreateDirectory(_dir);
        using var bmp = new SKBitmap(new SKImageInfo(W, H * Frames, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int f = 0; f < Frames; f++)
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    bmp.SetPixel(x, f * H + y, new SKColor(0, 0, (byte)(40 * f), 255));
        for (int f = 0; f < Frames; f++)
        {
            bmp.SetPixel(0, f * H, SKColors.Transparent);
            bmp.SetPixel(1, f * H, new SKColor(255, 0, 0, 255));
        }
        var path = Path.Combine(_dir, "idle.strip.png");
        using (var data = bmp.Encode(SKEncodedImageFormat.Png, 100))
        using (var fs = File.Create(path)) data.SaveTo(fs);
        return path;
    }

    [Fact]
    public void A_strip_decodes_into_every_frame_and_keeps_alpha()
    {
        using var d = EmoteClipPlayer.DecodeStrip(SyntheticStrip(), Frames, 15)!;
        Assert.NotNull(d.Strip);
        Assert.Equal(Frames, d.Strip!.Frames.Count);
        Assert.Equal(W, d.Strip.Width);
        Assert.Equal(H, d.Strip.Height);
        Assert.All(d.Durations, ms => Assert.Equal(67, ms));   // 15 fps
        Assert.All(d.Required, r => Assert.Equal(-1, r));      // every frame stands alone
        var f3 = d.Strip.Frames[3];                            // Pbgra32: B, G, R, A
        Assert.Equal(0, f3[3]);                                // the clear pixel stays clear
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, f3[4..8]); // the solid red one stays solid
        Assert.Equal(120, f3[(W + 2) * 4]);                    // frame 3's own fill (40 * 3)

        Assert.Null(EmoteClipPlayer.DecodeStrip(Path.Combine(_dir, "missing.strip.png"), Frames, 15));
        Assert.Null(EmoteClipPlayer.DecodeStrip(SyntheticStrip(), 4, 15));   // 15 rows do not split into 4
    }

    [Fact]
    public void The_player_steps_through_a_strip_once_and_holds_the_last_frame()
        => WpfRenderHarness.OnStaThread(() =>
        {
            var path = SyntheticStrip();
            var img = new Image();
            var player = new EmoteClipPlayer(img) { ManualClock = true };
            long now = 1000;
            player.Now = () => now;
            int completed = 0;
            player.Completed += _ => completed++;
            var uri = new Uri(path);
            player.LoadDecoded(uri, EmoteClipPlayer.DecodeStrip(path, Frames, 15)!);

            Assert.True(player.IsReady);
            Assert.Equal(Frames, player.FrameCount);
            now += 140; player.Tick(now);
            Assert.Equal(2, player.FrameIndex);
            var bmp = Assert.IsAssignableFrom<WriteableBitmap>(img.Source);
            var px = new byte[W * 4];
            bmp.CopyPixels(new System.Windows.Int32Rect(0, 0, W, 1), px, W * 4, 0);
            Assert.Equal(0, px[3]);          // alpha survives into the shown frame
            Assert.Equal(255, px[7]);

            now += 1000; player.Tick(now);
            Assert.True(player.IsComplete);
            Assert.Equal(1, completed);
            Assert.NotNull(img.Source);

            player.Stop();
            Assert.Null(img.Source);
            Assert.False(player.IsReady);
        });

    private RainPackStore Store() => new(RainPackStore.RootFor(_dir), () => null, _ => "{}");

    private string Bake(RainPackStore store, out string hash)
    {
        hash = RainLookHash.Compute("{\"items\":{\"dress\":\"midnight\"}}");
        using var w = store.Begin(hash);
        var sink = new RainPackSink(w, 15, W, H);
        for (int i = 0; i < 2; i++) sink.AddRgbaFrame("idle", i, new byte[W * H * 4]);
        for (int i = 0; i < 3; i++) sink.AddRgbaFrame("greeting", i, new byte[W * H * 4]);
        sink.FinishAll();
        return w.Commit("{\"rev\":1}");
    }

    [Fact]
    public void V2_with_a_baked_pack_plays_the_pack_folder()
    {
        var store = Store();
        var folder = Bake(store, out var hash);
        var got = RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", hash, store.TryGetFolder, () => "avatar0_emotes");
        Assert.Equal(folder, got);
        Assert.True(RainPackPlayback.IsPackFolder(got));
    }

    [Fact]
    public void A_missing_corrupt_or_failing_pack_falls_back_to_the_embedded_clips()
    {
        var store = Store();
        var missing = RainLookHash.Compute("{\"items\":{\"dress\":\"nobody\"}}");
        Assert.Equal("rain_emotes", RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", missing, store.TryGetFolder, () => "v1"));
        Assert.Equal("rain_emotes", RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", "", store.TryGetFolder, () => "v1"));
        Assert.Equal("rain_emotes", RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", "../../etc", store.TryGetFolder, () => "v1"));

        var folder = Bake(store, out var hash);
        File.WriteAllText(Path.Combine(folder, RainPackStore.ManifestName), "{ not json");
        Assert.Equal("rain_emotes", RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", hash, store.TryGetFolder, () => "v1"));
        Assert.Equal("rain_emotes", RainPackPlayback.ResolveTubeFolder("v2", "rain_emotes", hash, _ => throw new IOException(), () => "v1"));
    }

    [Fact]
    public void V1_never_looks_at_packs_and_a_mod_without_a_v2_row_stays_v1()
    {
        int lookups = 0;
        string? Lookup(string h) { lookups++; return Path.Combine(_dir, "rain-packs", h); }
        var hash = RainLookHash.Compute("{}");
        Assert.Equal("avatar0_emotes", RainPackPlayback.ResolveTubeFolder("v1", "rain_emotes", hash, Lookup, () => "avatar0_emotes"));
        Assert.Equal("avatar0_emotes", RainPackPlayback.ResolveTubeFolder(null, "rain_emotes", hash, Lookup, () => "avatar0_emotes"));
        Assert.Equal("circe_emotes", RainPackPlayback.ResolveTubeFolder("v2", null, hash, Lookup, () => "circe_emotes"));
        Assert.Equal(0, lookups);
    }

    [Fact]
    public void Inside_a_pack_a_clip_resolves_through_the_manifest()
    {
        var folder = Bake(Store(), out _);
        var manifest = RainPackPlayback.ManifestFor(folder);
        Assert.NotNull(manifest);
        Assert.Equal(Path.Combine(folder, "idle.strip.png"), RainPackPlayback.ClipPath(folder, manifest, "idle"));
        Assert.Null(RainPackPlayback.ClipPath(folder, manifest, "jump"));   // not baked: the folded map never asks
        var greeting = RainPackPlayback.ClipForFile(manifest, Path.Combine(folder, "greeting.strip.png"));
        Assert.Equal(3, greeting!.Frames);
        Assert.Equal(15, greeting.Fps);
        Assert.True(RainPackPlayback.IsStripFile(new Uri(Path.Combine(folder, "greeting.strip.png"))));

        // Embedded and mod folders are not packs and keep the webp/gif lookup.
        Assert.False(RainPackPlayback.IsPackFolder("rain_emotes"));
        Assert.False(RainPackPlayback.IsPackFolder(_dir));
        Assert.Null(RainPackPlayback.ManifestFor(_dir));
        Assert.Null(RainPackPlayback.ClipPath("rain_emotes", null, "idle"));
        Assert.False(RainPackPlayback.IsStripFile(new Uri("pack://application:,,,/Resources/rain_emotes/idle.webp")));
    }
}
