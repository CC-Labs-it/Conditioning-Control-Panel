using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SeasonRolloverTests
{
    [Fact]
    public Task OpenAppRecordsTheUtcDayOnLocalRollover() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = new AppSettings();
        var now = new DateTimeOffset(2026, 11, 11, 23, 59, 0, TimeSpan.FromHours(2));
        var recorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = 0;
        var timer = App.StartSeasonRollover(utc =>
        {
            SeasonActivity.MarkActiveToday(s, utc, () => saves++);
            recorded.TrySetResult();
        }, () => now, TimeSpan.FromMilliseconds(10));
        try
        {
            await Task.Delay(50);
            Assert.Empty(s.SeasonActiveDays);
            now = now.AddMinutes(2); // local midnight, UTC still on the preceding day
            await recorded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "2026-11-11" }, s.SeasonActiveDays);
            await Task.Delay(50);
            Assert.Equal(1, saves);
        }
        finally { timer.Stop(); }
    });
}
