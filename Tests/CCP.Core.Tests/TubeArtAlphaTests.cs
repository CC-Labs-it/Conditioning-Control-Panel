using ConditioningControlPanel.AvatarTubeLayout;
using Xunit;

namespace CCP.Core.Tests;

public sealed class TubeArtAlphaTests
{
    [Fact]
    public void ClearsEveryHazeAlphaButPreservesZeroAndVisiblePixels()
    {
        var pixels = new byte[256 * 4];
        for (int a = 0; a < 256; a++)
        {
            pixels[a * 4] = 1; pixels[a * 4 + 1] = 2;
            pixels[a * 4 + 2] = 3; pixels[a * 4 + 3] = (byte)a;
        }
        var original = (byte[])pixels.Clone();
        Assert.Equal(8, TubeArtAlpha.Clean(pixels));
        for (int a = 0; a < 256; a++)
            for (int c = 0; c < 4; c++)
                Assert.Equal(a is >= 1 and <= 8 ? (byte)0 : original[a * 4 + c], pixels[a * 4 + c]);
        Assert.Equal(0, TubeArtAlpha.Clean(pixels));
    }

    [Fact]
    public void HonorsCustomFloorAndEmptyInputs()
    {
        Assert.Equal(0, TubeArtAlpha.Clean(null));
        Assert.Equal(0, TubeArtAlpha.Clean(new byte[0]));
        byte[] pixels = { 1, 1, 1, 9, 2, 2, 2, 10, 3 };
        Assert.Equal(1, TubeArtAlpha.Clean(pixels, 9));
        Assert.Equal(new byte[] { 0, 0, 0, 0, 2, 2, 2, 10, 3 }, pixels);
    }
}
