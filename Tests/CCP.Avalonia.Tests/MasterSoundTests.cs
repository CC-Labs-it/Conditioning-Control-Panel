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
    private sealed class Voice(Action ended, string? failure = null) : LibVlcAudio.ISoundVoice
    {
        public double Gain;
        public int Writes;
        public bool Disposed, Playing;
        public string? Routed;
        public double Volume { set { Writes++; Gain = value; } }
        private Action<long>? _started;
        public Action<long>? Started { set { if (failure == "setup") throw new InvalidOperationException(); _started = value; } }
        public void ApplyPreferredDevice() { Assert.True(Playing); Routed = LibVlcAudio.PreferredDevice(CoreSettings.Current.AudioOutputDeviceId, "default", new[] { "headset" }); }
        public bool Play() { if (failure == "play") throw new InvalidOperationException(); Playing = true; ApplyPreferredDevice(); _started?.Invoke(2000); return true; }
        public void Dispose() { Disposed = true; ended(); }
    }
    [Theory, InlineData(false), InlineData(true)]
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
    [Theory, InlineData("panic"), InlineData("pause"), InlineData("shutdown")]
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
    [Theory, InlineData("setup"), InlineData("play")]
    public void Setup_failure_disposes_voice_and_detaches_volume_watch(string failure)
    {
        var settings = CoreSettings.Current;
        var before = settings.MasterVolume;
        Voice? voice = null;
        var finished = 0;
        var started = 0;
        try
        {
            var stop = LibVlcAudio.StartSound(done => voice = new Voice(done, failure), 50, _ => started++, () => finished++);
            Assert.True(voice!.Disposed);
            Assert.Equal(0, started);
            Assert.Equal(1, finished);
            var writes = voice.Writes;
            settings.MasterVolume = before == 0 ? 50 : 0;
            Assert.Equal(writes, voice.Writes);
            stop();
            Assert.Equal(1, finished);
        }
        finally { settings.MasterVolume = before; }
    }
    [Theory, InlineData(true, false, true), InlineData(true, true, false), InlineData(false, false, false)]
    public void Native_sound_and_mindwipe_wait_for_delayed_setup(bool route, bool stopDuringSetup, bool checkLoop)
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("ALSA null output is Linux-only");
        LibVlcAudio audio;
        // ALSA's null PCM: a real output with soft mute that discards every sample, so nothing reaches the desktop; no read-ahead, or the unpaced sink ends a clip a second early.
        try { audio = new LibVlcAudio("--aout=alsa", "--alsa-audio-device=null", "--file-caching=0"); }
        catch (Exception e) when (Environment.GetEnvironmentVariable("CI") != "true") { Assert.Skip("libvlc not installed: " + e.Message); return; }
        var wav = System.IO.Path.Combine(System.IO.Directory.CreateTempSubdirectory("ccp-sound-").FullName, "a.wav");
        LayeredAudioLoopTests.WriteSilentWav(wav, ms: 250);
        LibVlcAudio.Voice? voice = null;
        bool? mutedAtStart = null;
        using ManualResetEventSlim started = new(), held = new(), release = new();
        Task? holder = null;
        try
        {
            audio.PlaySound(wav, 50, _ => { mutedAtStart = voice!.Player.Mute; started.Set(); }, null, ended =>
            {
                voice = audio.NewVoice(wav, ended, route: route);
                // A busy pool: the setup work item cannot take the player until released.
                holder = Task.Run(() => { lock (voice.Player) { held.Set(); release.Wait(); } });
                held.Wait();
                return voice;
            });
            Assert.False(started.Wait(TimeSpan.FromMilliseconds(600)), "reported success before device and gain setup");
            Assert.True(SpinWait.SpinUntil(() => voice!.Player.State == LibVLCSharp.Shared.VLCState.Paused, 5000), "clip never paused");
            Assert.Equal(0, voice!.Player.Time);
            Assert.True(voice.Player.Mute, "audible before device and gain setup");
            if (stopDuringSetup)
            {
                audio.StopSound();
                release.Set();
                Assert.False(started.Wait(TimeSpan.FromMilliseconds(500)), "stopped clip started after setup");
                return;
            }
            release.Set();
            Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "setup never started playback");
            Assert.False(mutedAtStart);
            if (checkLoop)
            {
                var loopWav = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(wav)!, "loop.wav");
                LayeredAudioLoopTests.WriteSilentWav(loopWav, ms: 3000);
                using var loop = audio.PlayVoice(loopWav, 0.5, true, () => { });
                Assert.NotNull(loop);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var gate = loop.GetType().GetField("_gate", flags)!.GetValue(loop)!;
                var live = (System.Collections.Generic.List<LibVlcAudio.Voice>)loop.GetType().GetField("_live", flags)!.GetValue(loop)!;
                long firstTime = -1;
                LibVlcAudio.Voice? next = null;
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    lock (gate)
                    {
                        if (live.Count < 2 || live[1].Player.State != LibVLCSharp.Shared.VLCState.Paused) return false;
                        firstTime = live[0].Player.Time;
                        next = live[1];
                        return firstTime >= 0;
                    }
                }, 6000), "next loop copy was not prepared");
                Assert.InRange(firstTime, 0, 2000);
                loop.Volume = 0.125;
                Assert.False(SpinWait.SpinUntil(() => next!.Player.IsPlaying, 250), "next loop copy started too early");
                Assert.True(SpinWait.SpinUntil(() => next!.Player.IsPlaying, 5000), "next loop copy did not start");
                Assert.InRange(next!.Player.Volume, 49, 51);
                LibVlcAudio.Voice[] old = []; LibVlcAudio.Voice? parked = null, fresh = null; var overlapped = false;
                Assert.True(SpinWait.SpinUntil(() => { lock (gate) { old = live.ToArray(); return (parked = (LibVlcAudio.Voice?)loop.GetType().GetField("_prepared", flags)!.GetValue(loop)) != null; } }, 6000), "no copy parked");
                parked!.Failed = true; parked.Dispose();   // what EncounteredError does to a parked copy
                Assert.True(SpinWait.SpinUntil(() => { lock (gate) { fresh = live.FirstOrDefault(v => !old.Contains(v)); overlapped = live.Contains(next); return fresh != null; } }, 6000) && overlapped, "failed parked copy was not replaced at the overlap");
                Assert.True(SpinWait.SpinUntil(() => fresh!.Player.IsPlaying, 5000), "replacement never played");
            }
        }
        finally { release.Set(); holder?.Wait(); audio.StopSound(); }
    }
}
