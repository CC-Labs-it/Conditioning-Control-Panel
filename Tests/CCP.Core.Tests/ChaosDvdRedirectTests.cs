using System;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChaosDvdRedirectTests
{
    // WPF ChaosDvdOverlay.cs:305-319 (restack b138 from #3527).
    [Fact]
    public void RedirectCapsSpeedThroughFadeAndRejectsRemovedLogos()
    {
        var flock = new ChaosDvdFlock(new Random(1));
        var logo = Assert.Single(flock.Launch(5, 2, 1));
        logo.Vx = logo.Vy = 0;
        var hue = logo.HueIndex;
        Assert.True(flock.Redirect(logo));
        Assert.Equal((hue + 1) % ChaosDvdFlock.Hues.Length, logo.HueIndex);
        Assert.Equal(ChaosDvdFlock.BaseSpeed * 1.18, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6);
        for (int i = 0; i < 20; i++) Assert.True(flock.Redirect(logo));
        Assert.Equal(ChaosDvdFlock.BaseSpeed * 2.5, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6);
        logo.FadeOutSec = 0;
        Assert.True(flock.Redirect(logo));   // WPF's click handler stays attached through the fade
        flock.Clear();
        Assert.False(flock.Redirect(logo));
    }
}
