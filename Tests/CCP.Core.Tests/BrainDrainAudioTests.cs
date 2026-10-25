using System;
using System.IO;
using System.Linq;
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

    [Fact]
    public Task PresenceHooksFireOncePerRealStartAndStop() => WithPlayer(async _ =>
    {
        var started = 0;
        var stopped = 0;
        var player = new BrainDrainPlayer((_, _, _, _) => null, roll: () => 1)
        {
            Started = () => started++,
            Stopped = () => stopped++,
        };
        try
        {
            player.Stop();                 // never started: WPF returns before SetIdleActivity
            Assert.Equal(0, stopped);
            player.Start();
            player.Start();                // already running
            Assert.Equal(1, started);
            player.Stop();
            player.Stop();
            Assert.Equal(1, stopped);
        }
        finally { player.Stop(); }
        await Task.CompletedTask;
    });

    [Fact]
    public Task SettingsWritesChangeThePlayingClipWithoutASliderHandler() => WithPlayer(async make =>
    {
        double gain = -1;
        var plays = 0;
        var player = make((_, initial, _, _) =>
        {
            plays++;
            gain = initial;
            return new BrainDrainPlayer.Voice(() => { }, v => gain = v);
        });
        var s = CoreSettings.Current;
        s.MasterVolume = 80;
        s.BrainDrainVolume = 40;
        player.Start();
        player.Tick();
        Assert.Equal((double)BrainDrainSchedule.EffectiveVolume(80, 40), gain);
        s.MasterVolume = 0; // hotkey/preset/remote write, no AudioSettingsBinder
        Assert.Equal(0, gain);
        s.MasterVolume = 50;
        Assert.Equal((double).2f, gain);
        s.BrainDrainVolume = 20;
        Assert.Equal((double).1f, gain);
        Assert.Equal(1, plays);
        player.Stop();
        s.MasterVolume = 100;
        Assert.Equal((double).1f, gain); // stopped voices are no longer subscribed
        await Task.CompletedTask;
    });

    [Fact]
    public async Task ReplacedSettingsRefreshTheClipAndDetachTheOldObject()
    {
        var oldProvider = CoreSettings.ServiceProvider;
        // RestoreFrom saves settings.json (and may rotate backups) in the shared sandbox; put the
        // files back afterwards so a later `new SettingsService()` does not load this test's values (P02).
        var dir = CorePaths.UserData;
        var saved = Directory.Exists(dir)
            ? Directory.GetFiles(dir, "settings*").ToDictionary(p => p, File.ReadAllBytes)
            : new System.Collections.Generic.Dictionary<string, byte[]>();
        var service = new SettingsService(); // assembly-owned CCP_USERDATA_DIR sandbox
        CoreSettings.ServiceProvider = () => service;
        try
        {
            await WithPlayer(async make =>
            {
                double gain = -1;
                var player = make((_, _, _, _) => new BrainDrainPlayer.Voice(() => { }, v => gain = v));
                var before = service.Current;
                before.MasterVolume = 100;
                before.BrainDrainVolume = 100;
                player.Start();
                player.Tick();
                service.RestoreFrom(new ConditioningControlPanel.Models.AppSettings
                {
                    MasterVolume = 50, BrainDrainVolume = 40, BrainDrainEnabled = true,
                });
                Assert.Equal((double).2f, gain);
                before.MasterVolume = 0;
                Assert.Equal((double).2f, gain);
                service.Current.MasterVolume = 0;
                Assert.Equal(0, gain);
                player.Stop();
                player.Start();
                player.Tick();
                service.Current.MasterVolume = 50;
                Assert.Equal((double).2f, gain);
                await Task.CompletedTask;
            });
        }
        finally
        {
            CoreSettings.ServiceProvider = oldProvider;
            if (Directory.Exists(dir))
                foreach (var p in Directory.GetFiles(dir, "settings*"))
                    if (!saved.ContainsKey(p)) File.Delete(p);
            foreach (var (p, bytes) in saved) File.WriteAllBytes(p, bytes);
        }
    }

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
