using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class LibVlcOneShotPauseTests
{
    /// <summary>WPF AudioPlaybackHandle.Pause/Resume: a paused one-shot holds, and a freeze that lifts
    /// at once (pause then resume back to back) still leaves it playing to the end.</summary>
    [Fact]
    public void OneShot_pause_holds_and_rapid_pause_resume_ends_playing()
    {
        var shared = typeof(LibVlcAudio).GetProperty("Shared", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var prior = shared.GetValue(null);
        var path = Path.Combine(Directory.CreateTempSubdirectory("oneshot-pause-").FullName, "line.wav");
        using (var w = new BinaryWriter(File.Create(path)))
        {
            const int data = 8000 * 2; // one second, 8 kHz mono 16-bit
            w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(data); w.Write(new byte[data]);
        }
        using var started = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        Action? stop = null;
        try
        {
            var audio = new LibVlcAudio("--aout=dummy");
            stop = audio.PlayOneShot(path, 0.5f, "test", _ => started.Set(), finished.Set);
            Assert.True(started.Wait(5000));
            LibVlcAudio.SetPaused(stop, true);
            Assert.False(finished.Wait(2500), "a paused one-shot ran to the end");
            for (var i = 0; i < 20; i++) { LibVlcAudio.SetPaused(stop, true); LibVlcAudio.SetPaused(stop, false); }
            Assert.True(finished.Wait(5000), "the one-shot stayed paused after the last resume");
        }
        finally
        {
            stop?.Invoke();
            finished.Wait(5000);
            shared.SetValue(null, prior);
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch (IOException) { }
        }
    }
}
