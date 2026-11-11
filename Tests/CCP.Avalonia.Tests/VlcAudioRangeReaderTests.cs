using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class VlcAudioRangeReaderTests
{
    [Fact]
    public void ReadsMidFileRangeAsMonoSamples()
    {
        _ = new LibVlcAudio("--aout=dummy");
        var samples = VlcAudioRangeReader.Read(ToneWav.Tone(440, 4000), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), CancellationToken.None);
        Assert.InRange(samples.Length, VlcAudioRangeReader.SampleRate - 4096, VlcAudioRangeReader.SampleRate);
    }

    [Fact]
    public void MidFileRangeIsNotSilent()
    {
        _ = new LibVlcAudio("--aout=dummy");
        // ToneWav writes 22,050 Hz mono PCM16. Silence its first second so this also proves the seek.
        var wav = File.ReadAllBytes(ToneWav.Tone(440, 4000));
        Array.Clear(wav, 44, 22050 * 2);
        var path = Path.Combine(Path.GetTempPath(), "ccp-sync-test-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, wav);
            var samples = VlcAudioRangeReader.Read(path, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), CancellationToken.None);
            Assert.True(samples.Any(s => Math.Abs(s) > 0.1));
        }
        finally { File.Delete(path); }
    }

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
