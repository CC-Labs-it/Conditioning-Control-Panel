using System;
using System.IO;
using System.Linq;
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
        RequireNativeVlc();
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
        RequireNativeVlc();
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

    [Fact]
    public void NativeCueSelectsTheSavedDeviceBeforePlayingAndFallsBackForStaleIds()
    {
        RequireNativeVlc();
        var path = Path.Combine(Path.GetTempPath(), "ccp-cue-route-" + Guid.NewGuid().ToString("N") + ".wav");
        WriteConstantWav(path);
        var oldSettings = CoreSettings.ServiceProvider;
        try
        {
            var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
            service.Current.AudioOutputDeviceId = "";
            var output = OperatingSystem.IsWindows() ? "directsound" : "pulse";
            string? selected = OperatingSystem.IsLinux() ? "ccp_headphones" : null;
            if (OperatingSystem.IsWindows())
            {
                using var probe = new LibVlcAudio.FirstShowCue("--aout=directsound,none");
                probe.PlayerCreated = player =>
                {
                    Assert.True(player.SetAudioOutput("directsound"));
                    selected = player.AudioOutputDeviceEnum.Select(d => d.DeviceIdentifier).FirstOrDefault(id => !string.IsNullOrEmpty(id));
                };
                var stop = probe.Play(path, .25f, "device-probe"); stop();
                Assert.False(string.IsNullOrEmpty(selected), "The Windows test requires a DirectSound output device");
            }
            using var backend = new LibVlcAudio.FirstShowCue("--aout=" + output + ",none");
            foreach (var saved in new[] { selected!, "", "ccp-missing-output" })
            {
                service.Current.AudioOutputDeviceId = saved;
                using var playing = new ManualResetEventSlim();
                LibVLCSharp.Shared.MediaPlayer? current = null; string? beforePlay = null;
                backend.PlayerCreated = player => { current = player; beforePlay = player.OutputDevice; player.Playing += (_, _) => playing.Set(); };
                var stop = backend.Play(path, .25f, "device-test");
                Assert.True(playing.Wait(TimeSpan.FromSeconds(10)), "Native audio did not start");
                if (saved == selected) Assert.Equal(selected, beforePlay);
                else Assert.NotEqual(saved.Length == 0 ? "ccp-missing-output" : saved, beforePlay);
                if (OperatingSystem.IsLinux())
                {
                    var expected = saved == selected ? selected : "ccp_default";
                    Assert.True(SpinWait.SpinUntil(() => current!.OutputDevice == expected, TimeSpan.FromSeconds(5)), "Pulse sink was not selected");
                }
                stop();
            }
            // A device that becomes available after player creation still uses the existing live fallback.
            service.Current.AudioOutputDeviceId = "";
            using var latePlaying = new ManualResetEventSlim();
            LibVLCSharp.Shared.MediaPlayer? late = null;
            backend.PlayerCreated = player => { late = player; service.Current.AudioOutputDeviceId = selected!; player.Playing += (_, _) => latePlaying.Set(); };
            var lateStop = backend.Play(path, .25f, "late-device-test");
            Assert.True(latePlaying.Wait(TimeSpan.FromSeconds(10)), "Native audio did not start");
            Assert.True(SpinWait.SpinUntil(() => late!.OutputDevice == selected, TimeSpan.FromSeconds(5)), "Live device fallback was not applied");
            lateStop();
        }
        finally { CoreSettings.ServiceProvider = oldSettings; File.Delete(path); }
    }

    private static void RequireNativeVlc()
    {
        if (OperatingSystem.IsWindows())
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "libvlc", Environment.Is64BitProcess ? "win-x64" : "win-x86");
            Assert.True(File.Exists(Path.Combine(dir, "libvlc.dll")), "Windows LibVLC is required");
            Assert.True(File.Exists(Path.Combine(dir, "plugins", "audio_output", "libdirectsound_plugin.dll")), "DirectSound plugin is required");
            return;
        }
        Assert.True(OperatingSystem.IsLinux(), "Native audio tests require Windows or Linux");
        Assert.True(NativeLibrary.TryLoad("libvlc.so.5", out var handle), "Linux LibVLC is required");
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
