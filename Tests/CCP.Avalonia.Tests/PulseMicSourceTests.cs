using System.Linq;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The mic picker's Linux list: OS default first, real sources by name, no sink monitors
/// (a monitor is other apps' output, not a microphone).</summary>
public sealed class PulseMicSourceTests
{
    [Fact]
    public void Lists_default_then_real_sources_without_monitors()
    {
        const string pactl =
            "55\talsa_output.usb-BEACN.pro-output-0.monitor\tPipeWire\ts32le 2ch 48000Hz\tSUSPENDED\n" +
            "56\talsa_input.usb-BEACN.pro-input-0\tPipeWire\ts32le 2ch 48000Hz\tSUSPENDED\n" +
            "57\tbluez_input.headset\tPipeWire\ts16le 1ch 16000Hz\tIDLE\n";
        var devices = PulseMicSource.ParseSources(pactl);
        Assert.Equal(new[] { "System default", "alsa_input.usb-BEACN.pro-input-0", "bluez_input.headset" },
            devices.Select(d => d.Name));
        Assert.Equal(new[] { -1, 0, 1 }, devices.Select(d => d.Index));
    }

    /// <summary>Distro-check F6: no pactl -> fail open without spawning it; with pactl the list decides.</summary>
    [Fact]
    public void Microphone_probe_without_pactl_fails_open_without_spawning()
    {
        Assert.True(PulseMicSource.MicrophonePresent(false, () => throw new System.InvalidOperationException("spawned pactl")));
        Assert.False(PulseMicSource.MicrophonePresent(true, () => "55\tx.monitor\tPipeWire\ts32le\tIDLE\n"));

        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-path-").FullName;
        try
        {
            Assert.False(LibVlcAudio.OnPath("pactl", dir));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "pactl"), "");
            Assert.True(LibVlcAudio.OnPath("pactl", "/nonexistent" + System.IO.Path.PathSeparator + dir));
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    /// <summary>WPF App.xaml.cs:527: on Windows the quest probe counts waveIn devices instead of asking pactl,
    /// and a throw still reaches the gate's CachedProbe (fail open there, not absent here).</summary>
    [Fact]
    public void Windows_quest_microphone_probe_counts_wavein_devices()
    {
        Assert.False(PulseMicSource.QuestMicrophoneProbe(true, () => 0)());
        Assert.True(PulseMicSource.QuestMicrophoneProbe(true, () => 1)());
        Assert.Throws<System.InvalidOperationException>(() =>
            PulseMicSource.QuestMicrophoneProbe(true, () => throw new System.InvalidOperationException("winmm"))());
    }
}
