using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Core.Tests;

public class WebcamMonitorSelectionTests
{
    [Fact]
    public void NamedMonitorReturnsAfterTemporaryFallbackWithoutChangingTheSavedName()
    {
        const string saved = "second";
        Assert.Equal(new WebcamMonitorSelection.Choice(1, false), WebcamMonitorSelection.Select(saved, new[] { "first", "SECOND" }, 0));
        Assert.Equal(new WebcamMonitorSelection.Choice(0, true), WebcamMonitorSelection.Select(saved, new[] { "first" }, 0));
        Assert.Equal(new WebcamMonitorSelection.Choice(0, false), WebcamMonitorSelection.Select(saved, new[] { "second", "first" }, 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Primary")]
    public void LegacyAndPrimaryUseCurrentPrimary(string? saved)
    {
        Assert.Equal(1, WebcamMonitorSelection.Select(saved, new[] { "a", "b" }, 1).Index);
        Assert.Equal(0, WebcamMonitorSelection.Select(saved, new[] { "a", "b" }, -1).Index);
        Assert.Equal(-1, WebcamMonitorSelection.Select(saved, System.Array.Empty<string>(), 0).Index);
    }
}
