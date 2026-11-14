using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF StartStop.cs:533-547 restores the in-process mixer before finishing Stop. TestIsolation puts
/// back the settings and statics these tests assign.</summary>
public sealed class EngineAudioTeardownTests
{
    private sealed class SlowPlayer : LayeredAudio.ILayerPlayer
    {
        public readonly ManualResetEventSlim Restoring = new(), Release = new(), Unmuting = new(), ReleaseUnmute = new();
        public readonly LayeredAudio.VlcLayerPlayer.GainFence Fence = new(100); public void Publish(int volume) => Fence.Publish(volume);
        public bool BlockRestore;
        private int _volume, _writes;
        public int Writes => Volatile.Read(ref _writes);
        public int Current => Volatile.Read(ref _volume);
        public int Volume { set {
            if (value == 100 && BlockRestore) { Restoring.Set(); Release.Wait(TimeSpan.FromSeconds(10)); }
            Volatile.Write(ref _volume, value); Interlocked.Increment(ref _writes);
        } }
        public void Dispose() { }
    }

    [Fact]
    public async Task StopFinishesWhileLayersRestoreSlowlyAndANewDuckWins()
    {
        var s = CoreSettings.Current;
        var file = Path.GetTempFileName();
        var player = new SlowPlayer();
        var layers = LayeredAudio.Instance = new LayeredAudio((_, volume) => { player.Volume = volume; return player; });
        var audio = LibVlcAudio.Instance = new LibVlcAudio(_ => "[]");
        MainShellWindow? shell = null;
        Task? stopping = null;
        try
        {
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                EngineAudioLifecycleTests.Headless();
                s.AudioOnlySession = false;
                s.AudioLayersEnabled = true;
                s.AudioLayersMasterVolume = s.MasterVolume = 100;
                s.AudioLayers = new() { new AudioLayerTrack { Path = file, Volume = 100 } };
                shell = EngineAudioLifecycleTests.CreateShell();
                layers.Start();
                shell.StartEngine();
                audio.Duck(50);
                return Task.CompletedTask;
            });
            await layers.VolumeUpdates.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(79, player.Current); var applied = -1;
            var gain = new LayeredAudio.VlcLayerPlayer.GainWorker(() => player.Fence.Value, v => { Volatile.Write(ref applied, v); return true; },
                v => player.Unmuting.IsSet ? Task.FromResult(false) : player.Fence.QueueUnmute(v, () => { player.Unmuting.Set(); player.ReleaseUnmute.Wait(TimeSpan.FromSeconds(10)); return true; }));
            gain.Kick(); Assert.True(player.Unmuting.Wait(TimeSpan.FromSeconds(5)));
            player.BlockRestore = true;
            stopping = AvaloniaTestDispatcher.RunAsync(() => { MainShellWindow.StopEngine(); return Task.CompletedTask; });
            Assert.True(player.Restoring.Wait(TimeSpan.FromSeconds(5)), "the slow restore must begin");
            await stopping.WaitAsync(TimeSpan.FromSeconds(3)); // teardown must finish before the volume setter returns
            Assert.False(CoreEngine.IsRunning);
            Assert.True(layers.IsPlaying); // standalone master remains on
            player.ReleaseUnmute.Set(); Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref applied) == 100, 5000));
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
            player.ReleaseUnmute.Set(); player.Release.Set();
            if (stopping != null) await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            audio.ForceUnduck();
            await layers.VolumeUpdates.WaitAsync(TimeSpan.FromSeconds(5));
            await AvaloniaTestDispatcher.RunAsync(() => { CoreEngine.Stop(); shell?.Close(); return Task.CompletedTask; });
            layers.Dispose();
            audio.Shutdown();
            File.Delete(file);
        }
    }
}
