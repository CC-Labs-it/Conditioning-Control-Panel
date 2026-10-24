using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiDoorOpenMappingTests
{
    [Theory]
    [InlineData("PRESETS", "sessions")]
    [InlineData("companion", "companion")]
    [InlineData("awareness", "awareness")]
    [InlineData("remotecontrol", "remote")]
    [InlineData("bambitakeover", "takeover")]
    [InlineData("lockdown", "lockdown")]
    [InlineData("premium", "vault")]
    [InlineData("discord", "profile")]
    [InlineData("appsettings", "settings")]
    [InlineData("progression", "progression")]
    public void TabOpenScoresItsCatalogueDoor(string key, string target)
        => Assert.Equal(target, EmiDoors.TargetForTab(key));

    [Theory]
    [InlineData("FLASH", "flashes")]
    [InlineData("video", "videos")]
    [InlineData("subliminal", "subliminals")]
    [InlineData("bubbles", "bubbles")]
    [InlineData("spiral", "loom")]
    [InlineData("pinkfilter", "pinkfilter")]
    [InlineData("braindrain", "braindrain")]
    [InlineData("mindwipe", "mindwipe")]
    public void RackOpenScoresTheModuleInsteadOfStudio(string key, string target)
        => Assert.Equal(target, EmiDoors.TargetForRack(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("settings")]
    [InlineData("studio")]
    [InlineData("quests")]
    [InlineData("achievements")]
    [InlineData("fyp")]
    [InlineData("justdrop")]
    [InlineData("gradedintake")]
    [InlineData("spiral")]
    [InlineData("exclusives")]
    public void NonCardTabsAndHostLandingPagesDoNotScore(string? key)
        => Assert.Null(EmiDoors.TargetForTab(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("haptics")]
    [InlineData("scheduler")]
    public void NonCardRackModulesDoNotScore(string? key)
        => Assert.Null(EmiDoors.TargetForRack(key));
}
