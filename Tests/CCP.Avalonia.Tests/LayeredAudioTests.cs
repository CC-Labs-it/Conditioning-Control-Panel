using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>LayeredAudio's state machine with LibVLC stubbed: WPF LayeredAudioService's start
/// rules, live volume, cooperative duck and stop.</summary>
public sealed class LayeredAudioTests
{
    private sealed class FakePlayer : LayeredAudio.ILayerPlayer
    {
        public string Path = "";
        public int Volume { get; set; } = -1;
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    private sealed class GatedPlayer : LayeredAudio.ILayerPlayer
    {
        public readonly ManualResetEventSlim Gate = new(true), Entered = new();
        private int _volume = -1;
        public int Current => Volatile.Read(ref _volume);
        public int Volume { set { Entered.Set(); Gate.Wait(TimeSpan.FromSeconds(10)); Volatile.Write(ref _volume, value); } }
        public void Dispose() { }
    }

    [Fact]
    public void UnresponsiveOutputRetriesOffTheCallerUntilItRecovers()
    {
        var caller = Environment.CurrentManagedThreadId;
        int attempts = 0, thread = caller;
        var recovered = false;
        using var done = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();
        LayeredAudio.VlcLayerPlayer.RetryVolume(() =>
        {
            Volatile.Write(ref thread, Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref attempts);
            if (!Volatile.Read(ref recovered)) return false;
            done.Set();
            return true;
        });
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "the caller (mantra ramp, UI thread) never waits on the output");
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref attempts) >= 22, 5000), "retries past the old 20-attempt window");
        Assert.NotEqual(caller, Volatile.Read(ref thread));
        Volatile.Write(ref recovered, true);
        Assert.True(done.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void BlockedLayerWriteDoesNotHoldBackAnotherLayersDuck()
    {
        var s = CoreSettings.Current;
        var saved = (s.AudioLayers, s.AudioLayersEnabled, s.AudioLayersMasterVolume, s.MasterVolume);
        var file = Path.GetTempFileName();
        var players = new List<GatedPlayer>();
        try
        {
            var a = new AudioLayerTrack { Path = file, Volume = 100 };
            s.AudioLayers = new List<AudioLayerTrack> { a, new() { Path = file, Volume = 100 } };
            s.AudioLayersMasterVolume = s.MasterVolume = 100;
            var layers = new LayeredAudio((_, volume) => { var p = new GatedPlayer(); p.Volume = volume; players.Add(p); return p; });
            layers.Start(ignoreMasterToggle: true);
            players[0].Entered.Reset();
            players[0].Gate.Reset();
            a.Volume = 50;                                       // the slider stores it, then pushes it live
            layers.SetTrackVolumeLive(a, 50);                    // layer A's write hangs
            Assert.True(players[0].Entered.Wait(TimeSpan.FromSeconds(5)));
            layers.ApplyDuck(0.8f);                              // a cue starts while A is stuck
            Assert.True(SpinWait.SpinUntil(() => players[1].Current == 58, 5000), "B ducks during the cue"); // cbrt(0.2)
            layers.ReleaseDuck();
            Assert.True(SpinWait.SpinUntil(() => players[1].Current == 100, 5000));
            players[0].Gate.Set();
            Assert.True(layers.VolumeUpdates.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(79, players[0].Current);                // A lands on its newest gain, cbrt(0.5)
            layers.Dispose();
        }
        finally
        {
            foreach (var p in players) p.Gate.Set();
            (s.AudioLayers, s.AudioLayersEnabled, s.AudioLayersMasterVolume, s.MasterVolume) = saved;
            File.Delete(file);
        }
    }

    [Fact]
    public void StartVolumeDuckStopFollowWpfRules()
    {
        var s = CoreSettings.Current;
        var saved = (s.AudioLayers, s.AudioLayersEnabled, s.AudioLayersMasterVolume, s.MasterVolume);
        var file = Path.GetTempFileName();
        try
        {
            var a = new AudioLayerTrack { Path = file, Volume = 100 };
            var b = new AudioLayerTrack { Path = file, Volume = 50 };
            s.AudioLayers = new List<AudioLayerTrack>
            {
                a, b,
                new() { Path = file, Enabled = false },          // disabled: skipped
                new() { Path = file + ".missing" },              // missing file: skipped
            };
            s.AudioLayersMasterVolume = 100;
            s.MasterVolume = 100;
            s.AudioLayersEnabled = false;

            var opened = new List<FakePlayer>();
            var layers = new LayeredAudio((p, volume) => { var f = new FakePlayer { Path = p, Volume = volume }; opened.Add(f); return f; });

            layers.Start();                                      // master toggle off: nothing
            Assert.Empty(opened);
            layers.Start(ignoreMasterToggle: true);              // audio-only session path
            Assert.Equal(2, opened.Count);
            Assert.Equal(100, opened[0].Volume);
            Assert.Equal(79, opened[1].Volume);                  // cbrt(0.5): linear gain on LibVLC's cubic scale

            layers.SetTrackVolumeLive(b, 0);
            Assert.True(layers.VolumeUpdates.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, opened[1].Volume);
            s.AudioLayersMasterVolume = 50;
            layers.SetMasterVolumeLive();
            Assert.True(layers.VolumeUpdates.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(79, opened[0].Volume);
            layers.ApplyDuck(0.8f);
            Assert.True(layers.VolumeUpdates.Wait(TimeSpan.FromSeconds(5)));                              // 0.5 x 0.2 = 0.1
            Assert.Equal(46, opened[0].Volume);
            layers.ReleaseDuck();
            Assert.True(layers.VolumeUpdates.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(79, opened[0].Volume);

            s.AudioLayersEnabled = true;
            layers.Restart();                                    // rebuilds from settings
            Assert.True(opened[0].Disposed && opened[1].Disposed);
            Assert.Equal(4, opened.Count);
            layers.Stop();
            Assert.True(opened[2].Disposed && opened[3].Disposed);
            Assert.False(layers.IsPlaying);
            s.AudioLayersEnabled = false;
            layers.Restart();                                    // stopped and master off: stays off
            Assert.Equal(4, opened.Count);

            layers.Start(ignoreMasterToggle: true);              // audio-only bed, master still off
            Assert.Equal(6, opened.Count);
            layers.Restart();                                    // was playing, master off: WPF's Start() refuses
            Assert.True(opened[4].Disposed && opened[5].Disposed);
            Assert.Equal(6, opened.Count);
            Assert.False(layers.IsPlaying);
        }
        finally
        {
            (s.AudioLayers, s.AudioLayersEnabled, s.AudioLayersMasterVolume, s.MasterVolume) = saved;
            File.Delete(file);
        }
    }
}
