using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Xunit;
using static ConditioningControlPanel.Tests.RainStripPixels;

namespace ConditioningControlPanel.Tests;

/// <summary>Lane R7 (RAIN-V2-PLAN 2.3): the baked-look pack store and its emote fold.</summary>
public sealed class RainPackStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-rainpack-" + Guid.NewGuid().ToString("N"));
    private string? _current;
    private DateTime _now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private static string RainEmotesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Resources", "rain_emotes");
    }

    private static string? Template(string name) => File.ReadAllText(Path.Combine(RainEmotesDir(), name));

    private RainPackStore Store() =>
        new(RainPackStore.RootFor(_dir), () => _current, Template, () => _now = _now.AddMinutes(1));

    private static string Hash(int n) => RainLookHash.Compute($"{{\"items\":{{\"dress\":\"d{n}\"}}}}");

    private static string Bake(RainPackStore store, string hash, params string[] clips)
    {
        using var w = store.Begin(hash);
        var sink = new RainPackSink(w, 15, W, H);
        foreach (var c in clips)
            for (int i = 0; i < 2; i++) sink.AddRgbaFrame(c, i, Frame((byte)(i * 40)));
        sink.FinishAll();
        return w.Commit("{\"rev\":1}");
    }

    [Fact]
    public void A_crash_before_the_rename_leaves_nothing_visible_and_the_sweep_clears_it()
    {
        var store = Store();
        var hash = Hash(1);
        var w = store.Begin(hash);
        var sink = new RainPackSink(w, 15, W, H);
        sink.AddRgbaFrame("idle", 0, Frame(1));
        sink.FinishAll();
        w.BeforeRenameForTest = () => throw new IOException("power cut");
        Assert.Throws<IOException>(() => w.Commit("{}"));   // no Dispose: the process "died"

        Assert.Null(store.TryGetFolder(hash));
        Assert.False(Directory.Exists(Path.Combine(store.Root, hash)));
        Assert.True(Directory.Exists(Path.Combine(store.Root, hash + ".tmp")));

        Assert.Equal(1, Store().Sweep());
        Assert.Empty(Directory.GetDirectories(store.Root));
    }

    [Fact]
    public void Commit_publishes_manifest_strips_and_both_emote_files()
    {
        var store = Store();
        var folder = Bake(store, Hash(1), "idle", "greeting", "jump");
        Assert.Equal(folder, store.TryGetFolder(Hash(1)));
        Assert.True(Path.IsPathRooted(folder));
        var m = JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        Assert.Equal(1, (int)m["rev"]!);
        Assert.Equal(Hash(1), (string)m["lookHash"]!);
        Assert.Equal(1, (int)m["look"]!["rev"]!);
        Assert.False(string.IsNullOrEmpty((string?)m["bakedAt"]));
        var idle = m["clips"]!["idle"]!;
        Assert.Equal("idle.strip.png", (string)idle["file"]!);
        Assert.Equal((2, 15, W, H), ((int)idle["frames"]!, (int)idle["fps"]!, (int)idle["w"]!, (int)idle["h"]!));
        Assert.Equal(2, RainPackSink.ReadStrip(Path.Combine(folder, "idle.strip.png"), 2).Frames.Count);
        Assert.True(File.Exists(Path.Combine(folder, "emotes.json")));
        Assert.True(File.Exists(Path.Combine(folder, "clip_timing.json")));
        Assert.Empty(Directory.GetDirectories(store.Root, "*.tmp"));
    }

    [Fact]
    public void Eviction_keeps_three_and_never_takes_the_current_pack()
    {
        var store = Store();
        Bake(store, Hash(1), "idle");
        _current = Hash(1);                        // the oldest pack is the one the tube plays
        for (int n = 2; n <= 5; n++) Bake(store, Hash(n), "idle");

        Assert.NotNull(store.TryGetFolder(Hash(1)));
        Assert.Null(store.TryGetFolder(Hash(2)));
        Assert.Null(store.TryGetFolder(Hash(3)));
        Assert.NotNull(store.TryGetFolder(Hash(4)));
        Assert.NotNull(store.TryGetFolder(Hash(5)));
        Assert.Equal(RainPackStore.Capacity, Directory.GetDirectories(store.Root).Length);
    }

    [Fact]
    public void A_use_refreshes_a_pack_so_the_least_recently_used_goes_first()
    {
        var store = Store();
        for (int n = 1; n <= 3; n++) Bake(store, Hash(n), "idle");
        Assert.NotNull(store.TryGetFolder(Hash(1)));
        Bake(store, Hash(4), "idle");
        Assert.NotNull(store.TryGetFolder(Hash(1)));
        Assert.Null(store.TryGetFolder(Hash(2)));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"rev\":2,\"clips\":{}}")]
    [InlineData("{\"rev\":1,\"lookHash\":\"other\",\"clips\":{\"idle\":{\"file\":\"idle.strip.png\",\"frames\":2,\"fps\":15,\"w\":6,\"h\":4}}}")]
    [InlineData("{\"rev\":1,\"lookHash\":\"HASH\",\"clips\":{\"idle\":{\"file\":\"..\\\\x.png\",\"frames\":2,\"fps\":15,\"w\":6,\"h\":4}}}")]
    [InlineData("{\"rev\":1,\"lookHash\":\"HASH\",\"clips\":{\"idle\":{\"file\":\"gone.png\",\"frames\":2,\"fps\":15,\"w\":6,\"h\":4}}}")]
    public void A_corrupt_manifest_is_ignored_and_swept(string manifest)
    {
        var store = Store();
        var folder = Bake(store, Hash(1), "idle");
        File.WriteAllText(Path.Combine(folder, "manifest.json"), manifest.Replace("HASH", Hash(1)));
        Assert.Null(store.TryGetFolder(Hash(1)));
        Assert.Null(store.TryReadManifest(Hash(1)));
        Assert.Equal(1, store.Sweep());
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("ABCDEF")]
    public void TryGetFolder_refuses_anything_that_is_not_a_hash(string? hash) =>
        Assert.Null(Store().TryGetFolder(hash));

    [Fact]
    public void A_sink_refuses_gaps_bad_names_and_wrong_sizes()
    {
        using var w = Store().Begin(Hash(1));
        var sink = new RainPackSink(w, 15, W, H);
        Assert.Throws<ArgumentException>(() => sink.AddRgbaFrame("idle", 0, new byte[3]));
        Assert.Throws<ArgumentException>(() => sink.AddRgbaFrame("../idle", 0, Frame(1)));
        sink.AddRgbaFrame("idle", 1, Frame(1));
        Assert.Throws<InvalidOperationException>(() => sink.FinishClip("idle"));
        sink.AddFrame("jump", 0, EncodePng(new byte[(W + 1) * H * 4], W + 1));
        Assert.Throws<ArgumentException>(() => sink.FinishClip("jump"));
    }

    [Fact]
    public void A_three_clip_pack_folds_every_reaction_onto_the_clips_it_has()
    {
        var folder = Bake(Store(), Hash(1), "idle", "greeting", "jump");
        var have = new HashSet<string> { "idle", "greeting", "jump" };
        var j = JObject.Parse(File.ReadAllText(Path.Combine(folder, "emotes.json")));

        var idle = j["idleRotation"]!.Select(t => ((string)t["clip"]!, (int)t["weight"]!)).ToList();
        Assert.Equal(new[] { ("idle", 13) }, idle);            // 6+2+2+1+1+1 folded onto idle
        Assert.Equal(new[] { "idle" }, j["talking"]!["pool"]!.Select(t => (string)t!));
        Assert.Equal(new[] { "greeting", "idle" }, j["expressive"]!["pool"]!.Select(t => (string)t!));
        Assert.Equal(new[] { "greeting", "idle" }, j["clickEmotes"]!.Select(t => (string)t!));
        foreach (var map in new[] { "stemPrefix", "mood" })
            foreach (var p in ((JObject)j[map]!).Properties())
                Assert.Contains((string)p.Value!, have);
        Assert.Equal("jump", (string)j["stemPrefix"]!["level_up"]!);
        Assert.Equal("greeting", (string)j["stemPrefix"]!["session_start"]!);
        Assert.Equal("greeting", (string)j["stemPrefix"]!["bubble_pop"]!);   // giggle -> greeting
        Assert.Equal("idle", (string)j["mood"]!["teasing"]!);                // smirk -> idle
        Assert.Equal(1.07, (double)j["layout"]!["scale"]!);                  // the rest is copied

        var timing = JObject.Parse(File.ReadAllText(Path.Combine(folder, "clip_timing.json")));
        Assert.DoesNotContain(timing.Properties(), p => !p.Name.StartsWith("_"));
    }

    [Fact]
    public void A_full_pack_keeps_emotes_unchanged()
    {
        var template = JObject.Parse(Template("emotes.json")!);
        var all = Directory.GetFiles(RainEmotesDir(), "*.webp").Select(p => Path.GetFileNameWithoutExtension(p)!).ToHashSet();
        Assert.Equal(17, all.Count);
        Assert.True(JToken.DeepEquals(template, RainPackEmotesFold.FoldEmotes(template, all)));
    }

    [Fact]
    public void The_store_follows_the_CCP_USERDATA_DIR_redirect()
    {
        var sandbox = Path.Combine(_dir, "sandbox");
        Assert.Equal(Path.Combine(sandbox, "rain-packs"), RainPackStore.RootFor(App.ResolveUserDataPath(sandbox)));
        // Unset (or not rooted) = the real LocalAppData profile.
        Assert.EndsWith(Path.Combine("ConditioningControlPanel", "rain-packs"), RainPackStore.RootFor(App.ResolveUserDataPath(null)));
        Assert.EndsWith(Path.Combine("ConditioningControlPanel", "rain-packs"), RainPackStore.RootFor(App.ResolveUserDataPath("relative")));
    }
}
