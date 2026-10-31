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
}
