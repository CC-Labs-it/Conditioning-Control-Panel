using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
namespace CCP.Core.Tests;
public sealed class ShopProfileTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("{\"items\":false}")]
    public void BrokenRegistryFailsClosed(string? json) => Assert.Empty(ShopCosmeticsRegistry.Parse(json));

    [Theory]
    [InlineData(null)]
    [InlineData("{\"items\":[]}")]
    [InlineData("broken")]
    public void UnavailableFrameRegistryPreservesSavedFrame(string? json)
    {
        var ids = ShopCosmeticsRegistry.FrameIds(ShopCosmeticsRegistry.Parse(json));
        Assert.Null(ids);
        var raw = new ProfileCosmetics { FrameId = "profile.frame.gilt", Accent = "#FFD700" };
        var saved = ProfileCosmetics.Sanitize(raw, knownFrameIds: ids);
        Assert.Equal(raw.FrameId, saved.FrameId);
        Assert.Equal(raw.Accent, saved.Accent);
        var available = ShopCosmeticsRegistry.FrameIds(new[] { new ShopProfileItem("known", "frame", "f.png", "cos.known", false) });
        Assert.Contains("known", available!);
        Assert.Null(ProfileCosmetics.Sanitize(raw, knownFrameIds: available).FrameId);
    }

    [Fact]
    public void RegistryRejectsTraversalUnknownTypesAndDuplicates()
    {
        var items = ShopCosmeticsRegistry.Parse("""
            {"items":[{"id":"frame","type":"frame","file":"shop/f.png","needs":"prime"},
            {"id":"frame","type":"frame","file":"shop/other.png"},
            {"id":"bad","type":"frame","file":"../bad.png"},
            {"id":"root","type":"avatar","file":"/bad.png"},
            {"id":"other","type":"theme","file":"shop/t.png"}]}
            """);
        var item = Assert.Single(items);
        Assert.Equal("cos.frame", item.Grant);
        Assert.True(item.NeedsPrime);
        Assert.False(ShopCosmeticsRegistry.IsOwned(item, null));
        Assert.True(ShopCosmeticsRegistry.IsOwned(item, new[] { item.Grant }));
    }

    [Fact]
    public void OwnershipClampPreservesUnknownGrantsButRejectsKnownUnowned()
    {
        var raw = new ProfileCosmetics { AvatarId = "avatar", FrameId = "frame" };
        var avatars = new HashSet<string> { "avatar" };
        var frames = new HashSet<string> { "frame" };
        var grants = new Dictionary<string, string> { ["avatar"] = "cos.avatar", ["frame"] = "cos.frame" };
        ProfileCosmetics Clean(ISet<string>? owned) => ProfileCosmetics.Sanitize(raw,
            knownAvatarIds: avatars, knownFrameIds: frames, shopGrants: grants, ownedGrants: owned);
        Assert.Equal("frame", Clean(null).FrameId);
        Assert.Equal("avatar", Clean(null).AvatarId);
        Assert.Null(Clean(new HashSet<string>()).FrameId);
        Assert.Null(Clean(new HashSet<string>()).AvatarId);
        var clean = Clean(grants.Values.ToHashSet());
        Assert.Equal("frame", clean.FrameId);
        Assert.Equal("avatar", clean.AvatarId);
        Assert.Equal("frame", raw.FrameId);
        var look = PlayerLook.From(JObject.Parse("""{"avatar_id":"avatar","frame_id":"frame"}"""), avatars, frames);
        Assert.Equal(new PlayerLook("avatar", "frame"), look);
        Assert.True(PlayerLook.From(JObject.Parse("""{"avatar_id":"unknown","frame_id":42}"""), avatars, frames).IsEmpty);
        Assert.True(PlayerLook.From(null, avatars, frames).IsEmpty);
    }
}
