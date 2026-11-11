using System;
using System.IO;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosMenuLoopVoiceTests
{
    [Fact]
    public void Loop_pauses_position_and_overlap_timer_and_resumes_same_player()
    {
        var root = Directory.CreateTempSubdirectory("chaos-menu-loop-").FullName;
        var path = Path.Combine(root, "cue.wav");
        using (var w = new BinaryWriter(File.Create(path)))
        {
            const int data = 8000 * 4 * 2;
            w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(data); w.Write(new byte[data]);
        }
        var audio = new LibVlcAudio("--aout=dummy");
        int copies = 0, pauses = 0;
        long firstTime = 0;
        var previous = LibVlcAudio.ConfigureLoopVoice;
        LibVlcAudio.ConfigureLoopVoice = player =>
        {
            if (Interlocked.Increment(ref copies) != 1) return;
            player.TimeChanged += (_, e) => Interlocked.Exchange(ref firstTime, e.Time);
            player.Paused += (_, _) => Interlocked.Increment(ref pauses);
        };
        LibVlcAudio.IPausableVoice? voice = null;
        try
        {
            voice = Assert.IsAssignableFrom<LibVlcAudio.IPausableVoice>(audio.PlayVoice(path, 0.5, true, () => { }));
            // Pause can arrive while LibVLC is still opening. A rapid resume must also win.
            voice.SetPaused(true);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref pauses) >= 1, 5000));
            voice.SetPaused(false);
            voice.SetPaused(true);
            voice.SetPaused(false);
            Assert.True(SpinWait.SpinUntil(() => Interlocked.Read(ref firstTime) >= 500, 5000));
            var beforePause = Volatile.Read(ref pauses);
            voice.SetPaused(true);
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref pauses) > beforePause, 3000));
            var pausedAt = Interlocked.Read(ref firstTime);
            Thread.Sleep(3800); // longer than the remaining loop interval
            Assert.Equal(1, Volatile.Read(ref copies));
            Assert.InRange(Interlocked.Read(ref firstTime), pausedAt - 300, pausedAt + 300);
            voice.SetPaused(false);
            Assert.True(SpinWait.SpinUntil(() => Interlocked.Read(ref firstTime) > pausedAt + 250, 3000),
                $"paused at {pausedAt}, now {Interlocked.Read(ref firstTime)}, copies {Volatile.Read(ref copies)}, pauses {Volatile.Read(ref pauses)}");
            Assert.Equal(1, Volatile.Read(ref copies)); // preserve the remaining loop interval
            Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref copies) >= 2, 4000));
        }
        finally
        {
            voice?.Dispose();
            LibVlcAudio.ConfigureLoopVoice = previous;
            Assert.True(SpinWait.SpinUntil(() =>
            {
                try { Directory.Delete(root, true); return true; }
                catch (IOException) { return false; }
            }, 5000));
        }
    }
}
