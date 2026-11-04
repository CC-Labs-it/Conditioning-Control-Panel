using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RabbitCallerDriverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Unavailable_summon_is_declined_without_spending_cooldown(bool key) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                Assert.True(ChaosRunHost.IsDescending);
                var toy = new ChaosToyState { Id = "rabbit_caller", KeyLabel = "Q", CooldownSec = 20 };
                ChaosRunHost.State!.ActiveToys.Clear();
                ChaosRunHost.State.ActiveToys.Add(toy);
                if (key) ChaosRunHost.OnToyKey("Q");
                else ChaosRunHost.UseToyById("rabbit_caller");
                Assert.Equal(0, toy.CooldownRemainingSec);
                Assert.False(toy.IsEffectActive);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
}
