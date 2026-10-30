using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using static ConditioningControlPanel.Tests.RainStripPixels;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Lane R5 (RAIN-V2-PLAN 2.3-2.5): the host side of the dressing room's Apply, driven by a fake page.
/// The controller runs its background and UI steps inline here, so every frame lands in order.
/// </summary>
public sealed class RainApplyTests : IDisposable
{
    private const string RoseFlare = "cos.rain.dress.rose-flare";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ccp-rainapply-" + Guid.NewGuid().ToString("N"));
    private readonly List<JObject> _posted = new();
    private string _look = "", _hash = "";
    private int _commits, _failures;
    private readonly List<RainBakeProgress?> _progress = new();

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class Progress : IRainProgressSource
    {
        public int Level { get; init; } = 1;
        public IReadOnlyCollection<string> UnlockedAchievements { get; init; } = Array.Empty<string>();
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return Path.Combine(new[] { dir!.FullName, "ConditioningControlPanel", "Resources" }.Concat(parts).ToArray());
    }

    private RainPackStore Store() =>
        new(RainPackStore.RootFor(_dir), () => _hash, name => File.ReadAllText(RepoFile("rain_emotes", name)));

    private RainApplyController Controller(params string[] grants) => new(new RainApplyController.Hooks
    {
        Post = msg => _posted.Add(JObject.FromObject(msg)),
        Store = Store,
        Access = () => new RainItemAccess(RainItemManifest.Load(RepoFile("web", "rain-wardrobe", RainItemManifest.FileName)),
            new RainGrantSet(grants), new Progress()),
        Clips = () => new[] { "idle", "greeting", "jump" },
        Commit = (look, hash) => { _look = look; _hash = hash; _commits++; },
        OnFailed = () => _failures++,
        Progress = p => _progress.Add(p),
    });

    private static JObject Look(string dress) =>
        JObject.Parse($"{{\"rev\":1,\"items\":{{\"dress\":\"{dress}\"}},\"colors\":{{}}}}");

    private static JObject Msg(object o) => JObject.FromObject(o);

    private JObject Last(string type) => _posted.Last(p => (string?)p["type"] == type);

    /// <summary>The page's bake: two real PNG frames per clip the GLB has, a skip for the rest.</summary>
    private static void PageBakes(RainApplyController c, string hash, bool finish = true, params string[] skip)
    {
        foreach (var clip in new[] { "idle", "greeting", "jump" })
        {
            if (skip.Contains(clip)) { c.Handle(Msg(new { type = "rain-bake-done", clip, frames = 0, skipped = true, lookHash = hash })); continue; }
            for (int i = 0; i < 2; i++)
            {
                var png = Convert.ToBase64String(EncodePng(new byte[RainApplyController.Width * RainApplyController.Height * 4], RainApplyController.Width, RainApplyController.Height));
                c.Handle(Msg(new { type = "rain-bake-frame", clip, index = i, png, lookHash = hash }));
            }
            c.Handle(Msg(new { type = "rain-bake-done", clip, frames = 2, lookHash = hash }));
        }
        if (finish) c.Handle(Msg(new { type = "rain-bake-done", all = true, lookHash = hash }));
    }

    [Fact]
    public void An_owned_look_bakes_then_commits_the_look_and_its_pack()
    {
        var c = Controller(RoseFlare);
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        var bake = Last("rain-bake");
        var hash = (string)bake["lookHash"]!;
        Assert.Equal(RainLookHash.Compute(RainLookCodec.Normalize(Look("rose-flare").ToString(Formatting.None))), hash);
        Assert.Equal(new[] { "idle", "greeting", "jump" }, bake["clips"]!.Values<string>());
        Assert.Equal((15, 192, 309), ((int)bake["fps"]!, (int)bake["w"]!, (int)bake["h"]!));
        Assert.Equal(0, _commits);

        PageBakes(c, hash, skip: "jump");

        Assert.Equal(1, _commits);
        Assert.Equal(hash, _hash);
        Assert.Contains("rose-flare", _look);
        Assert.Equal(hash, (string?)Last("rain-applied")["lookHash"]);
        var manifest = Store().TryReadManifest(hash);
        Assert.NotNull(manifest);
        Assert.Equal(new[] { "greeting", "idle" }, manifest!.Clips.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.False(c.IsBaking);
    }

    [Fact]
    public void An_already_baked_look_commits_at_once_without_a_bake()
    {
        var first = Controller(RoseFlare);
        first.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        var hash = (string)Last("rain-bake")["lookHash"]!;
        PageBakes(first, hash);
        _hash = ""; _look = ""; _posted.Clear();

        var c = Controller(RoseFlare);
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        Assert.DoesNotContain(_posted, p => (string?)p["type"] == "rain-bake");
        Assert.Equal(hash, _hash);
        Assert.Equal(hash, (string?)Last("rain-applied")["lookHash"]);
    }

    [Fact]
    public void Cancel_and_panic_leave_the_applied_look_and_drop_the_temp_pack()
    {
        _look = "previous"; _hash = "";
        var c = Controller(RoseFlare);
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        var hash = (string)Last("rain-bake")["lookHash"]!;
        PageBakes(c, hash, finish: false);
        Assert.True(Directory.Exists(Path.Combine(RainPackStore.RootFor(_dir), hash + RainPackStore.TempSuffix)));
        c.Handle(Msg(new { type = "rain-bake-done", all = true, cancelled = true, lookHash = hash }));

        Assert.Equal(("previous", 0), (_look, _commits));
        Assert.True((bool)Last("rain-applied")["failed"]!);
        Assert.False(Directory.Exists(Path.Combine(RainPackStore.RootFor(_dir), hash + RainPackStore.TempSuffix)));
        Assert.Null(Store().TryGetFolder(hash));
        Assert.Equal(0, _failures);   // a cancel is not a failure toast

        // Panic / window close mid-bake: Abort drops the temp; late frames are ignored.
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        Assert.True(c.IsBaking);
        c.Abort();
        PageBakes(c, hash);
        Assert.Equal(("previous", 0), (_look, _commits));
        Assert.False(Directory.Exists(Path.Combine(RainPackStore.RootFor(_dir), hash + RainPackStore.TempSuffix)));
    }

    [Fact]
    public void A_page_error_or_a_broken_frame_fails_the_bake_with_a_toast()
    {
        var c = Controller(RoseFlare);
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        var hash = (string)Last("rain-bake")["lookHash"]!;
        c.Handle(Msg(new { type = "rain-bake-frame", clip = "idle", index = 0, png = "not base64 !", lookHash = hash }));
        Assert.False(c.IsBaking);
        Assert.Equal((0, 1), (_commits, _failures));
        Assert.Equal("error", (string?)Last("rain-applied")["reason"]);

        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        c.Handle(Msg(new { type = "rain-bake-done", all = true, error = "readback", lookHash = hash }));
        Assert.Equal((0, 2), (_commits, _failures));
    }

    [Fact]
    public void An_unowned_piece_is_clamped_back_to_the_page_and_never_baked()
    {
        var c = Controller();   // no grants
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        Assert.DoesNotContain(_posted, p => (string?)p["type"] == "rain-bake");
        var reply = Last("rain-applied");
        Assert.True((bool)reply["failed"]!);
        Assert.Equal("clamped", (string?)reply["reason"]);
        Assert.Null(reply["look"]!["items"]!["dress"]);
        Assert.Equal(0, _commits);
    }

    [Fact]
    public void The_default_look_commits_with_no_pack_and_no_bake()
    {
        _hash = "old";
        var c = Controller();
        c.Handle(Msg(new { type = "rain-apply", look = JObject.Parse("{\"rev\":1,\"items\":{},\"colors\":{}}") }));
        Assert.DoesNotContain(_posted, p => (string?)p["type"] == "rain-bake");
        Assert.Equal(("", "", 1), (_look, _hash, _commits));
        Assert.Equal("", (string?)Last("rain-applied")["lookHash"]);
    }

    [Fact]
    public void Frames_for_another_look_are_ignored()
    {
        var c = Controller(RoseFlare);
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        c.Handle(Msg(new { type = "rain-bake-done", all = true, lookHash = new string('0', 64) }));
        Assert.True(c.IsBaking);
        Assert.Equal(0, _commits);
    }

    [Fact]
    public void Init_carries_apply_mode_the_look_and_the_account()
    {
        var init = RainApplyController.BuildInit(Look("rose-flare").ToString(Formatting.None), new[] { RoseFlare },
            12, new[] { "first_bubble" }, "it", "reduced", gentle: true, prices: null);
        Assert.Equal("init", (string?)init["type"]);
        Assert.True((bool)init["applyMode"]!);
        Assert.Equal("rose-flare", (string?)init["look"]!["items"]!["dress"]);
        Assert.Equal(new[] { RoseFlare }, init["grants"]!.Values<string>());
        Assert.Equal((12, "it", "reduced", true), ((int)init["level"]!, (string?)init["locale"], (string?)init["motion"], (bool)init["gentle"]!));
        Assert.Null(init["prices"]);

        var priced = RainApplyController.BuildInit("", null, 1, null, "en", "full", false, new Dictionary<string, int> { [RoseFlare] = 730 });
        Assert.Equal(JTokenType.Null, priced["look"]!.Type);
        Assert.Equal(730, (int)priced["prices"]![RoseFlare]!);
    }

    private List<JObject> Baking() => _posted.Where(p => (string?)p["type"] == "rain-baking").ToList();

    [Fact]
    public void A_bake_counts_its_clips_to_the_page_and_the_hook_then_clears_on_commit()
    {
        var c = Controller(RoseFlare);
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        var hash = (string)Last("rain-bake")["lookHash"]!;
        var first = Baking().Single();
        Assert.Equal((hash, 0, 3, 1, 0.0), ((string)first["lookHash"]!, (int)first["done"]!, (int)first["total"]!, (int)first["shown"]!, (double)first["fraction"]!));

        // Halfway into idle: one third of a third.
        c.Handle(Msg(new { type = "rain-bake-progress", clip = "idle", frame = 5, of = 10, lookHash = hash }));
        Assert.Equal(0.167, (double)Baking().Last()["fraction"]!, 3);
        // The same percent again posts nothing (a bake reports every frame).
        int before = Baking().Count;
        c.Handle(Msg(new { type = "rain-bake-progress", clip = "idle", frame = 5, of = 10, lookHash = hash }));
        Assert.Equal(before, Baking().Count);
        // Another look's frames and broken frames never move it.
        c.Handle(Msg(new { type = "rain-bake-progress", clip = "idle", frame = 9, of = 10, lookHash = new string('0', 64) }));
        c.Handle(Msg(new { type = "rain-bake-progress", clip = "idle", frame = "x", of = 10, lookHash = hash }));
        c.Handle(Msg(new { type = "rain-bake-progress", clip = "idle", frame = 3, of = 0, lookHash = hash }));
        Assert.Equal(before, Baking().Count);

        PageBakes(c, hash, finish: false, skip: "jump");
        var last = Baking().Last();
        Assert.Equal((3, 3, 3, 1.0), ((int)last["done"]!, (int)last["total"]!, (int)last["shown"]!, (double)last["fraction"]!));
        Assert.Equal(1.0, _progress.Last()!.Value.Fraction);

        c.Handle(Msg(new { type = "rain-bake-done", all = true, lookHash = hash }));
        Assert.Equal(1, _commits);
        Assert.Null(_progress.Last());
    }

    [Fact]
    public void A_clip_done_twice_counts_once_and_the_fraction_never_runs_back()
    {
        var c = Controller(RoseFlare);
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        var hash = (string)Last("rain-bake")["lookHash"]!;
        c.Handle(Msg(new { type = "rain-bake-done", clip = "idle", frames = 0, skipped = true, lookHash = hash }));
        c.Handle(Msg(new { type = "rain-bake-done", clip = "idle", frames = 0, skipped = true, lookHash = hash }));
        c.Handle(Msg(new { type = "rain-bake-done", clip = "wave", frames = 0, skipped = true, lookHash = hash }));   // not asked for
        Assert.Equal(1, (int)Baking().Last()["done"]!);
        // A late progress frame for a clip already in does not add to the next clip.
        c.Handle(Msg(new { type = "rain-bake-progress", clip = "idle", frame = 9, of = 10, lookHash = hash }));
        Assert.Equal(0.333, (double)Baking().Last()["fraction"]!, 3);
        var fractions = _progress.Where(p => p != null).Select(p => p!.Value.Fraction).ToList();
        Assert.Equal(fractions.OrderBy(f => f), fractions);
    }

    [Fact]
    public void Panic_cancel_and_an_error_clear_the_progress()
    {
        var c = Controller(RoseFlare);
        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        var hash = (string)Last("rain-bake")["lookHash"]!;
        c.Handle(Msg(new { type = "rain-bake-progress", clip = "idle", frame = 1, of = 4, lookHash = hash }));
        Assert.NotNull(_progress.Last());
        c.Abort();   // the window's close and panic funnel
        Assert.Null(_progress.Last());
        Assert.False(c.IsBaking);

        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        c.Handle(Msg(new { type = "rain-bake-done", all = true, cancelled = true, lookHash = hash }));
        Assert.Null(_progress.Last());

        c.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        c.Handle(Msg(new { type = "rain-bake-done", all = true, error = "readback", lookHash = hash }));
        Assert.Null(_progress.Last());
        Assert.Equal(0, _commits);
    }

    [Fact]
    public void A_cached_look_never_reports_progress()
    {
        var first = Controller(RoseFlare);
        first.Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        PageBakes(first, (string)Last("rain-bake")["lookHash"]!);
        _posted.Clear(); _progress.Clear();
        Controller(RoseFlare).Handle(Msg(new { type = "rain-apply", look = Look("rose-flare") }));
        Assert.Empty(Baking());
        Assert.Empty(_progress);
    }

    [Fact]
    public void Init_carries_the_hosts_page_lines_and_drops_blanks()
    {
        var init = RainApplyController.BuildInit("", null, 1, null, "it", "full", false, null,
            new Dictionary<string, string> { ["baking"] = "Cambio in corso: {n} di {total}", ["bakeHint"] = " " });
        Assert.Equal("Cambio in corso: {n} di {total}", (string?)init["strings"]!["baking"]);
        Assert.Null(init["strings"]!["bakeHint"]);
        Assert.Null(RainApplyController.BuildInit("", null, 1, null, "en", "full", false, null)["strings"]);
    }

    [Fact]
    public void Bake_clips_are_every_clip_the_shipped_emotes_name()
    {
        var clips = RainApplyController.ClipsFromEmotes(File.ReadAllText(RepoFile("rain_emotes", "emotes.json")));
        var webps = Directory.GetFiles(RepoFile("rain_emotes"), "*.webp").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        Assert.Equal(17, clips.Count);
        Assert.Equal("idle", clips[0]);
        Assert.All(clips, c => Assert.Contains(c, webps));
    }
}
