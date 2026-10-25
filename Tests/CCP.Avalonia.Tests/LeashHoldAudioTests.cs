using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LeashHoldAudioTests
{
    [Fact]
    public void Tick_UsesModCueAndMasterGain_AndStaysSilentWhenMutedOrSuppressed()
    {
        var (oldAudio, oldArt, oldSuppressed, oldSettings) = (CoreAudio.PlayOneShotProvider,
            CoreModArt.AudioOverridePathProvider, CoreAudio.IsOutputSuppressedProvider, CoreSettings.ServiceProvider);
        var settings = new SettingsService();
        var cue = Path.GetTempFileName();
        var played = new List<(string Path, float Gain, string Tag)>();
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreModArt.AudioOverridePathProvider = rel => { Assert.Equal("chaos/countdown_tick.mp3", rel); return cue; };
            CoreAudio.PlayOneShotProvider = (path, gain, tag, _, _) => played.Add((path, gain, tag));
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreSettings.Current.MasterVolume = 50;
            var now = new DateTime(2030, 1, 1);
            LeashSound.Tick(now);
            Assert.Equal((cue, 0.05f, "leash-tick"), Assert.Single(played));
            LeashSound.Tick(now.AddMilliseconds(50)); // WPF's global 130 ms gap.
            Assert.Single(played);
            CoreSettings.Current.MasterVolume = 0;
            LeashSound.Tick(now.AddSeconds(1));
            CoreSettings.Current.MasterVolume = 100;
            CoreAudio.IsOutputSuppressedProvider = () => true;
            LeashSound.Tick(now.AddSeconds(2));
            Assert.Single(played);
            CoreAudio.IsOutputSuppressedProvider = () => false;
            File.Delete(cue);
            LeashSound.Tick(now.AddSeconds(3));
            Assert.Single(played);
        }
        finally
        {
            (CoreAudio.PlayOneShotProvider, CoreModArt.AudioOverridePathProvider, CoreAudio.IsOutputSuppressedProvider, CoreSettings.ServiceProvider)
                = (oldAudio, oldArt, oldSuppressed, oldSettings);
            File.Delete(cue);
        }
    }
}
