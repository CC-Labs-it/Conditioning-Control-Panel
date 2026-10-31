using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;
public class BrainDrainAudioTests
{
    private sealed class Clock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Timer();
        private sealed class Timer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class Voice : MindWipePlayer.IVoice
    {
        public double Gain;
        public bool Disposed;
        public double Volume { set => Gain = value; }
        public void Dispose() => Disposed = true;
    }
    [Fact]
    public void ClipsFinishLiveGainFollowsRestoreAndPanicStops()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        var service = new ConditioningControlPanel.Services.SettingsService();
        var voice = new Voice(); int plays = 0; Action? ended = null;
        var player = new BrainDrainPlayer((_, gain, loop, done) =>
        { Assert.False(loop); plays++; voice.Gain = gain; ended = done; return voice; }, new Clock(), () => 0);
        var oldInstance = BrainDrainPlayer.Instance;
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CorePaths.EffectiveAssetsProvider = () => root;
            service.Current.BrainDrainEnabled = true;
            service.Current.MasterVolume = 50; service.Current.BrainDrainVolume = 40;
            BrainDrainPlayer.Instance = player;
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.Equal(1, plays); Assert.Equal(.2, voice.Gain, 8);
            player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(1, plays);
            service.Current.BrainDrainVolume = 80; Assert.Equal(.4, voice.Gain, 8);
            service.RestoreFrom(new ConditioningControlPanel.Models.AppSettings
                { BrainDrainEnabled = true, MasterVolume = 100, BrainDrainVolume = 50 });
            Assert.Equal(.5, voice.Gain, 8);
            service.Current.BrainDrainVolume = 20; Assert.Equal(.2, voice.Gain, 8);
            ended!(); player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(2, plays);
            // Exercise the registered panic surface without creating any windows.
            Assert.Single(PanicSurfaces.All, x => x.Id == "brain-drain-audio").Stop(null);
            Assert.True(voice.Disposed); Assert.False(player.IsRunning);
            player.Tick(TimeSpan.FromSeconds(5)); Assert.Equal(2, plays);
        }
        finally { player.Stop(); BrainDrainPlayer.Instance = oldInstance; Directory.Delete(root, true); }
    }
    [Fact]
    public void StopDuringDecoderCreationDisposesTheLateVoice()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, "braindrain"));
        File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
        var voice = new Voice(); BrainDrainPlayer? player = null;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            CoreSettings.Current.BrainDrainEnabled = true;
            player = new BrainDrainPlayer((_, _, _, _) => { player!.Stop(); return voice; }, new Clock(), () => 0);
            player.Start(); player.Tick(TimeSpan.FromSeconds(5));
            Assert.True(voice.Disposed); Assert.False(player.IsRunning);
        }
        finally { player?.Stop(); Directory.Delete(root, true); }
    }
}
