using System;
using System.IO;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiDeskItemRulesTests
{
    [Fact]
    public void OwnershipIsExactAndUnknownSnapshotPreservesValidPicks()
    {
        var look = new DeskItemLook("desk.hat.party", "desk.toy.snow-globe");
        Assert.Equal(look, EmiDeskItemRules.Clamp(look, null));
        Assert.Equal(look, EmiDeskItemRules.Clamp(look,
            new[] { "cos.desk.hat.party", "cos.desk.toy.snow-globe" }));
        Assert.Equal(new DeskItemLook(null, look.Toy), EmiDeskItemRules.Clamp(look,
            new[] { "cos.desk.toy.snow-globe", "COS.DESK.HAT.PARTY", "cos.desk.*" }));
        Assert.Same(DeskItemLook.Empty, EmiDeskItemRules.Clamp(look, Array.Empty<string>()));
        Assert.Same(DeskItemLook.Empty, EmiDeskItemRules.Clamp(
            new DeskItemLook(look.Toy, look.Hat), null));
        Assert.Same(DeskItemLook.Empty, EmiDeskItemRules.Clamp(new DeskItemLook("../x", "unknown"), null));
        Assert.Same(DeskItemLook.Empty, EmiDeskItemRules.Clamp(null, null));
    }

    [Fact]
    public void MissingPoseHasNoFallbackAndUnknownIdsCannotEscapeRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var folder = Path.Combine(root, "Resources", "emi", "desk-items", "desk.hat.party");
            Directory.CreateDirectory(folder);
            var idle = Path.Combine(folder, EmiChains.OverFileName("idle"));
            File.WriteAllText(idle, "fixture");
            Assert.Equal(idle, EmiDeskItemRules.OverPath("desk.hat.party", "idle", root));
            Assert.Null(EmiDeskItemRules.OverPath("desk.hat.party", "pet", root));
            Assert.Null(EmiDeskItemRules.OverPath("../desk.hat.party", "idle", root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
