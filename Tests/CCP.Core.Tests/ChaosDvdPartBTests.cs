using System;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
namespace CCP.Core.Tests;

public sealed class ChaosDvdPartBTests
{
    // WPF ChaosDvdOverlay.cs:322-331,392-398.
    [Fact]
    public void RabbitSplitIsArmedOnceExtendsAtCapAndRejectsRetiringAndOrdinaryLogos()
    {
        var flock = new ChaosDvdFlock(new Random(1));
        var toy = Assert.Single(flock.Launch(5, 1, 1));
        Assert.Null(flock.TouchRabbit(toy));
        var thought = Assert.Single(flock.Launch(5, 1, 1, text: "x", splitOnRabbit: true));
        thought.FadeOutSec = 0;
        Assert.Null(flock.TouchRabbit(thought));
        thought.FadeOutSec = -1;
        for (int i = 0; i < 7; i++) flock.Launch(5, 1, 1, text: "x");
        Assert.Null(flock.TouchRabbit(thought));
        Assert.Equal(7, thought.RemainingSec);
        Assert.True(thought.SplitSpent);
        Assert.Null(flock.TouchRabbit(thought));
        Assert.Equal(7, thought.RemainingSec);
        Assert.Equal(8, flock.Active.Count(l => l.IsThought));
    }
}
