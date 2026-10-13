using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Race;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The race's LibVLC player and decoder (WPF Services/Race/TrackPlayer.cs, NAudioTrackDecode.cs).
/// Real libvlc with the dummy output: no device, no network.</summary>
public sealed class RaceTrackPlayerTests
{
    private static void NeedVlc()
    {
        try { _ = new LibVlcAudio("--aout=dummy"); }
        catch (Exception e)
        {
            if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CI") == "true") throw;
            Assert.Skip("libvlc not installed: " + e.Message);
        }
    }

    /// <summary>16-bit PCM WAV; dataSize overrides the header's data length (0 = an unpatched header).</summary>
    private static byte[] Wav(int rate, short channels, short[] samples, uint? dataSize = null)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int bytes = samples.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write(channels); w.Write(rate);
        w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(dataSize ?? (uint)bytes);
        foreach (var s in samples) w.Write(s);
        return ms.ToArray();
    }

    /// <summary>WPF NAudioTrackDecode.Decode: any file to mono 16 kHz floats, with progress; the seed App
    /// gives Core (App.axaml.cs, WPF App.xaml.cs:313). A 2 s 44.1 kHz stereo tone comes back as ~32000 samples
    /// at its own loudness, and the temporary WAV is gone afterwards.</summary>
    [Fact]
    public void DecodeTurnsAStereoFileIntoMono16k()
    {
        NeedVlc();
        var dir = Directory.CreateTempSubdirectory("ccp-race-decode-").FullName;
        var src = Path.Combine(dir, "tone.wav");
        var tone = Enumerable.Range(0, 44100 * 2).SelectMany(i => { var s = (short)(Math.Sin(i * 2 * Math.PI * 440 / 44100) * 16000); return new[] { s, s }; }).ToArray();
        File.WriteAllBytes(src, Wav(44100, 2, tone));
        var old = TrackDecoder.DecodeProvider;
        var leftovers = Directory.GetFiles(Path.GetTempPath(), "ccp-race-*.wav").Length;
        try
        {
            TrackDecoder.DecodeProvider = RaceTrackPlayer.Decode;
            var reports = new List<double>();
            var pcm = TrackDecoder.Decode(src, new SyncProgress(reports.Add), CancellationToken.None);
            Assert.InRange(pcm.Mono16k.Length, 30400, 33600);
            Assert.InRange(pcm.DurationSec, 1.9, 2.1);
            double rms = Math.Sqrt(pcm.Mono16k.Average(x => (double)x * x));
            Assert.InRange(rms, 0.25, 0.45);                     // 16000/32768 / sqrt 2 = 0.345
            Assert.Equal(1, reports.Last());
            Assert.All(reports, r => Assert.InRange(r, 0, 1));
            Assert.Equal(leftovers, Directory.GetFiles(Path.GetTempPath(), "ccp-race-*.wav").Length);
            Assert.Throws<OperationCanceledException>(() => RaceTrackPlayer.Decode(src, null, new CancellationToken(true)));
        }
        finally
        {
            TrackDecoder.DecodeProvider = old;
            Directory.Delete(dir, true);
        }
    }

    /// <summary>The WAV reader: a header the muxer never patched reads to the end; anything not 16 kHz mono
    /// 16-bit (or not a WAV at all) is refused with the page's "would not decode" message.</summary>
    [Fact]
    public void ReadMono16TakesUnpatchedHeadersAndRefusesOtherFormats()
    {
        var samples = new short[] { 0, 16384, -32768, 32767 };
        Assert.Equal(new[] { 0f, 0.5f, -1f, 32767 / 32768f }, RaceTrackPlayer.ReadMono16(Wav(16000, 1, samples, dataSize: 0)));
        Assert.Equal(new[] { 0f, 0.5f }, RaceTrackPlayer.ReadMono16(Wav(16000, 1, samples, dataSize: 4)));
        Assert.Equal(4, RaceTrackPlayer.ReadMono16(Wav(16000, 1, samples, dataSize: uint.MaxValue)).Length);
        Assert.Throws<InvalidDataException>(() => RaceTrackPlayer.ReadMono16(Wav(44100, 1, samples)));
        Assert.Throws<InvalidDataException>(() => RaceTrackPlayer.ReadMono16(Wav(16000, 2, samples)));
        Assert.Throws<InvalidDataException>(() => RaceTrackPlayer.ReadMono16(new byte[64]));
    }

    /// <summary>WPF OnPlaybackStopped :152-157: the file running out raises Ended once; a Stop the run asked
    /// for never does; Play re-arms it. Master volume maps through the cube root (WPF linear gain).</summary>
    [Fact]
    public void EndedOnlyForARealEndingAndOnce()
    {
        NeedVlc();
        var dir = Directory.CreateTempSubdirectory("ccp-race-player-").FullName;
        var src = Path.Combine(dir, "short.wav");
        File.WriteAllBytes(src, Wav(16000, 1, new short[16000]));
        using var player = new RaceTrackPlayer();
        int ended = 0;
        player.Ended += () => ended++;
        try
        {
            player.Load(src);
            // Load does not wait for the parse (the UI thread); the length arrives shortly after.
            Assert.True(SpinWait.SpinUntil(() => player.DurationSec > 0, 10_000), "duration parsed");
            Assert.InRange(player.DurationSec, 0.9, 1.1);
            player.OnEndReached(null, EventArgs.Empty);
            player.OnEndReached(null, EventArgs.Empty);
            Assert.Equal(1, ended);

            player.Load(src);
            player.Stop();
            player.OnEndReached(null, EventArgs.Empty);
            Assert.Equal(1, ended);

            player.Play();
            player.OnEndReached(null, EventArgs.Empty);
            Assert.Equal(2, ended);
            player.Stop();
        }
        finally { Directory.Delete(dir, true); }

        Assert.Equal((100, 0, 79, 100), (RaceTrackPlayer.VlcVolume(100), RaceTrackPlayer.VlcVolume(0),
            RaceTrackPlayer.VlcVolume(50), RaceTrackPlayer.VlcVolume(150)));
    }

    /// <summary>The page's Brake (track-pause on) right after track-play reaches the player while libvlc is
    /// still opening; WPF's WaveOutEvent pauses at once, so the pause is kept and applied once it plays,
    /// and a Resume before then cancels it.</summary>
    [Fact]
    public void PauseRightAfterPlayIsHonoured()
    {
        NeedVlc();
        var dir = Directory.CreateTempSubdirectory("ccp-race-pause-").FullName;
        var src = Path.Combine(dir, "long.wav");
        File.WriteAllBytes(src, Wav(16000, 1, new short[16000 * 20]));
        using var player = new RaceTrackPlayer();
        try
        {
            player.Load(src);
            player.Play();
            player.Pause();
            Assert.True(SpinWait.SpinUntil(() => player.State == LibVLCSharp.Shared.VLCState.Paused, 10_000),
                "paused, not " + player.State);
            Assert.False(player.IsPlaying);

            player.Play();
            player.Pause();
            player.Resume();
            Assert.True(SpinWait.SpinUntil(() => player.IsPlaying, 10_000), "a Resume before Playing cancels the pause");

            // Pause then Resume at once, while libvlc's async SetPause(true) is still in flight: it plays on.
            Assert.True(SpinWait.SpinUntil(() => player.State == LibVLCSharp.Shared.VLCState.Playing, 10_000));
            player.Pause();
            player.Resume();
            double from = player.PositionSec;
            Assert.True(SpinWait.SpinUntil(() => player.PositionSec > from + 0.5, 10_000),
                "a Resume right after a Pause keeps playing, not " + player.State);
            player.Stop();
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) { lock (this) report(value); }
    }
}
