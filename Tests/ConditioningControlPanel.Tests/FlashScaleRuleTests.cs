using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// A flash asked for at a named size drew at the player's Image Scale instead: the random-pick
/// burst computed the size and never passed it to the loader. The Back Room's "medium, whatever the
/// user's own slider says" (BackRoomFxPlan.FlashSize), a Flash effect bubble's intensity size and the
/// AI's flash_image size were all ignored.
/// </summary>
public sealed class FlashScaleRuleTests
{
    [Theory]
    [InlineData(100, 250, 1.0)]    // the Back Room's 100 % beats a 250 % slider
    [InlineData(68, 100, 0.68)]    // an effect bubble's intensity size
    [InlineData(150, 50, 1.5)]
    public void ANamedSize_Wins(int size, int userScale, double expected)
        => Assert.Equal(expected, FlashScaleRule.Resolve(size, userScale), 6);

    [Theory]
    [InlineData(null, 180, 1.8)]
    [InlineData(0, 180, 1.8)]      // an AI command that left the field out deserializes as 0
    [InlineData(-5, 120, 1.2)]
    public void NoSize_UsesThePlayersScale(int? size, int userScale, double expected)
        => Assert.Equal(expected, FlashScaleRule.Resolve(size, userScale), 6);

    [Fact]
    public void TheBurstLoader_IsHandedTheResolvedScale()
    {
        var src = File.ReadAllText(Path.Combine(SourceRoot(), "Services", "Flash", "FlashService.cs"));
        int start = src.IndexOf("private async void LoadAndShowImages(", StringComparison.Ordinal);
        Assert.True(start >= 0, "LoadAndShowImages not found");
        int end = src.IndexOf("private JackpotRemixDirector EnsureRemixDirector()", start, StringComparison.Ordinal);
        Assert.True(end > start, "LoadAndShowImages end not found");
        var body = src.Substring(start, end - start);

        Assert.Contains("var scale = FlashScaleRule.Resolve(size, settings.ImageScale);", body);
        Assert.Contains("LoadImagesUntilAsync(targetCount, scale: scale)", body);
        Assert.Contains("GetChaosImagePaths(targetCount + BurstSpareCandidates), scale)", body);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("ConditioningControlPanel project not found above " + AppContext.BaseDirectory);
    }
}
