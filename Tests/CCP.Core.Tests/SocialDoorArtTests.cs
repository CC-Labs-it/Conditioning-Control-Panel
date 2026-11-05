using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SocialDoorArtTests
{
    [Theory]
    [InlineData("drone-mode")]
    [InlineData("infection-control")]
    [InlineData("builtin-locked")]
    public void NavTwinIsSelectedOnlyWhenShipped(string mod)
    {
        var twin = "nav/mods/" + mod + "/door_social.png";
        Assert.Equal(twin, CoreModArt.EmbeddedModTwin("nav/door_social.png", mod));
        Assert.Equal(twin, CoreModArt.EmbeddedPathFor("nav/door_social.png", mod, p => p == twin));
        Assert.Equal("nav/door_social.png", CoreModArt.EmbeddedPathFor("nav/door_social.png", mod, _ => false));
        Assert.Equal("nav/mods/" + mod + "/door_home.png", CoreModArt.EmbeddedModTwin("nav/door_home.png", mod));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../drone-mode")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/root")]
    public void UnsafeOrAbsentModDoesNotProbeResources(string? mod)
    {
        Assert.Null(CoreModArt.EmbeddedModTwin("nav/door_social.png", mod));
        Assert.Equal("nav/door_social.png", CoreModArt.EmbeddedPathFor("nav/door_social.png", mod,
            _ => throw new InvalidOperationException("must not probe")));
    }

    [Theory]
    [InlineData("features/flash.png")]
    [InlineData("nav/mods/drone-mode/door_social.png")]
    [InlineData("")]
    public void OnlyNavArtGetsATwin(string path) => Assert.Null(CoreModArt.EmbeddedModTwin(path, "drone-mode"));
}
