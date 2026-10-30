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
