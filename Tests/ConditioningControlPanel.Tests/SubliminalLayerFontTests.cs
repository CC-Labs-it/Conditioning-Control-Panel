using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1493: the compositor subliminal card (every flash that is not over a video) drew in
/// a hard-coded Arial, while the per-screen windows used over a video honour the Studio font pick.
/// The card face now comes from <see cref="SubliminalLayer.ResolveFace"/>.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class SubliminalLayerFontTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Arial")]
    [InlineData("No Such Font 1493")]
    public void EmptyOrMissingFont_FallsBackToArial(string? name)
        => Assert.Equal("Arial", SubliminalLayer.ResolveFace(name).FamilyName);

    [Theory]
    [InlineData("Times New Roman")]
    [InlineData("Consolas")]
    public void InstalledFont_IsTheCardFace(string name)
        => Assert.Equal(name, SubliminalLayer.ResolveFace(name).FamilyName);

    [Fact]
    public void CommaChain_UsesTheFirstFamilyOnly()
        => Assert.Equal("Consolas", SubliminalLayer.ResolveFace("Consolas, Arial").FamilyName);
}
