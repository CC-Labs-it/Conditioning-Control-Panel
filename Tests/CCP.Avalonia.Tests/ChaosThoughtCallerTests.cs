using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosThoughtCallerTests
{
    [Theory]
    [InlineData("en", "GIVE IN")]
    [InlineData("de", "GIB NACH")]
    [InlineData("es", "RÍNDETE")]
    [InlineData("fr", "CÈDE")]
    [InlineData("it", "CEDI")]
    [InlineData("ja", "身を委ねて")]
    [InlineData("ko", "몸을 맡겨")]
    [InlineData("pt-BR", "RENDA-SE")]
    [InlineData("ru", "ПОДДАЙСЯ")]
    [InlineData("zh-CN", "顺从吧")]
    public async Task EmptyPoolLaunchesLocalizedFallback(string language, string expected)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var launch = ChaosRunHost.LaunchThought;
            var pool = CoreSettings.Current.BouncingTextPool;
            var savedLanguage = LocalizationManager.Instance.CurrentLanguage;
            var calls = new List<string>();
            try
            {
                LocalizationManager.Instance.SetLanguage(language);
                CoreSettings.Current.BouncingTextPool = new() { ["disabled"] = false };
                ChaosRunHost.LaunchThought = (_, _, _, text) => calls.Add(text);
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosRunHost.State!.IntrusiveThoughtsSec = 3;
                for (int i = 0; i < 20; i++) ChaosRunHost.Tick();
                Assert.Equal(expected, Assert.Single(calls));
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosRunHost.LaunchThought = launch;
                CoreSettings.Current.BouncingTextPool = pool;
                LocalizationManager.Instance.SetLanguage(savedLanguage);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ThoughtsFollowActiveRunClockAndReset()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var launch = ChaosRunHost.LaunchThought;
            var pool = CoreSettings.Current.BouncingTextPool;
            var savedLanguage = LocalizationManager.Instance.CurrentLanguage;
            var calls = new List<(double, double, double, string)>();
            void Ticks(int n) { for (int i = 0; i < n; i++) ChaosRunHost.Tick(); }
            void Start() { ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 300, WaveCount = 3 }); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); }
            try
            {
                LocalizationManager.Instance.SetLanguage("en");
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
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.LaunchThought = launch; CoreSettings.Current.BouncingTextPool = pool; LocalizationManager.Instance.SetLanguage(savedLanguage); }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService.cs:3009-3011: only a maxed Intrusive Thoughts arms the rabbit split (#3555).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MaxedCapstoneArmsTheRabbitSplit(bool maxed)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var pool = CoreSettings.Current.BouncingTextPool;
            var (can, prepare, sfx) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx);
            try
            {
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                CoreSettings.Current.BouncingTextPool = new() { ["selected"] = true };
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 300, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosRunHost.State!.IntrusiveThoughtsSec = 4;
                if (maxed) ChaosRunHost.State.MaxedBoons.Add("intrusive_thoughts");
                for (int i = 0; i < 20; i++) ChaosRunHost.Tick();
                var thought = Assert.Single(ChaosDvdOverlay.Flock.Active, l => l.IsThought);
                Assert.Equal(maxed, thought.SplitOnRabbit);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx) = (can, prepare, sfx);
                CoreSettings.Current.BouncingTextPool = pool;
            }
            return Task.CompletedTask;
        });
    }
}
