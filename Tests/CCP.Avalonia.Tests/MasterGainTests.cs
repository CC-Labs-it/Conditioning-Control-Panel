using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

// No windows, fonts or native audio: exercise the real player with fake output voices.
// Runs alone: it moves the process-wide CoreSettings.Current dials and the shared master watcher.
[Collection(RunsAloneCollection.Name)]
public sealed class MasterGainTests
{
    private sealed class Voice : MindWipePlayer.IVoice
    {
        public double Gain;
        public bool Disposed;
        public double Volume { set => Gain = value; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Both_live_players_follow_master_and_own_dials_and_stop_detaches()
    {
        var settings = CoreSettings.Current;
        var saved = (settings.MasterVolume, settings.MindWipeVolume, settings.MindWipeAudioPath);
        using var folder = new Folder();
        var voices = new List<Voice>();
        settings.MasterVolume = 50;
        settings.MindWipeVolume = 80;
        settings.MindWipeAudioPath = folder.Clip;
        var player = new MindWipePlayer((_, gain, _, _) =>
        {
            var voice = new Voice { Gain = gain };
            voices.Add(voice);
            return voice;
        });
        try
        {
            player.TriggerOnce();
            player.StartLoop(0.8);
            Assert.All(voices, v => Assert.Equal(0.4, v.Gain, 4));
            settings.MasterVolume = 0;
            Assert.All(voices, v => Assert.Equal(0, v.Gain));
            settings.MasterVolume = 25;
            Assert.All(voices, v => Assert.Equal(0.2, v.Gain, 4));
            player.UpdateSettings(6, 0.4);
            Assert.All(voices, v => Assert.Equal(0.1, v.Gain, 4));
            player.Stop();
            settings.MasterVolume = 100;
            Assert.All(voices, v => { Assert.True(v.Disposed); Assert.Equal(0.1, v.Gain, 4); });
        }
        finally
        {
            player.Stop();
            (settings.MasterVolume, settings.MindWipeVolume, settings.MindWipeAudioPath) = saved;
        }
    }

    [Fact]
    public void A_dial_moved_during_player_creation_is_applied_before_publication()
    {
        var settings = CoreSettings.Current;
        var saved = (settings.MasterVolume, settings.MindWipeAudioPath);
        using var folder = new Folder();
        settings.MasterVolume = 100;
        settings.MindWipeAudioPath = folder.Clip;
        var voice = new Voice();
        var player = new MindWipePlayer((_, gain, _, _) =>
        {
            voice.Gain = gain;
            settings.MasterVolume = 0;
            return voice;
        });
        try { player.StartLoop(1); Assert.Equal(0, voice.Gain); }
        finally { player.Stop(); (settings.MasterVolume, settings.MindWipeAudioPath) = saved; }
    }

    [Fact]
    public async System.Threading.Tasks.Task Companion_audio_uses_the_sound_gain_surface()
    {
        var previous = CoreAudio.PlaySoundProvider;
        var called = false;
        CoreAudio.PlaySoundProvider = (path, own, tag, started, _) =>
        {
            Assert.Equal("fixture.mp3", path);
            Assert.Equal(1f, own);
            called = true;
            started?.Invoke(TimeSpan.FromSeconds(1));
        };
        try
        {
            Assert.True(await ConditioningControlPanel.Avalonia.CompanionEffects.PlayAudio("fixture.mp3"));
            Assert.True(called);
        }
        finally { CoreAudio.PlaySoundProvider = previous; }
    }

    [Theory]
    [InlineData(1f, true, 100, 100)]
    [InlineData(1f, true, 50, 71)]   // SoundGain 0.5^1.5 = 0.354, cubic -> 71
    [InlineData(1f, true, 0, 0)]
    [InlineData(0.05f, true, 100, 37)] // SoundGain floor 0.05
    [InlineData(1f, false, 0, 100)]  // a one-shot ignores master
    [InlineData(0.5f, false, 100, 79)]
    public void LibVlc_clip_volume_follows_master_only_for_sounds(float own, bool followMaster, int master, int expected)
        => Assert.Equal(expected, LibVlcAudio.VlcVolume(own, followMaster, master));

    [Fact]
    public void Sound_binding_follows_master_until_finished()
    {
        var settings = CoreSettings.Current;
        settings.MasterVolume = 100;
        var heard = new List<float>();
        using (var binding = new LibVlcAudio.SoundMasterBinding(0.5f, heard.Add))
        {
            settings.MasterVolume = 50;
            settings.MasterVolume = 0;
            settings.MasterVolume = 100;
        }
        settings.MasterVolume = 25;
        Assert.Equal(new[] { 0.125f, 0f, (float)Math.Pow(0.5, 1.5) }, heard);
    }

    [Fact]
    public void Panic_force_restores_nested_ducks()
    {
        CoreSettings.Current.MasterVolume = 100;
        var calls = new List<string>();
        var audio = new LibVlcAudio(args =>
        {
            if (args.StartsWith("-f json"))
                return "[{\"index\":7,\"volume\":{\"left\":{\"value\":65536}},\"properties\":{\"application.process.id\":\"1\"}}]";
            calls.Add(args);
            return "";
        });
        LibVlcAudio.Instance = audio;
        try
        {
            audio.Duck(80);
            audio.Duck(80);
            audio.Drain();
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "audio-duck").Stop(null);
            audio.Drain();
            Assert.Contains("set-sink-input-volume 7 65536", calls);
            var count = calls.Count;
            audio.Unduck(-1);
            audio.Drain();
            Assert.Equal(count, calls.Count);
        }
        finally { audio.Shutdown(); LibVlcAudio.Instance = null; }
    }

    private sealed class Folder : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("ccp-master-gain-");
        public string Clip => Path.Combine(_dir.FullName, "wipe.mp3");
        public Folder() => File.WriteAllText(Clip, "fixture");
        public void Dispose() => _dir.Delete(true);
    }
}
