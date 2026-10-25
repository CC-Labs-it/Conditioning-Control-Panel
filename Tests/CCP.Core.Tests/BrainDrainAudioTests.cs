using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BrainDrainAudioTests
{
    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, .5)]
    public void CadenceAndGainMatchWpf(bool high, double seconds)
    {
        Assert.Equal(seconds, BrainDrainSchedule.TickInterval(high).TotalSeconds);
        Assert.Equal(seconds / 120, BrainDrainSchedule.Probability(50, TimeSpan.FromSeconds(seconds)));
        Assert.Equal(.2f, BrainDrainSchedule.EffectiveVolume(50, 40));
        Assert.Equal(0, BrainDrainSchedule.EffectiveVolume(-10, 100));
        Assert.Equal(1, BrainDrainSchedule.ClampIntensity(0));
        Assert.Equal(100, BrainDrainSchedule.ClampIntensity(150));
    }

    [Fact]
    public void DiscoveryCreatesPrimaryAndAssetsWinCaseInsensitiveNames()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var primary = Path.Combine(root, "primary");
            var legacy = Path.Combine(root, "legacy");
            Assert.Empty(BrainDrainSchedule.DiscoverClips(primary, legacy));
            Assert.True(Directory.Exists(primary));
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(primary, "clip.WAV"), "");
            File.WriteAllText(Path.Combine(legacy, "CLIP.wav"), "");
            File.WriteAllText(Path.Combine(legacy, "other.ogg"), "");
            File.WriteAllText(Path.Combine(primary, "ignored.txt"), "");
            var clips = BrainDrainSchedule.DiscoverClips(primary, legacy);
            Assert.Equal(2, clips.Length);
            Assert.Contains(Path.Combine(primary, "clip.WAV"), clips);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task StopRetiresInFlightBuildEvenAfterRestart()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        await WithPlayer(async make =>
        {
            var stopped = 0;
            var player = make((_, _, _, _) =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
                return new BrainDrainPlayer.Voice(() => stopped++, _ => { });
            });
            player.Start();
            var build = Task.Run(player.Tick);
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                player.Stop();
                player.Start();
            }
            finally { release.Set(); }
            await build;
            Assert.Equal(1, stopped);
            player.Stop();
        });
    }

    [Fact]
    public Task PlayingClipIsNotDisplacedAndStopIsUnconditional() => WithPlayer(async make =>
    {
        var plays = 0;
        var stopped = 0;
        double gain = -1;
        Action? ended = null;
        var player = make((_, _, _, finished) =>
        {
            plays++;
            ended = finished;
            return new BrainDrainPlayer.Voice(() => stopped++, v => gain = v);
        });
        player.Start();
        player.Tick();
        player.Tick();
        Assert.Equal(1, plays);
        CoreSettings.Current.MasterVolume = 50;
        CoreSettings.Current.BrainDrainVolume = 40;
        player.RefreshVolume();
        Assert.Equal((double).2f, gain);
        ended!();
        player.Tick();
        Assert.Equal(2, plays);
        player.Stop();
        player.Stop();
        Assert.Equal(1, stopped);
        player.Tick();
        Assert.Equal(2, plays);
        await Task.CompletedTask;
    });

    [Fact]
    public Task DisabledAndSuppressedStayQuietAndSynchronousEndAllowsNextClip() => WithPlayer(async make =>
    {
        var plays = 0;
        var player = make((_, _, _, ended) =>
        {
            plays++;
            ended();
            return new BrainDrainPlayer.Voice(() => { }, _ => { });
        });
        CoreSettings.Current.BrainDrainEnabled = false;
        player.Start();
        Assert.False(player.IsRunning);
        CoreSettings.Current.BrainDrainEnabled = true;
        player.Start();
        var old = CoreAudio.IsOutputSuppressedProvider;
        try
        {
            CoreAudio.IsOutputSuppressedProvider = () => true;
            player.Tick();
            Assert.Equal(0, plays);
            CoreAudio.IsOutputSuppressedProvider = () => false;
            player.Tick();
            player.Tick();
            Assert.Equal(2, plays);
        }
        finally { CoreAudio.IsOutputSuppressedProvider = old; }
        await Task.CompletedTask;
    });

    private static async Task WithPlayer(Func<Func<Func<string, double, bool, Action, BrainDrainPlayer.Voice?>, BrainDrainPlayer>, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var oldPath = CorePaths.EffectiveAssetsProvider;
        var s = CoreSettings.Current;
        var old = (s.BrainDrainEnabled, s.MasterVolume, s.BrainDrainVolume);
        BrainDrainPlayer? player = null;
        try
        {
            CorePaths.EffectiveAssetsProvider = () => root;
            Directory.CreateDirectory(Path.Combine(root, "braindrain"));
            File.WriteAllText(Path.Combine(root, "braindrain", "clip.wav"), "");
            s.BrainDrainEnabled = true;
            await test(play => player = new BrainDrainPlayer(play, roll: () => 0));
        }
        finally
        {
            player?.Stop();
            CorePaths.EffectiveAssetsProvider = oldPath;
            (s.BrainDrainEnabled, s.MasterVolume, s.BrainDrainVolume) = old;
            Directory.Delete(root, true);
        }
    }
}
