using System;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
namespace CCP.Core.Tests;
public sealed class ChaosDvdPartBTests {
    // WPF ChaosDvdOverlay.cs:305-331,392-398.
    [Fact]
    public void RedirectCapsSpeedThroughFadeAndRejectsRemovedLogos() { var flock = new ChaosDvdFlock(new Random(1)); var logo = Assert.Single(flock.Launch(5, 2, 1)); logo.Vx = logo.Vy = 0;
        Assert.True(flock.Redirect(logo)); Assert.Equal(230 * 1.18, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6); for (int i = 0; i < 20; i++) Assert.True(flock.Redirect(logo));
        Assert.Equal(230 * 2.5, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6); logo.FadeOutSec = 0; Assert.True(flock.Redirect(logo)); // WPF click handler stays attached through the fade
        flock.Clear(); Assert.False(flock.Redirect(logo)); }
    [Fact]
    public void RabbitSplitIsArmedOnceExtendsAtCapAndRejectsRetiringAndOrdinaryLogos() { var flock = new ChaosDvdFlock(new Random(1)); var toy = Assert.Single(flock.Launch(5, 1, 1));
        Assert.Null(flock.TouchRabbit(toy)); var thought = Assert.Single(flock.Launch(5, 1, 1, text: "x", splitOnRabbit: true)); thought.FadeOutSec = 0; Assert.Null(flock.TouchRabbit(thought));
        thought.FadeOutSec = -1; for (int i = 0; i < 7; i++) flock.Launch(5, 1, 1, text: "x"); Assert.Null(flock.TouchRabbit(thought)); Assert.Equal(7, thought.RemainingSec);
        Assert.True(thought.SplitSpent); Assert.Null(flock.TouchRabbit(thought)); Assert.Equal(7, thought.RemainingSec); Assert.Equal(8, flock.Active.Count(l => l.IsThought)); } }
