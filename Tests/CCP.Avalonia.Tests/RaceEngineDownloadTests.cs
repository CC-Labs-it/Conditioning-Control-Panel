using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RaceEngineDownloadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PanicCancelsInFlightFetchAndReleasesOwner(bool watchdog)
    {
        var started = new TaskCompletionSource();
        var pending = RaceEngineDownload.RunAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return "unreachable";
        }, CancellationToken.None);
        await started.Task;
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RaceEngineDownload.RunAsync(_ => Task.FromResult("second"), CancellationToken.None));
            if (watchdog) PanicWatchdog.Teardown();
            else PanicSurfaces.All.Single(s => s.Id == "games").Stop(null);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { RaceEngineDownload.Cancel(); }
        Assert.Equal("retry", await RaceEngineDownload.RunAsync(_ => Task.FromResult("retry"), CancellationToken.None));
    }

    [Fact]
    public async Task CardCancellationReachesFetch()
    {
        using var cancel = new CancellationTokenSource();
        var pending = RaceEngineDownload.RunAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return "unreachable";
        }, cancel.Token);
        cancel.Cancel();
        try { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { RaceEngineDownload.Cancel(); }
    }
}
