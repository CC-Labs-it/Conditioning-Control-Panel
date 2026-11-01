using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FirstShowPreviewAudioTests
{
    [Fact]
    public void PreviewUsesStandalonePlayerAndFallbackVolumeOnlyWithoutSettings()
    {
        var path = Path.GetTempFileName();
        var settings = CoreSettings.ServiceProvider;
        var overridePath = CoreModArt.AudioOverridePathProvider;
        var suppressed = CoreAudio.IsOutputSuppressedProvider;
        var stoppable = CoreAudio.PlayStoppableProvider;
        var oneShot = CoreAudio.PlayOneShotProvider;
        try
        {
            CoreSettings.ServiceProvider = null;
            CoreModArt.AudioOverridePathProvider = _ => path;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = null; CoreAudio.PlayOneShotProvider = null;
            int created = 0, played = 0, stopped = 0;
            float volume = 0;
            using var audio = new FirstShowAudio(preview: true);
            audio.CreateCuePlayer = () =>
            {
                created++;
                return (p, v, tag) => { Assert.Equal(path, p); Assert.Equal("first-show-cue", tag); volume = v; played++; return () => stopped++; };
            };
            CoreAudio.IsOutputSuppressedProvider = () => true;
            audio.Cue("click");
            Assert.Equal(0, created);
            CoreAudio.IsOutputSuppressedProvider = () => false;
            audio.Cue("click");
            Assert.Equal(1, created); Assert.Equal(1, played); Assert.Equal(.208f, volume);
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            service.Current.MasterVolume = 0;
            audio.Cue("click");
            Assert.Equal(1, played);
            service.Current.MasterVolume = 50;
            audio.Cue("click");
            Assert.Equal(1, created); Assert.Equal(2, played); Assert.Equal(.5f * .32f, volume); Assert.Equal(1, stopped);
            audio.Dispose(); audio.Dispose(); audio.Cue("click");
            Assert.Equal(2, stopped); Assert.Equal(2, played);
        }
        finally
        {
            CoreSettings.ServiceProvider = settings; CoreModArt.AudioOverridePathProvider = overridePath;
            CoreAudio.IsOutputSuppressedProvider = suppressed; CoreAudio.PlayStoppableProvider = stoppable;
            CoreAudio.PlayOneShotProvider = oneShot; File.Delete(path);
        }
    }

    [Fact]
    public void AFailedStopDoesNotStartAReplacement()
    {
        var path = Path.GetTempFileName();
        var settings = CoreSettings.ServiceProvider; var overridePath = CoreModArt.AudioOverridePathProvider;
        var suppressed = CoreAudio.IsOutputSuppressedProvider; var stoppable = CoreAudio.PlayStoppableProvider;
        var oneShot = CoreAudio.PlayOneShotProvider;
        try
        {
            CoreSettings.ServiceProvider = null; CoreModArt.AudioOverridePathProvider = _ => path;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = null; CoreAudio.PlayOneShotProvider = null;
            int played = 0, attempts = 0;
            using var audio = new FirstShowAudio(preview: true);
            audio.CreateCuePlayer = () => (_, _, _) => { played++; return () => { attempts++; throw new IOException("stop failed"); }; };
            audio.Cue("click"); audio.Cue("click");
            Assert.Equal(1, played); Assert.Equal(1, attempts);
            audio.Dispose(); Assert.Equal(2, attempts);
        }
        finally
        {
            CoreSettings.ServiceProvider = settings; CoreModArt.AudioOverridePathProvider = overridePath;
            CoreAudio.IsOutputSuppressedProvider = suppressed; CoreAudio.PlayStoppableProvider = stoppable;
            CoreAudio.PlayOneShotProvider = oneShot; File.Delete(path);
        }
    }

    [Fact]
    public void NonPreviewWithoutSettingsIsSilentAndPreviewUsesAnExistingBackend()
    {
        var path = Path.GetTempFileName();
        var settings = CoreSettings.ServiceProvider;
        var overridePath = CoreModArt.AudioOverridePathProvider;
        var suppressed = CoreAudio.IsOutputSuppressedProvider;
        var stoppable = CoreAudio.PlayStoppableProvider;
        try
        {
            CoreSettings.ServiceProvider = null;
            CoreModArt.AudioOverridePathProvider = _ => path;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            int played = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { played++; return () => { }; };
            using var regular = new FirstShowAudio();
            regular.Cue("click"); Assert.Equal(0, played);
            using var preview = new FirstShowAudio(preview: true);
            preview.CreateCuePlayer = () => throw new Exception("Existing backend should be used");
            preview.Cue("click"); Assert.Equal(1, played);
        }
        finally
        {
            CoreSettings.ServiceProvider = settings; CoreModArt.AudioOverridePathProvider = overridePath;
            CoreAudio.IsOutputSuppressedProvider = suppressed; CoreAudio.PlayStoppableProvider = stoppable; File.Delete(path);
        }
    }
}
