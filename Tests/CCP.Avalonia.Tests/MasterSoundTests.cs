using System;
using System.Linq;
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
    [InlineData("panic")]
    [InlineData("pause")]
    [InlineData("shutdown")]
    public void Stop_routes_release_sound_with_engine_off(string route)
    {
        CoreSession.IsEngineRunningProvider = () => false;
        var audio = new LibVlcAudio(_ => "");
        audio.Seed();
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
        finally { audio.StopSound(); runner.Stop(); }
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
}
