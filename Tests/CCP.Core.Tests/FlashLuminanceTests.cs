using System.Linq;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Core.Tests;

public class FlashLuminanceTests
{
    [Fact]
    public void AverageUsesRec601AndAlphaAcrossTheSample()
    {
        var pixels = Enumerable.Repeat(new byte[] { 0, 0, 255, 255 }, 32)
            .Concat(Enumerable.Repeat(new byte[] { 255, 255, 255, 255 }, 32))
            .SelectMany(x => x).ToArray();
        Assert.Equal((0.299 + 1) / 2, FlashLuminance.Average(pixels, true), 6);
        Assert.Equal(0, FlashLuminance.Average(new byte[4], true));
        Assert.Equal(0.299, FlashLuminance.Average(new byte[] { 128, 0, 0, 128 }, false), 6);
    }
}
