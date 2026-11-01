using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FirstShowPreviewBackendTests
{
    [Fact]
    public void NativeOutputHasRequestedGainFromTheFirstSample()
    {
        RequireLinuxVlc();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-preview-gain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var input = Path.Combine(dir, "input.wav"); var output = Path.Combine(dir, "output.wav");
        WriteConstantWav(input);
        using var finished = new ManualResetEventSlim();
        try
        {
            using var backend = new LibVlcAudio.FirstShowCue("--aout=afile", "--audiofile-file=" + output,
                "--audiofile-format=s16", "--audiofile-channels=1", "--audiofile-wav");
            backend.PlayerCreated = player => player.EndReached += (_, _) => finished.Set();
            foreach (var (gain, expected) in new[] { (.25f, 4000), (.5f, 8000) })
            {
                finished.Reset();
                var stop = backend.Play(input, gain, "gain-test");
                Assert.True(finished.Wait(TimeSpan.FromSeconds(10)), "LibVLC did not finish the cue");
                stop();
                using var file = new BinaryReader(File.OpenRead(output));
                Assert.Equal("RIFF", new string(file.ReadChars(4))); file.ReadInt32();
                Assert.Equal("WAVE", new string(file.ReadChars(4)));
                while (true)
                {
                    var id = new string(file.ReadChars(4)); var size = file.ReadInt32();
                    if (id == "data") { Assert.True(size >= 128); break; }
                    file.BaseStream.Position += size + (size & 1);
                }
                for (int i = 0; i < 64; i++) Assert.InRange(file.ReadInt16(), expected - 2, expected + 2);
            }
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplacementAndCloseWaitForTheNativeStop(bool preview)
    {
        RequireLinuxVlc();
        var path = Path.Combine(Path.GetTempPath(), "ccp-preview-stop-" + Guid.NewGuid().ToString("N") + ".wav");
        WriteConstantWav(path);
        var settings = CoreSettings.ServiceProvider; var overridePath = CoreModArt.AudioOverridePathProvider;
        var suppressed = CoreAudio.IsOutputSuppressedProvider; var stoppable = CoreAudio.PlayStoppableProvider;
        var oneShot = CoreAudio.PlayOneShotProvider;
        using var enteredFirst = new ManualResetEventSlim(); using var enteredSecond = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim(); using var releaseSecond = new ManualResetEventSlim();
        using var stoppedFirst = new ManualResetEventSlim(); using var stoppedSecond = new ManualResetEventSlim();
        int started = 0, stops = 0;
        var backend = new LibVlcAudio.FirstShowCue("--aout=dummy");
        var audio = new FirstShowAudio(preview);
        Task? replace = null, close = null;
        try
        {
            CoreSettings.ServiceProvider = null; CoreModArt.AudioOverridePathProvider = _ => path;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = null; CoreAudio.PlayOneShotProvider = null;
            if (!preview)
            {
                var settingsService = new SettingsService(); settingsService.Current.MasterVolume = 65;
                CoreSettings.ServiceProvider = () => settingsService;
                var seeded = new LibVlcAudio(_ => "[]");   // same delegate target as Seed(), no system audio
                CoreAudio.PlayStoppableProvider = seeded.PlayOneShot;
            }
            backend.PlayerCreated = _ => Interlocked.Increment(ref started);
            backend.StopPlayer = stop =>
            {
                var n = Interlocked.Increment(ref stops);
                if (n == 1) { enteredFirst.Set(); releaseFirst.Wait(TimeSpan.FromSeconds(10)); }
                else { enteredSecond.Set(); releaseSecond.Wait(TimeSpan.FromSeconds(10)); }
                stop();
                if (n == 1) stoppedFirst.Set(); else stoppedSecond.Set();
            };
            audio.CreateCuePlayer = () => backend.Play;
            audio.Cue("click"); Assert.Equal(1, Volatile.Read(ref started));
            replace = Task.Run(() => audio.Cue("click"));
            Assert.True(enteredFirst.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotSame(replace, await Task.WhenAny(replace, Task.Delay(500)));
            Assert.Equal(1, Volatile.Read(ref started));
            releaseFirst.Set(); await replace;
            Assert.Equal(2, Volatile.Read(ref started));
            close = Task.Run(audio.Dispose);
            Assert.True(enteredSecond.Wait(TimeSpan.FromSeconds(10)));
            Assert.NotSame(close, await Task.WhenAny(close, Task.Delay(500)));
            releaseSecond.Set(); await close;
        }
        finally
        {
            releaseFirst.Set(); releaseSecond.Set();
            if (replace != null) await replace;
            if (enteredFirst.IsSet) Assert.True(stoppedFirst.Wait(TimeSpan.FromSeconds(10)));
            if (close != null) await close;
            audio.Dispose();
            if (Volatile.Read(ref started) >= 2) Assert.True(stoppedSecond.Wait(TimeSpan.FromSeconds(10)));
            backend.Dispose();
            CoreSettings.ServiceProvider = settings; CoreModArt.AudioOverridePathProvider = overridePath;
            CoreAudio.IsOutputSuppressedProvider = suppressed; CoreAudio.PlayStoppableProvider = stoppable;
            CoreAudio.PlayOneShotProvider = oneShot; File.Delete(path);
        }
    }

    private static void RequireLinuxVlc()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux LibVLC file and dummy outputs are required");
        if (!NativeLibrary.TryLoad("libvlc.so.5", out var handle)) Assert.Skip("LibVLC is not installed");
        NativeLibrary.Free(handle);
    }

    private static void WriteConstantWav(string path)
    {
        const int rate = 44100, samples = rate;
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + samples * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate);
        w.Write(rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8.ToArray()); w.Write(samples * 2);
        for (int i = 0; i < samples; i++) w.Write((short)16000);
    }
}
