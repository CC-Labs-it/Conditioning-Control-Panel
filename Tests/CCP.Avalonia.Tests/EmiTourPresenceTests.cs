using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Tours;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class EmiTourPresenceTests
{
    // WPF EmiTourNarrator.cs:133,141: an already-out or broken desk never blocks a card.
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 1)]
    public async Task SummonGuardsAndFailureLeaveTheTourRunning(bool enabled, bool isOut, int expected)
    {
        var service = new TutorialService();
        int calls = 0;
        using var presence = new EmiTourPresence(service, () => enabled, () => isOut, () =>
        {
            calls++;
            throw new InvalidOperationException("unavailable desk");
        });
        service.Start(TutorialType.ShortWalk);
        await presence.PendingSummon;
        Assert.Equal(expected, calls);
        Assert.True(service.IsActive);
        Assert.Equal("sw-assets", service.CurrentStep!.Id);
        service.Skip();
        presence.Dispose();
        service.Start(TutorialType.ShortWalk);
        await presence.PendingSummon;
        Assert.Equal(expected, calls);
        service.Skip();
    }

    // Restack b189 (#4645): WPF EmiTourNarrator.cs:195 offers the book after either ending, once per ending.
    [Theory]
    [InlineData(true, "tourFinished")]
    [InlineData(false, "tourSkipped")]
    public async Task EndingSchedulesTheBookOfferOnce(bool complete, string why)
    {
        var service = new TutorialService();
        var offers = new System.Collections.Generic.List<string>();
        using var presence = new EmiTourPresence(service, () => true, () => true, () => Task.CompletedTask,
            _ => { }, _ => false, offers.Add);
        service.Start(TutorialType.ShortWalk);
        await presence.PendingSummon;
        Assert.Empty(offers);
        if (complete) while (service.IsActive) service.Next();
        else service.Skip();
        Assert.Equal(new[] { why }, offers);
    }

    // WPF EmiTourNarrator.cs:61-88: replacement detaches the previous service too.
    [Fact]
    public void AttachmentIsIdempotentAndReplacementDisposesTheOldAttachment()
    {
        var first = new TutorialService();
        var second = new TutorialService();
        try
        {
            EmiTourPresence.Attach(first);
            var original = EmiTourPresence.Active;
            EmiTourPresence.Attach(first);
            Assert.Same(original, EmiTourPresence.Active);
            EmiTourPresence.Attach(second);
            Assert.NotSame(original, EmiTourPresence.Active);
            EmiTourPresence.Detach();
            EmiTourPresence.Detach();
            Assert.Null(EmiTourPresence.Active);
        }
        finally { EmiTourPresence.Detach(); }
    }

    // Restack b169 (from #3795): WPF EmiTourNarrator.cs:148-193 moment mapping.
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task NarrationMapsStartStepsAndEnding(bool complete, bool ownPool)
    {
        var service = new TutorialService();
        var moments = new System.Collections.Generic.List<string>();
        using var presence = new EmiTourPresence(service, () => true, () => true, () => Task.CompletedTask,
            moments.Add, _ => ownPool);
        service.Start(TutorialType.ShortWalk);
        await presence.PendingSummon;
        var first = service.CurrentStep!.Id;
        Assert.Equal("tourStarted", moments[0]);
        Assert.Equal(ownPool ? "tour." + first : "tourStep", moments[1]);
        if (complete) while (service.IsActive) service.Next();
        else service.Skip();
        Assert.Equal(complete ? "tourFinished" : "tourSkipped", moments[^1]);
        Assert.Single(moments, m => m is "tourFinished" or "tourSkipped");
    }

    // Summon first, then speak: tourStarted waits for the summon to land.
    [Fact]
    public async Task GreetingWaitsForTheSummon()
    {
        var service = new TutorialService();
        var moments = new System.Collections.Generic.List<string>();
        var summon = new TaskCompletionSource();
        using var presence = new EmiTourPresence(service, () => true, () => false, () => summon.Task, moments.Add);
        var start = service.StartAsync(TutorialType.ShortWalk, () => presence.PendingSummon);
        Assert.Empty(moments);
        summon.SetResult();
        await start;
        Assert.Equal(new[] { "tourStarted", "tourStep" }, moments);
        service.Skip();
    }

    // A throwing desk never blocks the tour; a disposed attachment says nothing on the next one.
    [Fact]
    public async Task NarrationFailureLeavesTheTourAndDisposeSilencesTheNext()
    {
        var service = new TutorialService();
        var moments = new System.Collections.Generic.List<string>();
        var presence = new EmiTourPresence(service, () => true, () => true, () => Task.CompletedTask, m =>
        {
            moments.Add(m);
            throw new InvalidOperationException("desk unavailable");
        });
        service.Start(TutorialType.ShortWalk);
        await presence.PendingSummon;
        Assert.True(service.IsActive);
        service.Skip();
        Assert.Equal("tourSkipped", moments[^1]);
        var count = moments.Count;
        presence.Dispose();
        service.Start(TutorialType.ShortWalk);
        service.Skip();
        Assert.Equal(count, moments.Count);
    }
}
