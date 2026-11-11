using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF LayeredAudioService.cs:89-121 sets track and master gain before Play.</summary>
public sealed class LayeredAudioStartupTests
{
    [Fact]
    public void ForcedAudioOnlyBedStartsMutedWithItsConfiguredGain()
    {
        var vlc = SilentVlc();
        var file = WriteWav();
        var s = CoreSettings.Current;
        var saved = (s.AudioLayers, s.AudioLayersEnabled, s.AudioLayersMasterVolume, s.MasterVolume);
        LayeredAudio.VlcLayerPlayer? player = null;
        var initial = -1;
        var layers = new LayeredAudio((path, volume) =>
        {
            initial = volume;
            return player = new LayeredAudio.VlcLayerPlayer(vlc, path, initialVolume: volume);
        });
        try
        {
            s.AudioLayersEnabled = false;
            s.AudioLayersMasterVolume = 50;
            s.MasterVolume = 80;
            s.AudioLayers = new() { new AudioLayerTrack { Path = file, Volume = 25 } };
            layers.Start(ignoreMasterToggle: true); // WPF StartStop.cs:365-367
            Assert.True(layers.IsPlaying);
            Assert.Equal(46, initial); // cbrt(0.25 * 0.5 * 0.8)
            Assert.NotNull(player);
            player!.Publish(20);                                  // a duck queued while the first gain is confirming
            player.Volume = 46;                                   // its older queued write lands after it
            Assert.True(SpinWait.SpinUntil(() => player.CurrentPlayback is { Playing: true, Muted: false, Volume: 20 }, 10_000));
        }
        finally
        {
            layers.Dispose();
            player?.Closing.Wait(5000);
            (s.AudioLayers, s.AudioLayersEnabled, s.AudioLayersMasterVolume, s.MasterVolume) = saved;
            File.Delete(file);
        }
    }

    [Fact]
    public void PlayingBeforeTheFirstGainStaysMutedUntilItIsConfigured()
    {
        var vlc = SilentVlc();
        var file = WriteWav();
        var player = new LayeredAudio.VlcLayerPlayer(vlc, file);
        try
        {
            Assert.True(SpinWait.SpinUntil(() => player.CurrentPlayback is { Playing: true }, 10_000));
            Thread.Sleep(100); // give the Playing callback time to try to apply a missing gain
            Assert.True(player.CurrentPlayback is { Muted: true });
            player.Volume = 35;
            Assert.True(SpinWait.SpinUntil(() => player.CurrentPlayback is { Playing: true, Muted: false, Volume: 35 }, 10_000));
        }
        finally
        {
            player.Dispose();
            player.Closing.Wait(5000);
            File.Delete(file);
        }
    }

    private static LibVLCSharp.Shared.LibVLC SilentVlc()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("ALSA null output test runs on Linux");
        try { return new LibVlcAudio("--aout=alsa", "--alsa-audio-device=null").Vlc; }
        catch (Exception e)
        {
            if (Environment.GetEnvironmentVariable("CI") == "true") throw;
            Assert.Skip("libvlc not installed: " + e.Message);
            throw;
        }
    }

    private static string WriteWav()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ccp-layer-{Guid.NewGuid():N}.wav");
        const int rate = 8000, data = rate * 10 * 2;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data); w.Write(new byte[data]);
        return path;
    }
}
