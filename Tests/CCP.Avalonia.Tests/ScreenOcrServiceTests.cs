using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ScreenOcrServiceTests
{
    private static readonly ScreenOcrService.Word Own = new("own", new Rectangle(10, 10, 10, 10));
    private static readonly ScreenOcrService.Word Other = new("other", new Rectangle(100, 100, 10, 10));

    [Fact]
    public async Task SwitchesAccessIntervalAndOwnWindowFiltering()
    {
        var access = true;
        var scans = 0;
        var deliveries = new List<IReadOnlyList<ScreenOcrService.Word>>();
        using var service = new ScreenOcrService(_ => { scans++; return Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>([Own, Other]); },
            () => access, () => [new Rectangle(0, 0, 15, 15)], words => { deliveries.Add(words); return false; });
        service.Sync(false, true, 60000); await service.TickAsync(); Assert.Equal(0, scans);
        service.Sync(true, false, 60000); await service.TickAsync(); Assert.Equal(0, scans);
        access = false; service.Sync(true, true, 60000); await service.TickAsync(); Assert.Equal(0, scans);
        access = true; service.Sync(true, true, 60000); service.Sync(true, true, 90000);
        Assert.Equal(90000, service.IntervalMs);
        await service.TickAsync();
        Assert.Equal(Other, Assert.Single(Assert.Single(deliveries)));
        service.Sync(false, true, 90000); await service.TickAsync(); Assert.Equal(1, scans);
    }

    [Fact]
    public async Task IntervalChangeReschedulesTheTimer()
    {
        var ticked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new ScreenOcrService(_ => { ticked.TrySetResult(); return Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>([]); },
            () => true, () => [], _ => false);
        service.Sync(true, true, 60000);
        service.Sync(true, true, 10);
        await ticked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConfirmationIsBoundedAndScansDoNotOverlap()
    {
        var scans = 0;
        var pending = new TaskCompletionSource<IReadOnlyList<ScreenOcrService.Word>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new ScreenOcrService(_ => { scans++; return scans == 1 ? pending.Task : Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>([Other]); },
            () => true, () => [], _ => true, (_, _) => Task.CompletedTask);
        service.Sync(true, true, 60000);
        var first = service.TickAsync(); await service.TickAsync(); Assert.Equal(1, scans);
        pending.SetResult([Other]); await first;
        Assert.Equal(4, scans);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InFlightResultsAreNotDeliveredAfterStopOrAccessLoss(bool stop)
    {
        var access = true;
        var delivered = 0;
        var pending = new TaskCompletionSource<IReadOnlyList<ScreenOcrService.Word>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new ScreenOcrService(_ => pending.Task, () => access, () => [], _ => { delivered++; return false; });
        service.Sync(true, true, 60000);
        var tick = service.TickAsync();
        if (stop) service.Stop(); else access = false;
        pending.SetResult([Other]); await tick;
        Assert.Equal(0, delivered);
        access = true; await service.TickAsync(); Assert.Equal(0, delivered);
    }
}
