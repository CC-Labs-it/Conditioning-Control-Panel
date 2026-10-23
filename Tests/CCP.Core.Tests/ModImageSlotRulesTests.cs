using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ModImageSlotRulesTests
{
    [Theory]
    [InlineData("art.png", "picture.PNG", true)]
    [InlineData("art.jpg", "picture.jpeg", true)]
    [InlineData("preview", "picture.webp", true)]
    [InlineData("art.png", "picture.jpg", false)]
    [InlineData("spiral.gif", "picture.png", false)]
    [InlineData("art.png", "picture", false)]
    public void FileFormatMustMatchThePackedSlotName(string slot, string file, bool accepted) =>
        Assert.Equal(accepted, ModImageSlotRules.DescribeExtensionMismatch(slot, file) == null);

    [Fact]
    public void FourMegabytesIsAcceptedAndOnlyLargerFilesNeedConfirmation()
    {
        Assert.False(ModImageSlotRules.IsOversized(4L * 1024 * 1024));
        Assert.True(ModImageSlotRules.IsOversized(4L * 1024 * 1024 + 1));
        Assert.Equal("6.0 MB", ModImageSlotRules.FormatSize(6L * 1024 * 1024));
    }
}
