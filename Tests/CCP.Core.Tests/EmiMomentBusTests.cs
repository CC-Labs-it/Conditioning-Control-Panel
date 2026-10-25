using System;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class EmiMomentBusTests
{
    [Fact]
    public void AwayPublishesButNeverQueuesSpeechAndIgnoresBlankIds()
    {
        var bus = new EmiMomentBus(EmiLineEngine.FromJson("{\"version\":1,\"moments\":{},\"pools\":{}}"));
        int published = 0, spoken = 0;
        bus.MomentFired += (_, m) => { Assert.Equal("ringOpen", m.Id); published++; };
        bus.SpeakingRequested += (_, _) => spoken++;
        bus.Fire(" ", null, true);
        bus.Fire("ringOpen", null, false);
        Assert.Equal(1, published); Assert.Equal(0, spoken);
        bus.Fire("ringOpen", null, true);
        Assert.Equal(2, published); Assert.Equal(1, spoken);
    }

    [Fact]
    public void HoldArmsWhileAwayAndReleaseEndsIt()
    {
        var engine = EmiLineEngine.FromJson("""
            {"version":1,"moments":{"intakeOpened":{"hold":true,"holdUntilReleased":true,"tailMs":0}},"pools":{}}
            """);
        var bus = new EmiMomentBus(engine);
        bus.Fire("intakeOpened", null, false);
        Assert.True(engine.HoldActive);
        bus.ReleaseHold("intakeOpened");
        Assert.False(engine.HoldActive);
    }

    [Fact]
    public void BrokenObserverDoesNotPreventDelivery()
    {
        var bus = new EmiMomentBus(EmiLineEngine.FromJson("{\"version\":1,\"moments\":{},\"pools\":{}}"));
        bool delivered = false;
        bus.MomentFired += (_, _) => throw new InvalidOperationException();
        bus.SpeakingRequested += (_, _) => delivered = true;
        bus.Fire("ringOpen", null, true);
        Assert.True(delivered);
    }
}
