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
    private static Task<IReadOnlyList<ScreenOcrService.Word>> Words(params ScreenOcrService.Word[] words) =>
        Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>(words);

    [Fact]
    public async Task SwitchesAccessAndInterval()
    {
        var access = true;
        var scans = 0;
        var deliveries = new List<IReadOnlyList<ScreenOcrService.Word>>();
        using var service = new ScreenOcrService(_ => { scans++; return Words(Own, Other); },
            () => access, (words, _) => { deliveries.Add(words); return Task.FromResult(false); });
        service.Sync(false, true, 60000); await service.TickAsync(); Assert.Equal(0, scans);
        service.Sync(true, false, 60000); await service.TickAsync(); Assert.Equal(0, scans);
        access = false; service.Sync(true, true, 60000); await service.TickAsync(); Assert.Equal(0, scans);
        access = true; service.Sync(true, true, 90000);   // regained access resumes without a restart
        Assert.Equal(90000, service.IntervalMs);
        await service.TickAsync();
        Assert.Equal(2, Assert.Single(deliveries).Count);
        service.Sync(false, true, 90000); await service.TickAsync(); Assert.Equal(1, scans);
    }

    [Fact]
    public void OwnWindowFilterDropsOnlyWordsInsideTheApp()
    {
        Assert.Equal(Other, Assert.Single(KeywordTriggerHead.WithoutOwnWindows([Own, Other], [new Rectangle(0, 0, 15, 15)])));
        Assert.Equal(2, KeywordTriggerHead.WithoutOwnWindows([Own, Other], []).Count);
    }

    [Fact]
    public async Task IntervalChangeReschedulesTheTimer()
    {
        var ticked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new ScreenOcrService(_ => { ticked.TrySetResult(); return Words(); },
            () => true, (_, _) => Task.FromResult(false));
        service.Sync(true, true, 60000);
        service.Sync(true, true, 10);
        await ticked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConfirmationIsBoundedAndScansDoNotOverlap()
    {
        var scans = 0;
        var pending = new TaskCompletionSource<IReadOnlyList<ScreenOcrService.Word>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new ScreenOcrService(_ => { scans++; return scans == 1 ? pending.Task : Words(Other); },
            () => true, (_, _) => Task.FromResult(true), (_, _) => Task.CompletedTask);
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
        using var service = new ScreenOcrService(_ => pending.Task, () => access, (_, _) => { delivered++; return Task.FromResult(false); });
        service.Sync(true, true, 60000);
        var tick = service.TickAsync();
        if (stop) service.Stop(); else access = false;
        pending.SetResult([Other]); await tick;
        Assert.Equal(0, delivered);
        access = true; await service.TickAsync();
        Assert.Equal(stop ? 0 : 1, delivered);   // a stop needs a Sync; a lapse ends at the regain
    }
}
