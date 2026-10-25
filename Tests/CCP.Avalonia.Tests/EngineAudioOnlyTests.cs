using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EngineAudioOnlyTests
{
    [Fact]
    public void AudioOnlyForcesTheBedAndStopPreservesOnlyTheStandaloneBed() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldAudio = LayeredAudio.Instance;
        var oldMixer = LibVlcAudio.Instance;
        var mixer = new LibVlcAudio(_ => "[]"); // no OS mixer access
        LibVlcAudio.Instance = mixer;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var file = Path.GetTempFileName();
        var player = new Player();
        using var audio = new LayeredAudio(_ => player = new Player());
        LayeredAudio.Instance = audio;
        var s = CoreSettings.Current;
        s.AudioOnlySession = true; s.AudioLayersEnabled = false;
        s.AudioLayers.Add(new AudioLayerTrack { Path = file, Enabled = true, Volume = 50 });
        var shell = new MainShellWindow();
        try
        {
            shell.OnEngineStarted();
            Assert.True(audio.IsPlaying);
            mixer.Duck(80);
            Assert.True(mixer.IsDucked);
            shell.OnEngineStopped();
            Assert.False(mixer.IsDucked);
            Assert.False(audio.IsPlaying);
            Assert.True(player.Disposed);
            s.AudioLayersEnabled = true;
            shell.OnEngineStarted();
            shell.OnEngineStopped();
            Assert.True(audio.IsPlaying);
            Assert.False(player.Disposed);
            s.AudioOnlySession = false;
            audio.Stop();
            shell.OnEngineStarted();
            Assert.False(audio.IsPlaying); // a visual run does not start the standalone bed
        }
        finally
        {
            shell.Close(); LayeredAudio.Instance = oldAudio; LibVlcAudio.Instance = oldMixer;
            CoreSettings.ServiceProvider = oldSettings; File.Delete(file);
        }
    });

    private sealed class Player : LayeredAudio.ILayerPlayer
    {
        public int Volume { private get; set; }
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
}
