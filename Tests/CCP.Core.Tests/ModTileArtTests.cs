using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ModTileArtTests
{
    [Theory]
    [InlineData(BuiltInMods.BambiSleepId, "bambi")]
    [InlineData(BuiltInMods.SissyHypnoId, "sissy")]
    [InlineData(BuiltInMods.DronificationId, "drone")]
    [InlineData(BuiltInMods.LockedId, "locked")]
    public void BuiltInFacePrecedesFallback(string modId, string suffix) =>
        Assert.Equal(new[] { $"features/vault_{suffix}.png", "features/vault.png" }, ModTileArt.Candidates("vault", modId));

    [Theory]
    [InlineData(null)]
    [InlineData(BuiltInMods.CCPDefaultId)]
    [InlineData("custom-mod")]
    public void UnmappedModUsesTheBasePath(string? modId) =>
        Assert.Equal(new[] { "features/mysterybox.png" }, ModTileArt.Candidates("mysterybox", modId));
}
