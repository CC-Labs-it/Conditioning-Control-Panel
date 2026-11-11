using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosThoughtCallerTests
{
    [Fact]
    public async Task ThoughtsFollowActiveRunClockAndReset()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var launch = ChaosRunHost.LaunchThought;
            var pool = CoreSettings.Current.BouncingTextPool;
            var calls = new List<(double, double, double, string)>();
            void Ticks(int n) { for (int i = 0; i < n; i++) ChaosRunHost.Tick(); }
            void Start() { ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 300, WaveCount = 3 }); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); }
            try
            {
                ChaosRunHost.LaunchThought = (a, b, c, d) => { calls.Add((a, b, c, d)); ChaosDvdOverlay.Flock.Launch(a, b, c, text: d); };
                CoreSettings.Current.BouncingTextPool = new() { ["selected"] = true, ["disabled"] = false };
                Start();
                Ticks(20);
                Assert.Empty(calls);
                var toy = new ChaosToyState { Id = "porn_dvd" };
                ChaosRunHost.State!.ActiveToys.Add(toy);
                ChaosRunHost.State.IntrusiveThoughtsSec = 4;
                Ticks(19);
                Assert.Empty(calls);
                ChaosRunHost.ToggleManualPause(); Ticks(20); Assert.Empty(calls); ChaosRunHost.ToggleManualPause();
                Ticks(1);
                Assert.Equal((4d, 1.3, 0.8, "selected"), Assert.Single(calls));
                Assert.True(ChaosDvdOverlay.AnyActive);
                Assert.False(toy.IsEffectActive);
                CoreSettings.Current.BouncingTextPool = new() { ["disabled"] = false };
                Ticks(20);
                Assert.Equal((4d, 1.3, 0.8, "GIVE IN"), calls[1]);
                Ticks(19);
                ChaosRunHost.ForceShutdown(); Ticks(20); Assert.Equal(2, calls.Count);
                Start(); ChaosRunHost.State!.IntrusiveThoughtsSec = 3;
                Ticks(19); Assert.Equal(2, calls.Count);
                Ticks(1); Assert.Equal((3d, 1.3, 0.8, "GIVE IN"), calls[2]);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.LaunchThought = launch; CoreSettings.Current.BouncingTextPool = pool; }
            return Task.CompletedTask;
        });
    }
}
