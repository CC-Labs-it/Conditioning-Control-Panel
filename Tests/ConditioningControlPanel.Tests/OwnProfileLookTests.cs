using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.PrizeCounter;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A bought thing is never invisible (owner 2026-11-17, "did I just buy nothing?"). The own avatar surfaces read
/// one seam (OwnProfileLook): the real loadout, or in the DEBUG shop preview the preview's own frame, display only;
/// the preview's Wear lands in the preview file and never on the account. The fitting room's "take me there" opens
/// a fixed set of places (ShownPlaces).
/// </summary>
[Collection("OwnProfileLook")]
public class OwnProfileLookTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakePreview : IPreviewProfileLook
    {
        public string? Frame, Avatar;
        public readonly System.Collections.Generic.HashSet<string> Owned = new(StringComparer.Ordinal);
        public int Sets;
        public (string? Frame, string? Avatar) Worn() => (Frame, Avatar);
        public bool Owns(string itemId) => Owned.Contains(itemId);
        public bool Wear(string itemId)
        {
            if (!Owned.Contains(itemId)) return false;
            if (itemId.Contains(".frame.")) Frame = itemId; else Avatar = itemId;
            Changed?.Invoke();
            return true;
        }
        public void Set(string? frameId, string? avatarId) { Frame = frameId; Avatar = avatarId; Sets++; Changed?.Invoke(); }
        public event Action? Changed;
    }

    /// <summary>Runs <paramref name="body"/> with the seam's real hooks recorded, and puts everything back.</summary>
    private static void WithSeam(Action<System.Collections.Generic.List<string>> body, ProfileCosmetics? real = null)
    {
        var (own, wear, granted, preview) = (OwnProfileLook.RealOwn, OwnProfileLook.RealWear, OwnProfileLook.RealGranted, OwnProfileLook.Preview);
        var realWrites = new System.Collections.Generic.List<string>();
        try
        {
            OwnProfileLook.RealOwn = () => real ?? new ProfileCosmetics();
            OwnProfileLook.RealWear = id => { realWrites.Add(id); return true; };
            OwnProfileLook.RealGranted = g => g == "cos.profile.frame.gilt";
            OwnProfileLook.Preview = null;
            body(realWrites);
        }
        finally
        {
            OwnProfileLook.Preview = preview;
            (OwnProfileLook.RealOwn, OwnProfileLook.RealWear, OwnProfileLook.RealGranted) = (own, wear, granted);
        }
    }

    [Fact]
    public void Real_mode_reads_the_account_and_writes_through_the_real_wear()
    {
        WithSeam(writes =>
        {
            Assert.False(OwnProfileLook.InPreview);
            Assert.Equal(new ProfileSlots("profile.frame.gilt", null), OwnProfileLook.Current);
            Assert.Equal("profile.frame.gilt", OwnProfileLook.Look.FrameId);
            Assert.True(OwnProfileLook.Owns(ShopCosmetics.Find("profile.frame.gilt")));
            Assert.False(OwnProfileLook.Owns(ShopCosmetics.Find("profile.frame.lace")));
            Assert.True(OwnProfileLook.WearFromCounter("profile.frame.lace"));
            Assert.Equal(new[] { "profile.frame.lace" }, writes);
            Assert.False(OwnProfileLook.SaveToPreview("profile.frame.lace", null));   // nothing to save to: the caller saves for real
        }, new ProfileCosmetics { FrameId = "profile.frame.gilt" });
    }

    [Fact]
    public void Preview_mode_shows_the_preview_frame_and_never_writes_the_account()
    {
        WithSeam(writes =>
        {
            var p = new FakePreview();
            p.Owned.Add("profile.frame.lace");
            p.Owned.Add("profile.avatar.emi-pixel");
            int changed = 0;
            void Count() => changed++;
            OwnProfileLook.Changed += Count;
            try
            {
                OwnProfileLook.Preview = p;
                Assert.True(OwnProfileLook.InPreview);
                Assert.Equal(new ProfileSlots(null, null), OwnProfileLook.Current);   // nothing worn in the preview yet
                Assert.True(OwnProfileLook.WearFromCounter("profile.frame.lace"));
                Assert.Equal("profile.frame.lace", OwnProfileLook.Current.FrameId);
                Assert.False(OwnProfileLook.WearFromCounter("profile.frame.polaroid"));   // not owned in the preview
                Assert.False(OwnProfileLook.WearFromCounter("desk.toy.rubber-duck"));      // not a profile row
                Assert.Empty(writes);                                                       // the account never heard it
                // (Changed rides the UI dispatcher when the suite has one, so its count is not asserted here.)

                // The real loadout under the overlay keeps everything but frame and picture, and is not changed itself.
                var real = new ProfileCosmetics { FrameId = "profile.frame.gilt", BannerId = "b1" };
                var shown = OwnProfileLook.Overlay(real);
                Assert.Equal("profile.frame.lace", shown.FrameId);
                Assert.Equal("b1", shown.BannerId);
                Assert.Equal("profile.frame.gilt", real.FrameId);

                // An id the preview says is worn but the registry does not know reads as none.
                p.Frame = "profile.frame.nope";
                Assert.Null(OwnProfileLook.Current.FrameId);

                Assert.True(OwnProfileLook.SaveToPreview("profile.frame.lace", "profile.avatar.emi-pixel"));
                Assert.Equal(1, p.Sets);
                Assert.Equal(new ProfileSlots("profile.frame.lace", "profile.avatar.emi-pixel"), OwnProfileLook.Current);

                // Leaving preview mode shows the real frame again: nothing real was changed.
                OwnProfileLook.Preview = null;
                Assert.Null(OwnProfileLook.Current.FrameId);   // WithSeam's real loadout is empty
                Assert.Empty(writes);
            }
            finally { OwnProfileLook.Changed -= Count; }
        });
    }

    [Fact]
    public void A_preview_with_no_picture_of_its_own_keeps_the_accounts_picture()
    {
        // Owner, 2026-11-17: a frame bought in the preview showed round an empty hole, because the preview's
        // missing picture replaced the account's free preset.
        WithSeam(writes =>
        {
            var p = new FakePreview();
            p.Owned.Add("profile.frame.ink-stamp");
            p.Owned.Add("profile.avatar.emi-pixel");
            p.Frame = "profile.frame.ink-stamp";
            OwnProfileLook.Preview = p;
            Assert.Equal(new ProfileSlots("profile.frame.ink-stamp", "avatar_sissy_1"), OwnProfileLook.Current);
            Assert.Equal("avatar_sissy_1", OwnProfileLook.Overlay(new ProfileCosmetics { AvatarId = "avatar_sissy_1" }).AvatarId);
            p.Avatar = "profile.avatar.emi-pixel";   // a picture worn in the preview still wins
            Assert.Equal("profile.avatar.emi-pixel", OwnProfileLook.Current.AvatarId);
            Assert.Empty(writes);
        }, real: new ProfileCosmetics { AvatarId = "avatar_sissy_1" });
    }

#if DEBUG
    [Fact]
    public void The_preview_file_keeps_the_worn_frame_across_restarts_and_heals_one_no_longer_owned()
    {
        var path = Path.Combine(Path.GetTempPath(), "ccp-own-look-" + Guid.NewGuid().ToString("N"), ShopPreviewBackend.FileName);
        try
        {
            var a = new ShopPreviewBackend(path, null, () => Now);
            Assert.False(a.WearProfile("profile.frame.lace"));                         // not owned yet
            // own it the way a buy does: the rules' own mark
            var data = Newtonsoft.Json.JsonConvert.DeserializeObject<ShopPreviewData>(File.ReadAllText(path))!;
            data.Owned["profile.frame.lace"] = new PreviewOwned { At = "2026-11-17T12:00:00Z", PaidSp = 120 };
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(data));
            var c = new ShopPreviewBackend(path, null, () => Now);
            int raised = 0;
            c.ProfileChanged += () => raised++;
            Assert.True(c.WearProfile("profile.frame.lace"));
            Assert.Equal(1, raised);
            Assert.Equal(("profile.frame.lace", (string?)null), new ShopPreviewBackend(path, null, () => Now).ProfileWorn());

            var look = new PreviewProfileLook(new ShopPreviewBackend(path, null, () => Now));
            Assert.Equal("profile.frame.lace", look.Worn().Frame);

            // Owned mark gone (an older file): the worn frame goes with it on the next read.
            data = Newtonsoft.Json.JsonConvert.DeserializeObject<ShopPreviewData>(File.ReadAllText(path))!;
            data.Owned.Remove("profile.frame.lace");
            File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(data));
            Assert.Equal(((string?)null, (string?)null), new ShopPreviewBackend(path, null, () => Now).ProfileWorn());
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }
#endif

    [Fact]
    public void The_preview_rules_wear_a_profile_row_in_its_own_slot_only_when_owned()
    {
        var d = ShopPreviewRules.Seed(Now, null);
        Assert.Equal("frame", ShopPreviewRules.ProfileKindOf("profile.frame.lace"));
        Assert.Equal("frame", ShopPreviewRules.ProfileKindOf("prime.frame.room-host"));
        Assert.Equal("avatar", ShopPreviewRules.ProfileKindOf("profile.avatar.emi-pixel"));
        Assert.Null(ShopPreviewRules.ProfileKindOf("desk.toy.rubber-duck"));
        Assert.Equal("not_owned", ShopPreviewRules.WearProfile(d, "profile.frame.lace").Reason);
        Assert.Equal("bad_input", ShopPreviewRules.WearProfile(d, "fx.bubble.heart").Reason);
        d.Owned["profile.frame.lace"] = new PreviewOwned();
        d.Owned["profile.avatar.emi-pixel"] = new PreviewOwned();
        Assert.True(ShopPreviewRules.WearProfile(d, "profile.frame.lace").Ok);
        Assert.Equal("profile.frame.lace", d.Profile["frame"]);
        Assert.False(d.Profile.ContainsKey("avatar"));
        ShopPreviewRules.SetProfile(d, null, "profile.frame.lace");   // a frame in the picture slot is ignored
        Assert.False(d.Profile.ContainsKey("frame"));
        Assert.False(d.Profile.ContainsKey("avatar"));
        ShopPreviewRules.SetProfile(d, "profile.frame.lace", "profile.avatar.emi-pixel");
        Assert.Equal(("profile.frame.lace", "profile.avatar.emi-pixel"), (d.Profile["frame"], d.Profile["avatar"]));
        // The effect equip and the wallet did not move.
        Assert.Equal(ShopPreviewRules.StartSp, d.Sp);
    }

    [Fact]
    public void The_host_preview_wear_never_routes_a_real_family()
    {
        var src = File.ReadAllText(RepoFile("Services", "PrizeCounter", "PrizeCounterHostService.cs"));
        int at = src.IndexOf("if (ShopPreviewBackend.Enabled)\r\n                    {", StringComparison.Ordinal);
        if (at < 0) at = src.IndexOf("if (ShopPreviewBackend.Enabled)\n                    {", StringComparison.Ordinal);
        Assert.True(at > 0);
        var branch = src.Substring(at, src.IndexOf("break;", at, StringComparison.Ordinal) - at);
        Assert.Contains("ShopPreviewBackend.Shared.WearProfile(wearId)", branch);
        Assert.DoesNotContain("RouteWear", branch);
        Assert.DoesNotContain("WearProfileItemFromCounter", branch);
        Assert.DoesNotContain("OwnProfileLook", branch);   // the seam could fall through to the real wear if unwired
    }

    [Fact]
    public void The_own_avatar_surfaces_all_read_the_seam()
    {
        Assert.Contains("OwnProfileLook.Overlay(", File.ReadAllText(RepoFile("MainWindow", "MainWindow.ProfileCosmetics.cs")));
        Assert.Contains("OwnProfileLook.Current", File.ReadAllText(RepoFile("MainWindow", "MainWindow.ProfileBubble.cs")));
        Assert.Contains("OwnProfileLook.Look", File.ReadAllText(RepoFile("Controls", "Friends", "FriendsDrawer.cs")));
        var chip = File.ReadAllText(RepoFile("Controls", "Friends", "FriendsRailChip.cs"));
        Assert.Contains("_drawer.MeLook()", chip);
        Assert.Contains("OwnProfileLook.Changed += OnOwnLook", chip);
        Assert.Contains("OwnProfileLook.Owns(item)", File.ReadAllText(RepoFile("Dialogs", "ProfileCustomizeDialog.xaml.cs")));
    }

    [Fact]
    public void The_places_open_from_a_door_and_never_from_a_game_sheet()
    {
        Assert.Equal(new[] { "profile", "desk", "race", "chess", "bubbles", "flash", "subliminal", "pinkfilter" }, ShownPlaces.All.ToArray());
        Assert.Equal(ShownPlaces.All.ToArray(), ShownPlaces.Goes("wallet", deskOn: true));
        Assert.DoesNotContain("desk", ShownPlaces.Goes("launcher", deskOn: false));
        foreach (var game in PrizeCounterHostService.GameDoors) Assert.Empty(ShownPlaces.Goes(game, deskOn: true));
        Assert.False(ShownPlaces.IsKnown("settings"));
        Assert.Null(ShownPlaces.RackKey("profile"));
    }

    [Fact]
    public void The_navigation_targets_are_real_keys()
    {
        // ShowTab's Profile page key, and the Studio rack keys of the four look pages (rack keys are API).
        Assert.Equal("discord", ShownPlaces.ProfileTab);
        Assert.Contains("case \"discord\":", File.ReadAllText(RepoFile("MainWindow", "MainWindow.TabNavigation.cs")));
        var rack = File.ReadAllText(RepoFile("Views", "Tabs", "StudioTabView.xaml.cs"));
        foreach (var place in new[] { ShownPlaces.Bubbles, ShownPlaces.Deck, ShownPlaces.Words, ShownPlaces.Fog })
            Assert.Contains($"Add(\"{ShownPlaces.RackKey(place)}\",", rack);
        // the look rows really live on those pages
        Assert.Contains("LookRow Slot=\"bubble\"", File.ReadAllText(RepoFile("Features", "BubblePopFeatureControl.xaml")));
        Assert.Contains("LookRow Slot=\"card\"", File.ReadAllText(RepoFile("Features", "FlashFeatureControl.xaml")));
        Assert.Contains("LookRow Slot=\"word\"", File.ReadAllText(RepoFile("Features", "SubliminalFeatureControl.xaml")));
        // the page's places are the host's (sheet/shown.js)
        var shown = File.ReadAllText(RepoFile("Resources", "web", "prizecounter", "sheet", "shown.js"));
        foreach (var m in System.Text.RegularExpressions.Regex.Matches(shown, @"go: '([a-z]+)'").Select(m => m.Groups[1].Value).Distinct())
            Assert.True(ShownPlaces.IsKnown(m), m);
    }

    [Fact]
    public void The_avatar_pulse_has_an_in_and_an_out_and_follows_motion_and_gentle()
    {
        Assert.Null(OwnAvatarPulse.For(MotionLevel.Off, gentle: false));
        Assert.Null(OwnAvatarPulse.For(MotionLevel.Off, gentle: true));
        var full = OwnAvatarPulse.For(MotionLevel.Full, gentle: false)!.Value;
        var less = OwnAvatarPulse.For(MotionLevel.Reduced, gentle: false)!.Value;
        var gentle = OwnAvatarPulse.For(MotionLevel.Full, gentle: true)!.Value;
        foreach (var p in new[] { full, less, gentle }) { Assert.True(p.RiseMs > 0 && p.FallMs > 0); Assert.True(p.Peak > 0 && p.Peak <= 1); }
        Assert.True(gentle.RiseMs >= 300);                       // Gentle visuals: no rise under 300 ms
        Assert.True(gentle.FallMs > full.FallMs);                // and a longer, softer fade
        Assert.True(less.GrowTo < full.GrowTo && gentle.GrowTo < full.GrowTo);
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName, "ConditioningControlPanel" }.Concat(parts).ToArray());
    }
}
