using System;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Core.Tests;

public sealed class LeashSelfCardRulesTests
{
    [Fact]
    public void ChasterBookingDoesNotBecomeAWaitingTask()
    {
        var holder = new LeashPerson("holder", "Juno", null);
        var now = DateTimeOffset.UtcNow;
        var booked = new Punishment("tab", PunishKind.Chaster, 900, null, holder, now, now.AddDays(3));
        var task = new Punishment("lines", PunishKind.Lines, 3, null, holder, now, now.AddDays(3));
        Assert.Equal(0, LeashUiRules.PendingGateCount(new[] { booked }));
        Assert.Equal(1, LeashUiRules.PendingGateCount(new[] { booked, task }));
        Assert.Equal(0, LeashUiRules.PendingGateCount(Array.Empty<Punishment>()));
    }
}
