using System;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Dares;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DareMeterHostTests
{
    private static DareBet Bet(string id, DareKind kind, string target)
    {
        var now = ServerClock.UtcNow;
        return new DareBet(id, DareSide.To, new DareFriend("f", "Fen", null), DareType.Cant,
            new DareWhat(kind, target), DareDeadline.ThreeHours, now.AddHours(1), false,
            new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Sp, 50),
            Array.Empty<DareSeal>(), DareState.Running, null, 0, 0, null, null, null,
            now.AddMinutes(-5), now.AddMinutes(-5), null, null);
    }

    [Fact]
    public void Flash_batches_and_completed_sessions_reach_the_meter() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.Null(DareHead.Service);
        var meter = new DareMeter();
        meter.Track(new[] { Bet("pics", DareKind.Pics, "100"), Bet("session", DareKind.Session, "easy") });
        using var hooks = new DareHead.MeterHooks(meter);
        hooks.Attach();
        FlashOverlay.NoteDareImages(3);
        DareTaps.RaiseSessionFinished(SessionDifficulty.Hard);
        Assert.Equal(3, meter.Reading("pics")?.Count);
        Assert.Equal(1, meter.Reading("session")?.Count);
    });

    [Fact]
    public void A_stop_ignores_later_flash_and_session_events() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.Null(DareHead.Service);
        var meter = new DareMeter();
        meter.Track(new[] { Bet("pics", DareKind.Pics, "100"), Bet("session", DareKind.Session, "easy") });
        using var hooks = new DareHead.MeterHooks(meter);
        hooks.Attach();
        hooks.Suspend();
        FlashOverlay.NoteDareImages(3);
        DareTaps.RaiseSessionFinished(SessionDifficulty.Hard);
        Assert.Equal(0, meter.Reading("pics")?.Count);
        Assert.Equal(0, meter.Reading("session")?.Count);
    });
}
