using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF StartStop.cs:533-547 restores the in-process mixer before finishing Stop.</summary>
public sealed class EngineAudioTeardownTests
{
    private sealed class SlowPlayer : LayeredAudio.ILayerPlayer
    {
        public readonly ManualResetEventSlim Restoring = new(), Release = new();
        public bool BlockRestore;
        private int _volume, _writes;
        public int Writes => Volatile.Read(ref _writes);
        public int Current => Volatile.Read(ref _volume);
        public int Volume
        {
            set
            {
                if (value == 100 && BlockRestore)
                {
                    Restoring.Set();
                    Release.Wait(TimeSpan.FromSeconds(10));
                }
                Volatile.Write(ref _volume, value);
                Interlocked.Increment(ref _writes);
            }
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task StopFinishesWhileLayersRestoreSlowlyAndANewDuckWins()
    {
        var s = CoreSettings.Current;
        var file = Path.GetTempFileName();
        var oldLayers = LayeredAudio.Instance;
        var oldAudio = LibVlcAudio.Instance;
        var oldHook = CoreEngine.StoppedHook;
        var player = new SlowPlayer();
        var layers = LayeredAudio.Instance = new LayeredAudio((_, volume) => { player.Volume = volume; return player; });
        var audio = LibVlcAudio.Instance = new LibVlcAudio(_ => "[]");
        MainShellWindow? shell = null;
        Task? stopping = null;
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                s.AudioOnlySession = false;
                s.AudioLayersEnabled = true;
                s.AudioLayersMasterVolume = s.MasterVolume = 100;
                s.AudioLayers = new() { new AudioLayerTrack { Path = file, Volume = 100 } };
                s.PanicKeyEnabled = false;
                s.OfflineMode = true;
                shell = new MainShellWindow();
                shell.Show();
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                layers.Start();
                shell.StartEngine();
                audio.Duck(50);
                return Task.CompletedTask;
            });
            await layers.VolumeUpdates.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(79, player.Current);
            player.BlockRestore = true;
            stopping = AvaloniaTestDispatcher.RunAsync(() => { MainShellWindow.StopEngine(); return Task.CompletedTask; });
            Assert.True(player.Restoring.Wait(TimeSpan.FromSeconds(5)), "the slow restore must begin");
            await stopping.WaitAsync(TimeSpan.FromSeconds(3)); // teardown must finish before the volume setter returns
            Assert.False(CoreEngine.IsRunning);
            Assert.True(layers.IsPlaying); // standalone master remains on
            var writes = player.Writes;
            s.MasterVolume = 25; layers.SetMasterVolumeLive();
            s.MasterVolume = 50; layers.SetMasterVolumeLive();
            s.MasterVolume = 100;
            audio.Duck(80); // newer duck must not be undone by the still-blocked restore
            player.Release.Set();
            await layers.VolumeUpdates.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(audio.IsDucked);
            Assert.Equal(58, player.Current);
            Assert.Equal(writes + 2, player.Writes); // blocked restore, then latest gain; stale sliders skipped
        }
        finally
        {
            player.Release.Set();
            if (stopping != null) await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            audio.ForceUnduck();
            await layers.VolumeUpdates.WaitAsync(TimeSpan.FromSeconds(5));
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                CoreEngine.StoppedHook = oldHook;
                CoreEngine.Stop();
                shell?.Close();
                return Task.CompletedTask;
            });
            layers.Dispose();
            audio.Shutdown();
            LayeredAudio.Instance = oldLayers;
            LibVlcAudio.Instance = oldAudio;
            File.Delete(file);
        }
    }

    [Fact]
    public async Task WindowsDuckOrderingKeepsTheNewDuckAfterASlowRestore()
    {
        var s = CoreSettings.Current;
        var file = Path.GetTempFileName();
        var oldLayers = LayeredAudio.Instance;
        var player = new SlowPlayer();
        var layers = LayeredAudio.Instance = new LayeredAudio((_, volume) => { player.Volume = volume; return player; });
        var duck = new WindowsDucker(body => body(Array.Empty<DuckSession>()), _ => "", null, () => false, 999);
        try
        {
            s.AudioLayersEnabled = true;
            s.AudioLayersMasterVolume = s.MasterVolume = 100;
            s.AudioLayers = new() { new AudioLayerTrack { Path = file, Volume = 100 } };
            layers.Start();
            duck.Duck(50);
            await layers.VolumeUpdates.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(79, player.Current);
            player.BlockRestore = true;
            var started = System.Diagnostics.Stopwatch.StartNew();
            duck.ForceUnduck();
            Assert.True(started.Elapsed < TimeSpan.FromSeconds(3), "restore must not block the caller");
            Assert.True(player.Restoring.Wait(TimeSpan.FromSeconds(5)));
            duck.Duck(80);
            player.Release.Set();
            await layers.VolumeUpdates.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(duck.IsDucked);
            Assert.Equal(58, player.Current);
        }
        finally
        {
            player.Release.Set();
            duck.ForceUnduck();
            await layers.VolumeUpdates.WaitAsync(TimeSpan.FromSeconds(5));
            duck.Shutdown();
            layers.Dispose();
            LayeredAudio.Instance = oldLayers;
            File.Delete(file);
        }
    }
}
