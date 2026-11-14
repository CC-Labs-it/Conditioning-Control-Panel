using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF StartStop.cs:365-367, 533-547 and Presets.cs:1617-1625. TestIsolation puts back the
/// settings and statics these tests assign.</summary>
public sealed class EngineAudioLifecycleTests
{
    internal static AppSettings Headless()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        return CoreSettings.Current;
    }

    internal static MainShellWindow CreateShell()
    {
        var settings = CoreSettings.Current;
        settings.PanicKeyEnabled = false; settings.OfflineMode = true;
        var shell = new MainShellWindow(); shell.Show();
        // The app seeds both hooks (App.axaml.cs); the trunk starts the audio-only bed from StartedHook.
        CoreEngine.StartedHook = shell.OnEngineStarted;
        CoreEngine.StoppedHook = shell.OnEngineStopped; return shell;
    }

    private sealed class Player : LayeredAudio.ILayerPlayer
    {
        public int Volume { set { } }
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public Task AudioOnlyStartAndStopRespectStandaloneMasterAndUnduck(bool audioOnly, bool master) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = Headless();
        var file = Path.GetTempFileName();
        var opened = new List<Player>();
        var layers = LayeredAudio.Instance = new LayeredAudio((_, _) => { var p = new Player(); opened.Add(p); return p; });
        var audio = LibVlcAudio.Instance = new LibVlcAudio(_ => "[]");
        MainShellWindow? shell = null;
        try
        {
            s.AudioOnlySession = audioOnly; s.AudioLayersEnabled = master;
            s.AudioLayers = new() { new AudioLayerTrack { Path = file } };
            shell = CreateShell();
            shell.StartEngine();
            Assert.True(CoreEngine.IsRunning);
            Assert.Equal(audioOnly, layers.IsPlaying);
            Assert.Equal(audioOnly ? 1 : 0, opened.Count);
            audio.Duck(80);
            Assert.True(audio.IsDucked);
            MainShellWindow.StopEngine();
            Assert.False(audio.IsDucked);
            Assert.Equal(audioOnly && master, layers.IsPlaying);
            if (audioOnly) Assert.Equal(!master, opened[0].Disposed);
        }
        finally
        {
            CoreEngine.Stop(); shell?.Close();
            layers.Dispose();
            audio.Shutdown();
            File.Delete(file);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SessionStartsAudioBedOnlyWhenItStartsTheEngine(bool alreadyRunning) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = Headless();
        var file = Path.GetTempFileName();
        var opened = new List<Player>();
        var layers = LayeredAudio.Instance = new LayeredAudio((_, _) => { var p = new Player(); opened.Add(p); return p; });
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        MainShellWindow? shell = null;
        try
        {
            s.AudioOnlySession = true; s.AudioLayersEnabled = false;
            s.AudioLayers = new() { new AudioLayerTrack { Path = file } };
            shell = CreateShell();
            if (alreadyRunning) shell.StartEngine();
            shell.StartSession(new Session { Id = "audio_lifecycle", Name = "Audio", DurationMinutes = 10 });
            Assert.True(runner.IsRunning);
            var player = Assert.Single(opened);
            Assert.False(player.Disposed);
            Assert.True(layers.IsPlaying);
            runner.Stop();
            Assert.True(player.Disposed);
            Assert.False(layers.IsPlaying);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop(); shell?.Close();
            layers.Dispose();
            File.Delete(file);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task StopRestoresAudioEvenWhenTheEngineWasAlreadyIdle(bool master, bool checkUnduck) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = Headless();
        var file = Path.GetTempFileName();
        var player = new Player();
        var layers = LayeredAudio.Instance = new LayeredAudio((_, _) => player);
        var audio = LibVlcAudio.Instance = new LibVlcAudio(_ => "[]");
        MainShellWindow? shell = null;
        try
        {
            s.AudioLayersEnabled = master;
            s.AudioLayers = new() { new AudioLayerTrack { Path = file } };
            shell = CreateShell();
            Assert.False(CoreEngine.IsRunning);
            layers.Start(ignoreMasterToggle: true);
            audio.Duck(80);
            Assert.True(layers.IsPlaying && audio.IsDucked);
            CoreEngine.Stop(); // WPF StopEngineCore also cleans up when already idle.
            if (checkUnduck) Assert.False(audio.IsDucked);
            else { Assert.Equal(master, layers.IsPlaying); Assert.Equal(!master, player.Disposed); }
        }
        finally
        {
            CoreEngine.Stop(); shell?.Close();
            layers.Dispose();
            audio.Shutdown();
            File.Delete(file);
        }
        return Task.CompletedTask;
    });
}
