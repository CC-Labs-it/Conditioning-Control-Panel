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
        public volatile int Published = -1;
        public void Publish(int volume) => Published = volume;
        public int Volume { set { Entered.Set(); Gate.Wait(TimeSpan.FromSeconds(10)); Volatile.Write(ref _volume, value); } }
        public void Dispose() { }
    }

    [Fact]
    public void GainRetriesAndUnmutesOnlyAtTheCurrentTarget()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int target = 100, attempts = 0, allow = 0, unmutedAt = -1, thread = 0, unmutes = 0;
        var caller = Environment.CurrentManagedThreadId; var gain = new LayeredAudio.VlcLayerPlayer.GainWorker(() => Volatile.Read(ref target) < 0 ? null : Volatile.Read(ref target), v =>
        {
            Volatile.Write(ref thread, Environment.CurrentManagedThreadId);
            if (Interlocked.Increment(ref attempts) <= 25) return false;
            if (v == 100) { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); }
            return v == 100 || Volatile.Read(ref allow) == 1;
        }, v => { Interlocked.Increment(ref unmutes); Volatile.Write(ref unmutedAt, v); return System.Threading.Tasks.Task.FromResult(false); });
        try {
            var clock = Stopwatch.StartNew(); gain.Kick(); gain.Kick();
            Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(200));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5))); // recovered after 25 failed writes
            Assert.Equal(-1, Volatile.Read(ref unmutedAt)); Assert.NotEqual(caller, Volatile.Read(ref thread));
            Volatile.Write(ref target, 10); gain.Kick(); release.Set();
            Assert.False(SpinWait.SpinUntil(() => Volatile.Read(ref unmutedAt) >= 0, 500));
            Volatile.Write(ref allow, 1); Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref unmutedAt) == 10, 5000));
            Assert.Equal(1, Volatile.Read(ref unmutes));
            var fence = new LayeredAudio.VlcLayerPlayer.GainFence(10); var audible = -1;
            entered.Reset(); release.Reset(); using var publishing = new ManualResetEventSlim();
            var held = System.Threading.Tasks.Task.Run(() => fence.TryUnmute(10, () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(5)); audible = fence.Value; })); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var publish = System.Threading.Tasks.Task.Run(() => { publishing.Set(); fence.Publish(0); }); Assert.True(publishing.Wait(TimeSpan.FromSeconds(5))); Assert.False(publish.Wait(TimeSpan.FromMilliseconds(500)));
            release.Set(); Assert.True(held.Wait(TimeSpan.FromSeconds(5))); Assert.True(publish.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(10, audible); Assert.Equal(0, fence.Value);
        } finally { Volatile.Write(ref target, -1); release.Set(); }
    }

    [Fact]
    public void BlockedLayerWriteDoesNotHoldBackAnotherLayersDuck()
    {
        var s = CoreSettings.Current;                            // TestIsolation puts it back
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
            var clock = Stopwatch.StartNew();
            layers.ApplyDuck(0.8f);                              // a cue starts while A is stuck
            Assert.True(clock.Elapsed < TimeSpan.FromMilliseconds(200), "the duck never waits for a layer write");
            Assert.Equal(46, players[0].Published);              // A's ducked gain is published before its write, cbrt(0.1)
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
            layers.ApplyDuck(0.8f);                              // 0.5 x 0.2 = 0.1
            Assert.True(layers.VolumeUpdates.Wait(TimeSpan.FromSeconds(5)));
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
