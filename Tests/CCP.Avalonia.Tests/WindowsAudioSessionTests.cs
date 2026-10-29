using System;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// On Windows no playback may move this process's WASAPI session (the per-app mixer slider Windows
/// remembers): LibVLC plays through DirectSound, where Player.Volume / Mute are per player.
/// The behaviour test plays SILENT audio in the test process only and puts the session back after.
/// </summary>
public sealed class WindowsAudioSessionTests
{
    [Fact]
    public void WindowsPlaysThroughDirectSoundUnlessTheCallerPicksAnOutput()
    {
        Assert.Equal("--aout=directsound,any", LibVlcAudio.WithOutput(Array.Empty<string>(), windows: true)[0]);
        Assert.Equal(new[] { "--aout=dummy" }, LibVlcAudio.WithOutput(new[] { "--aout=dummy" }, windows: true));
        Assert.Empty(LibVlcAudio.WithOutput(Array.Empty<string>(), windows: false));   // Linux: pulse is per stream
    }

    [Fact]
    public void LinearGainMapsToLibVlcsCubicVolume()
    {
        Assert.Equal(100, LibVlcAudio.VlcVolume(1f));
        Assert.Equal(79, LibVlcAudio.VlcVolume(0.5f));    // -6 dB, as WPF's 0.5 sample gain
        Assert.Equal(37, LibVlcAudio.VlcVolume(0.05f));   // a 5% slider stays 5%, no -20 dB floor
        Assert.Equal(0, LibVlcAudio.VlcVolume(0f));       // silence
        Assert.Equal(100, LibVlcAudio.VlcVolume(3f));
        Assert.Equal(0, LibVlcAudio.VlcVolume(-1f));
    }

    [Fact]
    public void PlayersAtDifferentLevelsAndAMuteNeverMoveTheProcessSession()
    {
        if (!OperatingSystem.IsWindows()) return;
        LibVlcAudio audio;
        try { audio = new LibVlcAudio(); } catch { return; }   // no libvlc on this runner
        var wav = Path.Combine(Path.GetTempPath(), $"ccp-silent-{Guid.NewGuid():N}.wav");
        WriteSilence(wav, 20);
        MediaPlayer? a = null, b = null;
        try
        {
            a = Play(audio.Vlc, wav);
            b = Play(audio.Vlc, wav);
            Set(a, p => p.Volume = 100);
            Set(b, p => p.Volume = 30);
            Set(b, p => p.Volume = 0);
            Set(a, p => p.Mute = true);
            var mine = OwnSessionVolumes();
            if (mine.Length == 0) return;             // no audio endpoint on this runner
            Assert.All(mine, v => Assert.True(v > 0.999f, $"the process session moved to {v:0.000}"));
            Assert.Equal(0, b.Volume);                // and each player kept its own level
            Assert.False(b.Mute);
        }
        finally
        {
            foreach (var p in new[] { a, b }) { try { p?.Stop(); p?.Dispose(); } catch { } }
            ResetOwnSessions();
            try { File.Delete(wav); } catch { }
        }
    }

    private static MediaPlayer Play(LibVLC vlc, string wav)
    {
        var m = new Media(vlc, wav, FromType.FromPath);
        m.AddOption(":no-video");
        var p = new MediaPlayer(m);
        using var playing = new ManualResetEventSlim();
        p.Playing += (_, _) => playing.Set();
        p.Play();
        playing.Wait(5000);
        Thread.Sleep(300);
        return p;
    }

    // As LibVlcAudio: never set a volume on libvlc's own thread, and give the output a moment.
    private static void Set(MediaPlayer p, Action<MediaPlayer> a) { ThreadPool.QueueUserWorkItem(_ => a(p)); Thread.Sleep(400); }

    private static float[] OwnSessionVolumes()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<float>();
        using var scope = WasapiSessions.Collect();
        return scope.Sessions.Where(s => s.Pid == Environment.ProcessId).Select(s => s.GetVolume()).ToArray();
    }

    private static void ResetOwnSessions()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            using var scope = WasapiSessions.Collect();
            foreach (var s in scope.Sessions.Where(s => s.Pid == Environment.ProcessId)) s.SetVolume(1f);
        }
        catch { }
    }

    private static void WriteSilence(string path, int seconds)
    {
        const int rate = 22050;
        var samples = rate * seconds;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + samples * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(samples * 2);
        w.Write(new byte[samples * 2]);
    }
}
