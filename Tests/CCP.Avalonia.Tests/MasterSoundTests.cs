using System;
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
        public bool Disposed;
        public double Volume { set => Gain = value; }
        public Action<long>? Started { private get; set; }
        public bool Play() { Started?.Invoke(2000); return true; }
        public void Dispose() { Disposed = true; ended(); }
    }

    [Fact]
    public void Sound_starts_muted_follows_master_and_unsubscribes_when_finished()
    {
        var settings = CoreSettings.Current;
        var before = settings.MasterVolume;
        Voice? voice = null;
        Action? stop = null;
        var finished = 0;
        try
        {
            settings.MasterVolume = 0;
            stop = LibVlcAudio.StartSound(ended => voice = new Voice(ended), 50,
                duration => Assert.Equal(TimeSpan.FromSeconds(2), duration), () => finished++);
            Assert.Equal(0, voice!.Gain);
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
        finally { if (voice?.Disposed == false) stop?.Invoke(); settings.MasterVolume = before; }
    }
}
