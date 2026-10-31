using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class Sync7CornerGifTests
{
    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    public void StandaloneNeedsRunningEngine(bool enabled, bool sessionOverlay, bool running, bool expected)
        => Assert.Equal(expected, CornerGifPlanner.AllowStandaloneCornerGif(enabled, sessionOverlay, running));
}
