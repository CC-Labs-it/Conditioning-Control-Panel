using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BrowserAudioSyncTests
{
    [Theory]
    [InlineData("{\"duration\":12.5}", 12.5)]
    [InlineData("{\"duration\":0}", null)]
    [InlineData("{\"duration\":90000}", null)]
    [InlineData("{\"duration\":86400}", 86400d)]
    [InlineData("{\"duration\":null}", null)]
    [InlineData("{\"duration\":-1}", null)]
    [InlineData("{}", null)]
    public void DurationUsesWpfBounds(string message, double? expected) =>
        Assert.Equal(expected, BrowserAudioSyncService.ParseReportedDuration(message)?.TotalSeconds);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisabledOrDisconnectedUnblocksVideoWithoutDecoding(bool enabled)
    {
        using var haptics = new HapticService(new HapticSettings());
        using var sync = new BrowserAudioSyncService(haptics, new AudioSyncSettings { Enabled = enabled },
            (_, _, _, _) => throw new Exception("Must not decode"));
        var completed = 0;
        sync.ProcessingCompleted += (_, _) => completed++;
        await sync.OnVideoDetectedAsync("https://example.invalid/video.mp4");
        Assert.Equal(1, completed);
        Assert.False(sync.IsProcessing);
        sync.Dispose();
        await sync.OnVideoDetectedAsync("https://example.invalid/video.mp4");
        Assert.Equal(1, completed);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(0.01, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(0.5, 0.04, 0.02)]
    [InlineData(0.5, 1, 0.54)]
    public void LivePowerPreservesQuietAndWpfDeviceFloor(float track, double power, float expected)
    {
        using var haptics = new HapticService(new HapticSettings());
        using var sync = new BrowserAudioSyncService(haptics, new AudioSyncSettings { LiveIntensity = power },
            (_, _, _, _) => throw new Exception("Must not decode"));
        Assert.Equal(expected, sync.ApplyLivePower(track), 5);
    }

    [Fact]
    public void LookAheadUsesLiveOffsetAndResyncsEveryFiveSeconds()
    {
        using var haptics = new HapticService(new HapticSettings());
        var settings = new AudioSyncSettings { ManualLatencyOffsetMs = 100 };
        using var sync = new BrowserAudioSyncService(haptics, settings, (_, _, _, _) => Array.Empty<float>());
        var time = TimeSpan.FromSeconds(10);
        var now = new DateTime(2026, 1, 1);
        Assert.Equal(time, sync.GetLookAhead(time, now));
        Assert.Equal(time + TimeSpan.FromMilliseconds(400 + haptics.ProviderLatencyMs), sync.GetLookAhead(time, now.AddSeconds(1)));
        settings.ManualLatencyOffsetMs = -100;
        Assert.Equal(time + TimeSpan.FromMilliseconds(200 + haptics.ProviderLatencyMs), sync.GetLookAhead(time, now.AddSeconds(4)));
        Assert.Equal(time, sync.GetLookAhead(time, now.AddSeconds(5)));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    public async Task FirstChunkBecomesReadyAndResetSuppressesPendingCompletion(bool bands, bool fail, int shutdown)
    {
        var settings = new HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        foreach (var provider in new[] { "lovense", "buttplug" }) settings.V2.Provider(provider).Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        settings.AudioSync.Enabled = true;
        settings.AudioSync.BandSplit = bands;
        using var haptics = new HapticService(settings);
        Assert.True(await haptics.ConnectAsync());
        Assert.True(haptics.MaxVibrateMotors >= 2);
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        CancellationToken decodingToken = default;
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sync = new BrowserAudioSyncService(haptics, settings.AudioSync, (_, _, _, ct) =>
        {
            decodingToken = ct;
            entered.Set();
            try { release.Wait(ct); }
            catch (OperationCanceledException) { cancelled.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); throw; }
            if (fail) return Array.Empty<float>();
            return Enumerable.Range(0, 44100).Select(i => (float)Math.Sin(i * .1)).ToArray();
        });
        var completed = 0;
        var started = 0;
        var progress = 0;
        var errors = 0;
        sync.ProcessingStarted += (_, _) => started++;
        sync.ProcessingProgress += (_, _) => progress++;
        sync.Error += (_, _) => errors++;
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
            await sync.OnVideoDetectedAsync("https://example.invalid/page.html");
            Assert.Equal(1, completed);
            Assert.Equal(0, started);
            Assert.False(entered.IsSet);
            completed = 0;
            var serve = Serve();
            var pending = Task.Run(() => sync.OnVideoDetectedAsync($"http://127.0.0.1:{port}/video.mp4", TimeSpan.FromSeconds(1)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            await serve;
            Assert.Equal(1, started);
            Assert.True(progress > 0);
            if (fail)
            {
                Assert.True(errors > 0);
                Assert.Equal(1, completed);
                Assert.False(sync.IsReadyToPlay);
                Assert.False(sync.IsProcessing);
                return;
            }
            Assert.True(sync.IsReadyToPlay);
            Assert.Equal(1, completed);
            sync.OnPlaybackStateUpdate(0, false);
            Assert.True(haptics.Mixer.GetLayer(HapticLayer.AudioSync) > 0);
            var splits = (double[]?[])typeof(HapticMixer).GetField("_layerSplits",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(haptics.Mixer)!;
            if (bands) Assert.Equal(2, Assert.IsType<double[]>(splits[(int)HapticLayer.AudioSync]).Length);
            else Assert.Null(splits[(int)HapticLayer.AudioSync]);
            sync.OnPlaybackStateUpdate(0, true);
            Assert.Equal(0, haptics.Mixer.GetLayer(HapticLayer.AudioSync));
            sync.OnPlaybackStateUpdate(0, false);
            Assert.True(haptics.Mixer.GetLayer(HapticLayer.AudioSync) > 0);
            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, -1, 90000 })
            {
                sync.OnPlaybackStateUpdate(bad, false);
                sync.OnVideoSeek(bad);
            }
            Assert.False(sync.IsWaitingForChunk);
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = -1;
            sync.ChunkLoadingRequired += (_, index) => loading = index;
            sync.ChunkLoadingCompleted += (_, _) => loaded.TrySetResult();
            entered.Reset(); release.Reset();
            sync.OnVideoSeek(settings.AudioSync.ChunkDurationSeconds + 1);
            Assert.Equal(1, loading);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            Assert.True(sync.IsWaitingForChunk);
            release.Set();
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(sync.IsWaitingForChunk);
            sync.OnVideoEnded();
            Assert.Equal(0, haptics.Mixer.GetLayer(HapticLayer.AudioSync));
            sync.OnPlaybackStateUpdate(0, false);
            Assert.True(haptics.Mixer.GetLayer(HapticLayer.AudioSync) > 0);
            Assert.Equal(1, started); // Replay uses cached data without detection or decoding.
            entered.Reset(); release.Reset();
            var loads = 0;
            sync.ChunkLoadingCompleted += (_, _) => loads++;
            sync.OnVideoSeek(settings.AudioSync.ChunkDurationSeconds * 2 + 1);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(15)));
            Assert.True(decodingToken.CanBeCanceled);
            var work = (Task)typeof(BrowserAudioSyncService).GetField("_pending",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(sync)!;
            var manager = typeof(BrowserAudioSyncService).GetField("_chunkManager",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(sync)!;
            var gate = (SemaphoreSlim)manager.GetType().GetField("_processingSemaphore",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manager)!;
            if (shutdown == 0) sync.StopSync();
            else if (shutdown == 1) sync.Reset();
            else sync.Dispose();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(work.IsCompleted);
            Assert.NotNull(gate.AvailableWaitHandle); // Reset/dispose must wait for decoder unwind.
            release.Set();
            await work.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, loads);
            Assert.False(sync.IsWaitingForChunk);
            sync.OnPlaybackStateUpdate(0, false);
            Assert.Equal(0, haptics.Mixer.GetLayer(HapticLayer.AudioSync));
            sync.Reset();
            if (shutdown == 2) return;
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
            Assert.Equal(0, errors);
        }
        finally { release.Set(); listener.Stop(); }
    }
}
