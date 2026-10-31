using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RainReleaseGateTests
{
    [Fact]
    public void ShippingGateKeepsSavedV2ButPlaysV1AndOmitsBookPage()
    {
        Assert.False(RainV2Gate.On);
        var settings = new AppSettings { AvatarGeneration = "v2" };
        Assert.Equal("v1", AvatarGenerationRule.Effective(settings.AvatarGeneration));
        Assert.Equal("v2", settings.AvatarGeneration);
        Assert.False(AvatarGenerationRule.PillVisible(RainV2Gate.On, true, false, false));
        Assert.DoesNotContain(EmiBookCards.All, c => c.Id == "rain");
    }

    [Fact]
    public void GateOnRestoresV2AndGateOffNeverResolvesItsFolder()
    {
        Assert.Equal("v2", AvatarGenerationRule.Effective(" V2 ", true));
        Assert.Equal("v1", AvatarGenerationRule.Effective("v2", false));
        Assert.True(AvatarGenerationRule.PillVisible(true, true, false, false));
        Assert.False(AvatarGenerationRule.PillVisible(true, true, true, false));
        Assert.False(AvatarGenerationRule.PillVisible(true, true, false, true));
        Assert.False(AvatarGenerationRule.PillVisible(true, false, false, false));
        Assert.Equal("classic", AvatarGenerationRule.ResolveFolder(
            AvatarGenerationRule.Effective("v2", false), "rain", () => "classic"));
        Assert.Equal("rain", AvatarGenerationRule.ResolveFolder(
            AvatarGenerationRule.Effective("v2", true), "rain", () => "classic"));
        Assert.Contains(EmiBookDeckShop.Shipping(true), c => c.Id == "rain");
        Assert.DoesNotContain(EmiBookDeckShop.Shipping(false), c => c.Id == "rain");
    }

    [Theory]
    [InlineData(BuiltInMods.BambiSleepId, "Bambi")]
    [InlineData(BuiltInMods.SissyHypnoId, "Bimbo")]
    [InlineData(null, "Emi")]
    [InlineData("unknown", "Emi")]
    public void ModelNameIsNotTheCompanionName(string? mod, string expected)
        => Assert.Equal(expected, CompanionName.ForMod(mod));
}
