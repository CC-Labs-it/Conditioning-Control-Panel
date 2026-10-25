using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BrowserAudioSyncTests
{
    [Theory]
    [InlineData("{\"duration\":12.5}", 12.5)]
    [InlineData("{\"duration\":0}", null)]
    [InlineData("{\"duration\":90000}", null)]
    public void DurationUsesWpfBounds(string message, double? expected) =>
        Assert.Equal(expected, BrowserAudioSyncService.ParseReportedDuration(message)?.TotalSeconds);

    [Fact]
    public async Task DisabledOrDisconnectedUnblocksVideoWithoutDecoding()
    {
        using var haptics = new HapticService(new HapticSettings());
        using var sync = new BrowserAudioSyncService(haptics, new AudioSyncSettings { Enabled = true },
            (_, _, _, _) => throw new Exception("Must not decode"));
        var completed = 0;
        sync.ProcessingCompleted += (_, _) => completed++;
        await sync.OnVideoDetectedAsync("https://example.invalid/video.mp4");
        Assert.Equal(1, completed);
        Assert.False(sync.IsProcessing);
    }

    [Fact]
    public async Task FirstChunkBecomesReadyAndResetSuppressesPendingCompletion()
    {
        var settings = new HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        foreach (var provider in new[] { "lovense", "buttplug" }) settings.V2.Provider(provider).Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        settings.AudioSync.Enabled = true;
        using var haptics = new HapticService(settings);
        Assert.True(await haptics.ConnectAsync());
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var sync = new BrowserAudioSyncService(haptics, settings.AudioSync, (_, _, _, ct) =>
        {
            entered.Set();
            release.Wait(ct);
            return Enumerable.Range(0, 44100).Select(i => (float)Math.Sin(i * .1)).ToArray();
        });
        var completed = 0;
        sync.ProcessingCompleted += (_, _) => completed++;
        async Task Serve()
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            await stream.ReadAsync(buffer);
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1\r\nConnection: close\r\n\r\nx"));
        }
        try
        {
            var serve = Serve();
            var pending = Task.Run(() => sync.OnVideoDetectedAsync($"http://127.0.0.1:{port}/video.mp4", TimeSpan.FromSeconds(1)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            await serve;
            Assert.True(sync.IsReadyToPlay);
            Assert.Equal(1, completed);
            sync.Reset();
            entered.Reset(); release.Reset();
            serve = Serve();
            pending = Task.Run(() => sync.OnVideoDetectedAsync($"http://127.0.0.1:{port}/video.mp4", TimeSpan.FromSeconds(1)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            sync.Reset();
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            await serve;
            Assert.Equal(1, completed);
            Assert.False(sync.IsReadyToPlay);
            Assert.False(sync.IsProcessing);
        }
        finally { release.Set(); listener.Stop(); }
    }
}
