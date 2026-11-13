using System;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net;
using ConditioningControlPanel.Services.Race;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RaceEngineDownloadTests
{
    [Theory]
    [InlineData("network", RaceEngineFetchResult.Network)]
    [InlineData("timeout", RaceEngineFetchResult.Network)]
    [InlineData("checksum", RaceEngineFetchResult.Checksum)]
    [InlineData("storage", RaceEngineFetchResult.Storage)]
    [InlineData("cpu", RaceEngineFetchResult.Unsupported)]
    public async Task AdapterMapsFailures(string error, RaceEngineFetchResult expected)
    {
        Exception ex = error switch { "network" => new HttpRequestException(), "timeout" => new TimeoutException(),
            "checksum" => new InvalidDataException(), "storage" => new IOException(), _ => new PlatformNotSupportedException() };
        var adapter = new RaceEngineFetchAdapter((_, _) => Task.FromException<string>(ex), () => false);
        Assert.Equal(expected, await adapter.FetchAsync(new Capture(_ => { }), CancellationToken.None));
    }
    private sealed class Capture(Action<double> report) : IProgress<double> { public void Report(double value) => report(value); }
    [Fact]
    public async Task AdapterReportsPercentAndUnknownLength()
    {
        var values = new System.Collections.Generic.List<double>();
        var adapter = new RaceEngineFetchAdapter((progress, _) => { progress.Report(new(1, 4)); progress.Report(new(4, 4));
            progress.Report(new(1, null)); return Task.FromResult("ok"); }, () => true);
        Assert.True(adapter.IsInstalled);
        Assert.Equal(RaceEngineFetchResult.Installed, await adapter.FetchAsync(new Capture(values.Add), CancellationToken.None));
        Assert.Equal(25, values[0]); Assert.Equal(100, values[1]); Assert.True(double.IsNaN(values[2]));
    }
    [Fact]
    public void SeedRequiresLinuxAndShell()
    {
        Assert.Null(RaceEngineFetchAdapter.Seed(false, true));
        Assert.Null(RaceEngineFetchAdapter.Seed(true, false));
        Assert.NotNull(RaceEngineFetchAdapter.Seed(true, true));
    }
    [Fact]
    public async Task AdapterPanicCancelsFetch()
    {
        var started = new TaskCompletionSource();
        var adapter = new RaceEngineFetchAdapter((_, cancel) => RaceEngineDownload.RunAsync(async token => {
            started.SetResult(); await Task.Delay(Timeout.Infinite, token); return "";
        }, cancel), () => false);
        var pending = adapter.FetchAsync(new Capture(_ => { }), CancellationToken.None);
        await started.Task;
        RaceEngineDownload.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }
    private sealed class BytesHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[1024]) });
    }
    [Fact]
    public async Task PanicCleansAdapterStaging()
    {
        var data = Path.Combine(Path.GetTempPath(), "ccp-adapter-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var http = new HttpClient(new BytesHandler());
            var pin = new RaceEnginePackage("45.1.0", "linux-x64", new Uri("https://example.invalid"), new string('0', 64));
            var adapter = new RaceEngineFetchAdapter((progress, cancel) => RaceEngineDownload.RunAsync(
                token => new RaceEngineFetcher(http, data).FetchAsync(pin, progress, token), cancel), () => false);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.FetchAsync(
                new Capture(_ => RaceEngineDownload.Cancel()), CancellationToken.None));
            Assert.Empty(Directory.GetDirectories(Path.Combine(data, "engines"), ".fetch-*"));
        }
        finally { RaceEngineDownload.Cancel(); if (Directory.Exists(data)) Directory.Delete(data, true); }
    }
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
