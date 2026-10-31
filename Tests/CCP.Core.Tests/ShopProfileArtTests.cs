using System.Linq;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ShopProfileArtTests
{
    [Fact]
    public void RegistryRejectsJunkTraversalAndDuplicates()
    {
        var rows = ShopProfileArt.Parse("""
            {"items":[{"id":"good","type":"frame","file":"shop/good.png"},
            {"id":"good","type":"frame","file":"shop/other.png"},
            {"id":"escape","type":"frame","file":"../bad.png"},
            {"id":"root","type":"avatar","file":"/bad.png"},
            {"id":"wrong","type":"kart","file":"shop/kart.png"}]}
            """);
        Assert.Equal("good", Assert.Single(rows).Id);
        Assert.Empty(ShopProfileArt.Parse("bad json"));
    }

    [Fact]
    public void ShippedShopIdsSurviveWireValidationAndTypeMismatchHasNoArt()
    {
        var ids = ShopProfileArt.KnownIds();
        Assert.Contains("profile.frame.lace", ids.Frames!);
        Assert.Contains("profile.avatar.emi-pixel", ids.Avatars);
        var look = PlayerLook.From(JObject.Parse("""{"avatar_id":"profile.avatar.emi-pixel","frame_id":"profile.frame.lace"}"""),
            ids.Avatars, ids.Frames);
        Assert.Equal(new PlayerLook("profile.avatar.emi-pixel", "profile.frame.lace"), look);
        Assert.NotNull(ShopProfileArt.ArtPath(look.FrameId, "frame"));
        Assert.Null(ShopProfileArt.ArtPath(look.FrameId, "avatar"));
        Assert.Null(ShopProfileArt.ArtPath("../escape", "frame"));
        Assert.False(look.IsEmpty);
    }
}
