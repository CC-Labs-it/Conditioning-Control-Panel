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

// WPF MainWindow.StartStop.cs:365-367,533-547. Fake player and mixer avoid OS audio access.
public sealed class EngineAudioOnlyTests
{
    [Fact]
    public void AudioOnlyForcesTheBedAndStopPreservesOnlyTheStandaloneBed() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var (oldStart, oldStop) = (CoreEngine.StartedHook, CoreEngine.StoppedHook);
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
        s.FlashEnabled = s.SubliminalEnabled = s.BouncingTextEnabled = s.BubblesEnabled = false;
        s.MandatoryVideosEnabled = s.LockCardEnabled = s.BubbleCountEnabled = s.PopQuizEnabled = s.MindWipeEnabled = false;
        s.AudioOnlySession = true; s.AudioLayersEnabled = false;
        s.AudioLayers.Add(new AudioLayerTrack { Path = file, Enabled = true, Volume = 50 });
        var shell = new MainShellWindow();
        CoreEngine.StartedHook = shell.OnEngineStarted;
        CoreEngine.StoppedHook = shell.OnEngineStopped;
        try
        {
            CoreEngine.Start();
            Assert.True(audio.IsPlaying);
            mixer.Duck(80);
            Assert.True(mixer.IsDucked);
            CoreEngine.Stop();
            Assert.False(mixer.IsDucked);
            Assert.False(audio.IsPlaying);
            Assert.True(player.Disposed);
            s.AudioLayersEnabled = true;
            CoreEngine.Start();
            CoreEngine.Stop();
            Assert.True(audio.IsPlaying);
            Assert.False(player.Disposed);
            s.AudioOnlySession = false;
            audio.Stop();
            CoreEngine.Start();
            Assert.False(audio.IsPlaying); // a visual run does not start the standalone bed
            CoreEngine.Stop();
            mixer.Duck(80);
            CoreEngine.Stop(); // panic cleanup also runs when the engine is already stopped
            Assert.False(mixer.IsDucked);
        }
        finally
        {
            CoreEngine.Stop();
            (CoreEngine.StartedHook, CoreEngine.StoppedHook) = (oldStart, oldStop);
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
