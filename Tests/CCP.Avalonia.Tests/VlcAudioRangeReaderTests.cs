using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Avalonia.Views.Windows;
using CCP.Tests.Shared;
using LibVLCSharp.Shared;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VlcAudioRangeReaderTests
{
    private static void WithDecoder(Action<LibVLC> run)
    {
        var shared = LibVlcAudio.Shared;
        LibVlcAudio.InitializeNative();
        var decoder = new LibVLC("--aout=dummy", "--quiet");
        try { run(decoder); }
        finally
        {
            decoder.Dispose();
            Assert.Multiple(
                () => Assert.Equal(IntPtr.Zero, decoder.NativeReference),
                () => Assert.Same(shared, LibVlcAudio.Shared));
        }
    }

    [Fact]
    public void ReadsMidFileRangeAsMonoSamples() => WithDecoder(decoder =>
    {
        var samples = VlcAudioRangeReader.Read(ToneWav.Tone(440, 4000), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), CancellationToken.None, decoder);
        Assert.InRange(samples.Length, VlcAudioRangeReader.SampleRate - 4096, VlcAudioRangeReader.SampleRate);
    });

    [Fact]
    public void MidFileRangeIsNotSilent() => WithDecoder(decoder =>
    {
        // ToneWav writes 22,050 Hz mono PCM16. Silence its first second so this also proves the seek.
        var wav = File.ReadAllBytes(ToneWav.Tone(440, 4000));
        Array.Clear(wav, 44, 22050 * 2);
        var path = Path.Combine(Path.GetTempPath(), "ccp-sync-test-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, wav);
            var samples = VlcAudioRangeReader.Read(path, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), CancellationToken.None, decoder);
            Assert.True(samples.Any(s => Math.Abs(s) > 0.1));
        }
        finally { File.Delete(path); }
    });

    [Fact]
    public void MissingFileIsEmpty()
    {
        Assert.Empty(VlcAudioRangeReader.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), TimeSpan.Zero, TimeSpan.FromSeconds(1), CancellationToken.None));
    }

    [Fact]
    public void CancellationThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => VlcAudioRangeReader.Read("missing", TimeSpan.Zero, TimeSpan.FromSeconds(1), cts.Token));
    }

    [Fact]
    public async Task PanicStopsAudioSyncAndCancelsPendingPreparation()
    {
        var surface = Assert.Single(PanicSurfaces.All, entry => entry.Id == "audio-sync");
        AudioSyncHost.Stop();
        var settings = new HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        foreach (var provider in new[] { "lovense", "buttplug" }) settings.V2.Provider(provider).Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        settings.AudioSync.Enabled = true;
        using var haptics = new HapticService(settings);
        Assert.True(await haptics.ConnectAsync());
        using var server = new FakeServer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Handle = async context =>
        {
            entered.TrySetResult();
            await release.Task;
            await FakeServer.Send(context, new byte[] { 1 });
        };
        AudioSyncHost.Start(haptics, settings.AudioSync);
        var service = Assert.IsType<BrowserAudioSyncService>(AudioSyncHost.Service);
        var completed = 0;
        service.ProcessingCompleted += (_, _) => completed++;
        var pending = Task.Run(() => service.OnVideoDetectedAsync(server.Prefix + "video.mp4", TimeSpan.FromSeconds(1)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(service.IsProcessing);
            haptics.Mixer.SetLayer(HapticLayer.AudioSync, 0.8);
            Assert.True(haptics.Mixer.GetLayer(HapticLayer.AudioSync) > 0);
            surface.Stop(null);
            await pending.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(service.IsProcessing);
            Assert.Equal(0, completed);
            Assert.Equal(0, haptics.Mixer.GetLayer(HapticLayer.AudioSync));
            Assert.Same(service, AudioSyncHost.Service);
        }
        finally
        {
            release.TrySetResult();
            AudioSyncHost.Stop();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(15)); } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public void HostCreatesOneService()
    {
        AudioSyncHost.Stop();
        var settings = new HapticSettings();
        using var haptics = new HapticService(settings);
        try
        {
            AudioSyncHost.Start(haptics, settings.AudioSync);
            var service = Assert.IsType<BrowserAudioSyncService>(AudioSyncHost.Service);
            AudioSyncHost.Start(haptics, settings.AudioSync);
            Assert.Same(service, AudioSyncHost.Service);
        }
        finally { AudioSyncHost.Stop(); }
    }
    [Fact]
    public async Task HostDisposesService()
    {
        AudioSyncHost.Stop();
        var settings = new HapticSettings();
        using var haptics = new HapticService(settings);
        try
        {
            AudioSyncHost.Start(haptics, settings.AudioSync);
            var service = Assert.IsType<BrowserAudioSyncService>(AudioSyncHost.Service);
            int completed = 0;
            service.ProcessingCompleted += (_, _) => completed++;
            await service.OnVideoDetectedAsync("https://example.com/test.mp4");
            Assert.Equal(1, completed);
            AudioSyncHost.Stop();
            Assert.Null(AudioSyncHost.Service);
            await service.OnVideoDetectedAsync("https://example.com/test.mp4");
            Assert.Equal(1, completed);
        }
        finally { AudioSyncHost.Stop(); }
    }
}
