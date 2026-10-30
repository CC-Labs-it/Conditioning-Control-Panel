using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Prize Counter's profile family (B3 section 2): Astra's seven PNGs in the cosmetics registry format,
/// and the clamp that keeps a frame or avatar nobody bought off the card.
/// </summary>
public class ShopCosmeticsTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", Path.Combine(parts));
    }

    private static IReadOnlyList<ShopCosmeticItem> Shipped() =>
        ShopCosmetics.Parse(File.ReadAllText(RepoFile("Resources", "cosmetics", "shop-registry.json")));

    private static readonly string[] ExpectedIds =
    {
        "profile.frame.lace", "profile.frame.ink-stamp", "profile.frame.gilt", "profile.frame.polaroid",
        "prime.frame.room-host", "profile.avatar.emi-pixel", "profile.avatar.rain-portrait",
    };

    [Fact]
    public void Registry_lists_the_seven_counter_items_with_their_grants()
    {
        var items = Shipped();
        Assert.Equal(ExpectedIds.OrderBy(x => x), items.Select(i => i.Id).OrderBy(x => x));
        Assert.All(items, i => Assert.Equal("cos." + i.Id, i.Grant));
        Assert.Equal(5, items.Count(i => i.IsFrame));
        Assert.Equal(2, items.Count(i => i.IsAvatar));
        Assert.True(items.Single(i => i.Id == "prime.frame.room-host").NeedsPrime);
        Assert.False(items.Single(i => i.Id == "profile.frame.lace").NeedsPrime);
    }

    [Fact]
    public void Every_registry_row_has_its_png_on_disk()
    {
        foreach (var item in Shipped())
            Assert.True(File.Exists(RepoFile("Resources", "cosmetics", item.File)), item.File);
    }

    [Fact]
    public void Every_item_has_a_shop_name_in_english()
    {
        var en = JObject.Parse(File.ReadAllText(RepoFile("Localization", "Languages", "en.json")));
        foreach (var item in Shipped())
            Assert.False(string.IsNullOrWhiteSpace((string?)en[item.NameKey]), item.NameKey);
    }

    [Fact]
    public void Parse_skips_junk_rows_and_never_throws()
    {
        Assert.Empty(ShopCosmetics.Parse(null));
        Assert.Empty(ShopCosmetics.Parse("not json"));
        var items = ShopCosmetics.Parse(
            "{\"items\":[{\"id\":\"a\",\"type\":\"hat\",\"file\":\"x.png\"},"
            + "{\"id\":\"b\",\"type\":\"frame\",\"file\":\"../x.png\"},"
            + "{\"id\":\"c\",\"type\":\"frame\",\"file\":\"c.png\"},"
            + "{\"id\":\"c\",\"type\":\"avatar\",\"file\":\"c2.png\"}]}");
        var only = Assert.Single(items);
        Assert.Equal("c", only.Id);
        Assert.Equal("cos.c", only.Grant);
    }

    [Fact]
    public void IsOwned_needs_the_grant()
    {
        var lace = Shipped().Single(i => i.Id == "profile.frame.lace");
        Assert.True(ShopCosmetics.IsOwned(lace, new[] { "cos.profile.frame.lace" }));
        Assert.False(ShopCosmetics.IsOwned(lace, new[] { "cos.profile.frame.gilt" }));
        Assert.False(ShopCosmetics.IsOwned(lace, null));
    }

    private static readonly Dictionary<string, string> Grants = ExpectedIds.ToDictionary(i => i, i => "cos." + i);
    private static readonly HashSet<string> Frames = ExpectedIds.Where(i => i.Contains(".frame.")).ToHashSet();
    private static readonly HashSet<string> Avatars = new(new[] { "avatar_bambi_1", "profile.avatar.emi-pixel" });

    private static ProfileCosmetics Clamp(ProfileCosmetics raw, ISet<string>? owned) =>
        ProfileCosmetics.Sanitize(raw, knownAvatarIds: Avatars, knownFrameIds: Frames, shopGrants: Grants, ownedGrants: owned);

    [Fact]
    public void Owned_frame_and_avatar_stay_on_the_card()
    {
        var clean = Clamp(new ProfileCosmetics { FrameId = "profile.frame.lace", AvatarId = "profile.avatar.emi-pixel" },
            new HashSet<string> { "cos.profile.frame.lace", "cos.profile.avatar.emi-pixel" });
        Assert.Equal("profile.frame.lace", clean.FrameId);
        Assert.Equal("profile.avatar.emi-pixel", clean.AvatarId);
    }

    [Fact]
    public void Unowned_shop_items_come_off_but_free_presets_stay()
    {
        var clean = Clamp(new ProfileCosmetics { FrameId = "prime.frame.room-host", AvatarId = "profile.avatar.emi-pixel" },
            new HashSet<string>());
        Assert.Null(clean.FrameId);
        Assert.Null(clean.AvatarId);

        var preset = Clamp(new ProfileCosmetics { AvatarId = "avatar_bambi_1" }, new HashSet<string>());
        Assert.Equal("avatar_bambi_1", preset.AvatarId);
    }

    [Fact]
    public void Unknown_grants_never_strip_a_loadout()
    {
        var clean = Clamp(new ProfileCosmetics { FrameId = "profile.frame.gilt", AvatarId = "profile.avatar.emi-pixel" }, null);
        Assert.Equal("profile.frame.gilt", clean.FrameId);
        Assert.Equal("profile.avatar.emi-pixel", clean.AvatarId);
    }

    [Fact]
    public void Unknown_frame_ids_are_dropped()
    {
        var clean = Clamp(new ProfileCosmetics { FrameId = "profile.frame.nope" }, null);
        Assert.Null(clean.FrameId);
    }

    [Fact]
    public void Frame_rides_the_payload_as_frame_id_and_is_absent_when_null()
    {
        var json = JsonConvert.SerializeObject(new ProfileCosmetics { FrameId = "profile.frame.lace" });
        Assert.Contains("\"frame_id\":\"profile.frame.lace\"", json);
        Assert.DoesNotContain("frame_id", JsonConvert.SerializeObject(new ProfileCosmetics()));
        var back = JsonConvert.DeserializeObject<ProfileCosmetics>(json)!;
        Assert.Equal("profile.frame.lace", back.FrameId);
        Assert.False(back.IsEmpty);
        Assert.Equal("profile.frame.lace", back.Clone().FrameId);
    }
}

/// <summary>Contract 4: another player's avatar and frame ride the friends and leash wires as optional cosmetics.</summary>
public class PlayerLookTests
{
    private static readonly HashSet<string> Avatars = new() { "avatar_bambi_1", "profile.avatar.emi-pixel" };
    private static readonly HashSet<string> Frames = new() { "profile.frame.lace" };

    [Fact]
    public void Reads_known_avatar_and_frame()
    {
        var look = PlayerLook.From(JObject.Parse("{\"avatar_id\":\"profile.avatar.emi-pixel\",\"frame_id\":\"profile.frame.lace\"}"), Avatars, Frames);
        Assert.Equal("profile.avatar.emi-pixel", look.AvatarId);
        Assert.Equal("profile.frame.lace", look.FrameId);
        Assert.False(look.IsEmpty);
    }

    [Fact]
    public void Unknown_ids_and_junk_read_as_none()
    {
        Assert.Same(PlayerLook.None, PlayerLook.From(null, Avatars, Frames));
        Assert.Same(PlayerLook.None, PlayerLook.From(new JArray(), Avatars, Frames));
        Assert.Same(PlayerLook.None, PlayerLook.From(JObject.Parse("{\"avatar_id\":\"x\",\"frame_id\":7}"), Avatars, Frames));
        var frameless = PlayerLook.From(JObject.Parse("{\"avatar_id\":\"avatar_bambi_1\",\"frame_id\":\"profile.frame.lace\"}"), Avatars, null);
        Assert.Null(frameless.FrameId);
        Assert.Equal("avatar_bambi_1", frameless.AvatarId);
    }

    [Fact]
    public void Friends_and_leash_wires_carry_the_look()
    {
        var snap = ConditioningControlPanel.Services.Friends.FriendsApi.ParseState(JObject.Parse(
            "{\"friends\":[{\"id\":\"u1\",\"name\":\"A\",\"cosmetics\":{\"avatar_id\":\"avatar_bambi_1\"}},{\"id\":\"u2\",\"name\":\"B\"}],"
            + "\"incoming\":[{\"id\":\"r1\",\"name\":\"C\",\"cosmetics\":{\"avatar_id\":\"avatar_sissy_2\"}}]}"));
        Assert.Equal("avatar_bambi_1", snap.Friends.Single(f => f.Id == "u1").Look.AvatarId);
        Assert.True(snap.Friends.Single(f => f.Id == "u2").Look.IsEmpty);

        var holder = ConditioningControlPanel.Services.Leash.LeashParse.Person(JObject.Parse(
            "{\"id\":\"h\",\"name\":\"H\",\"cosmetics\":{\"avatar_id\":\"avatar_circe_1\"}}"));
        Assert.Equal("avatar_circe_1", holder!.Look.AvatarId);
        Assert.Equal(new ConditioningControlPanel.Services.Leash.LeashPerson("h", "H", null),
            ConditioningControlPanel.Services.Leash.LeashParse.Person(JObject.Parse("{\"id\":\"h\",\"name\":\"H\"}")));
    }
}
