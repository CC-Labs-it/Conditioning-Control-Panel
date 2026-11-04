using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using Xunit;
namespace CCP.Core.Tests;
public class FlashGifSharedRulesTests
{
    [Theory]
    [InlineData("ConditioningControlPanel/Services/Flash/FlashService.cs", "FlashGifRules.ScaleFrameDelay")]
    [InlineData("ConditioningControlPanel/Services/Flash/FlashService.cs", "FlashGifRules.DecodeMax")]
    [InlineData("ConditioningControlPanel/Services/PerformanceProfile.cs", "FlashGifRules.MaxDecodeDimension")]
    [InlineData("ConditioningControlPanel/Services/Media/AnimatedWebp.cs", "FlashGifRules.FramePlan.Create")]
    [InlineData("CCP.Avalonia/Views/Overlays/FlashOverlayWindow.cs", "FlashGifRules.ScaleFrameDelay")]
    [InlineData("CCP.Avalonia/Views/Overlays/FlashOverlay.cs", "FlashGifRules.DecodeMax")]
    [InlineData("CCP.Avalonia/Views/Overlays/FlashOverlay.cs", "FlashGifRules.FramePlan.Create")]
    public void Both_heads_use_shared_rules(string path, string call)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "CCP.Core"))) root = root.Parent;
        Assert.NotNull(root);
        Assert.Contains(call, File.ReadAllText(Path.Combine(root!.FullName, path)));
    }
    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(0.1, 400)]
    public void Speed_handles_invalid_and_below_minimum_multipliers(double multiplier, double expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), FlashGifRules.ScaleFrameDelay(TimeSpan.FromMilliseconds(100), multiplier));
    [Fact]
    public void Decode_cap_obeys_wpf_tier_scaling_and_bounds()
    {
        Assert.Equal(1280, FlashGifRules.DecodeMax(PerformanceTier.Performance, 250));
        Assert.Equal(1920, FlashGifRules.DecodeMax(PerformanceTier.Balanced, 250));
        Assert.Equal(2048, FlashGifRules.DecodeMax(PerformanceTier.Quality, 250));
        Assert.Equal(256, FlashGifRules.DecodeMax(PerformanceTier.Performance, 10));
    }
}
