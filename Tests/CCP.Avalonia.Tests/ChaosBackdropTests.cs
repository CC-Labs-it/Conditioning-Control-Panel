using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosBackdropTests
{
    [Fact]
    public async Task Story_run_shows_plate_swaps_depth_and_panic_closes_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var narrative = settings.NarrativeModeEnabled;
            var backdrop = settings.BackdropEnabled;
            try
            {
                settings.NarrativeModeEnabled = true;
                settings.BackdropEnabled = true;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false, PlayMode = ChaosPlayMode.Story });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var plate = Assert.IsType<ChaosBackdropWindow>(ChaosBackdropWindow.Active);
                Assert.True(plate.IsVisible);
                Assert.Equal(1, plate.Depth);
                plate.SetDepth(2);
                Assert.Equal(2, plate.Depth);
                ChaosRunHost.ForceShutdown();
                Assert.Null(ChaosBackdropWindow.Active);
                Assert.False(plate.IsVisible);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                settings.NarrativeModeEnabled = narrative;
                settings.BackdropEnabled = backdrop;
            }
            return Task.CompletedTask;
        });
    }
}
