using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FirstShowAudioTests
{
    [Fact]
    public void VoiceCachesOneClipStopsThePreviousAndHonoursMasterAndSuppression()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldSuppression = CoreAudio.IsOutputSuppressedProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        using var audio = new FirstShowAudio();
        int played = 0, stopped = 0;
        try
        {
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            audio.Play = (path, volume, tag) =>
            {
                Assert.True(File.Exists(path)); Assert.Equal(.5f, volume);
                Assert.Equal("first-show-voice", tag); played++;
                return () => stopped++;
            };
            audio.Speak("hi again", "idle");
            var path = Assert.Single(Directory.GetFiles(audio.CacheDirectory));
            var stamp = File.GetLastWriteTimeUtc(path);
            audio.Speak("hi again", "idle");
            Assert.Single(Directory.GetFiles(audio.CacheDirectory));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(path));
            Assert.Equal(2, played); Assert.Equal(1, stopped);
            CoreSettings.Current.MasterVolume = 0; audio.Speak("muted");
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => true;
            audio.Speak("suppressed"); audio.Cue("flash");
            Assert.Equal(2, played);
            audio.Dispose(); audio.Dispose(); audio.Speak("closed");
            Assert.Equal(2, stopped); Assert.False(Directory.Exists(audio.CacheDirectory));
        }
        finally
        {
            settings.SaveImmediate(); CoreSettings.ServiceProvider = oldSettings;
            CoreAudio.IsOutputSuppressedProvider = oldSuppression;
        }
    }
}
