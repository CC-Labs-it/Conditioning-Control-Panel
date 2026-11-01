using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Audio;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class MasterSoundTests
{
    private sealed class Voice(Action ended) : LibVlcAudio.ISoundVoice
    {
        public double Gain;
        public bool Disposed, Playing;
        public string? Routed;
        public double Volume { set => Gain = value; }
        public Action<long>? Started { private get; set; }
        public void ApplyPreferredDevice() { Assert.True(Playing); Routed = LibVlcAudio.PreferredDevice(CoreSettings.Current.AudioOutputDeviceId, "default", new[] { "headset" }); }
        public bool Play() { Playing = true; Started?.Invoke(2000); return true; }
        public void Dispose() { Disposed = true; ended(); }
    }
    [Theory]
    [InlineData(false), InlineData(true)]
    public async Task Stop_and_replacement_wait_for_start_publication(bool replace)
    {
        var audio = new LibVlcAudio(_ => "");
        Voice? first = null, second = null;
        using ManualResetEventSlim creating = new(), release = new(), attempting = new();
        var start = Task.Run(() => audio.PlaySound("first", 50, null, null, done =>
        { first = new Voice(done); creating.Set(); release.Wait(); return first; }));
        Task? other = null;
        try
        {
            Assert.True(creating.Wait(TimeSpan.FromSeconds(5)));
            other = Task.Run(() => { attempting.Set(); if (replace) audio.PlaySound("second", 50, null, null, done => second = new Voice(done)); else audio.StopSound(); });
            Assert.True(attempting.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(other, await Task.WhenAny(other, Task.Delay(100)));
            release.Set();
            await Task.WhenAll(start, other);
            Assert.True(first!.Disposed);
            if (replace) { Assert.NotNull(second); Assert.False(second.Disposed); }
            audio.StopSound();
            Assert.True(second?.Disposed ?? true);
        }
        finally { release.Set(); await start; if (other != null) await other; audio.StopSound(); if (first?.Disposed == false) first.Dispose(); if (second?.Disposed == false) second.Dispose(); }
    }
    [Theory]
    [InlineData("panic"), InlineData("pause"), InlineData("shutdown")]
    public void Stop_routes_release_sound_with_engine_off(string route)
    {
        var previous = (CoreSession.IsEngineRunningProvider, CoreAudio.StopSoundProvider);
        CoreSession.IsEngineRunningProvider = () => false;
        var audio = new LibVlcAudio(_ => "");
        CoreAudio.StopSoundProvider = audio.StopSound;
        var runner = new SessionRunner(new SessionLogService());
        Voice? voice = null;
        var finished = 0;
        try
        {
            if (route == "pause") runner.Start(new Session { DurationMinutes = 1, Settings = new SessionSettings() });
            audio.PlaySound("fake", 50, null, () => finished++, ended => voice = new Voice(ended));
            Assert.False(CoreSession.IsEngineRunning);
            if (route == "panic") PanicSurfaces.All.Single(s => s.Id == "sound").Stop(null);
            else if (route == "pause") runner.Pause();
            else audio.Shutdown();
            Assert.True(voice!.Disposed);
            audio.StopSound();
            Assert.Equal(1, finished);
        }
        finally { try { audio.StopSound(); runner.Stop(); } finally { (CoreSession.IsEngineRunningProvider, CoreAudio.StopSoundProvider) = previous; } }
    }
    [Fact]
    public void Sound_routes_after_start_follows_master_and_unsubscribes_when_finished()
    {
        var settings = CoreSettings.Current;
        var before = (settings.MasterVolume, settings.AudioOutputDeviceId);
        Voice? voice = null;
        Action? stop = null;
        var finished = 0;
        try
        {
            settings.MasterVolume = 0;
            settings.AudioOutputDeviceId = "headset";
            stop = LibVlcAudio.StartSound(ended => voice = new Voice(ended), 50,
                duration => Assert.Equal(TimeSpan.FromSeconds(2), duration), () => finished++);
            Assert.Equal("headset", voice!.Routed);
            Assert.Equal(0, voice.Gain);
            settings.MasterVolume = 60;
            Assert.Equal(MasterVolumeBinding.SoundGain(50, 60), voice.Gain, 5);
            settings.MasterVolume = 0;
            Assert.Equal(0, voice.Gain);
            stop();
            Assert.True(voice.Disposed);
            Assert.Equal(1, finished);
            settings.MasterVolume = 100;
            Assert.Equal(0, voice.Gain);
        }
        finally { if (voice?.Disposed == false) stop?.Invoke(); (settings.MasterVolume, settings.AudioOutputDeviceId) = before; }
    }
    [Fact]
    public void Native_sound_stays_muted_until_a_delayed_setup_lands()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("ALSA null output is Linux-only");
        LibVlcAudio audio;
        // ALSA's null PCM: a real output with soft mute that discards every sample, so nothing reaches the desktop.
        try { audio = new LibVlcAudio("--aout=alsa", "--alsa-audio-device=null"); }
        catch (Exception e) when (Environment.GetEnvironmentVariable("CI") != "true") { Assert.Skip("libvlc not installed: " + e.Message); return; }
        var wav = System.IO.Path.Combine(System.IO.Directory.CreateTempSubdirectory("ccp-sound-").FullName, "a.wav");
        LayeredAudioLoopTests.WriteSilentWav(wav, ms: 3000);
        LibVlcAudio.Voice? voice = null;
        bool? mutedAtStart = null;
        using ManualResetEventSlim started = new(), held = new(), release = new();
        Task? holder = null;
        try
        {
            audio.PlaySound(wav, 50, _ => { mutedAtStart = voice!.Player.Mute; started.Set(); }, null, ended =>
            {
                voice = audio.NewVoice(wav, ended, route: true);
                // A busy pool: the setup work item cannot take the player until released.
                holder = Task.Run(() => { lock (voice.Player) { held.Set(); release.Wait(); } });
                held.Wait();
                return voice;
            });
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
            Thread.Sleep(200);
            Assert.True(mutedAtStart == true && voice!.Player.Mute, "audible before device and gain were applied");
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => { lock (voice!.Player) return !voice.Player.Mute; }, 5000), "setup never unmuted");
        }
        finally { release.Set(); holder?.Wait(); audio.StopSound(); }
    }
}
