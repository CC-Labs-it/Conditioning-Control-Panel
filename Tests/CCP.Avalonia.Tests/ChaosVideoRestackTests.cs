using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosVideoRestackTests
{
    [Fact]
    public async Task Video_start_settle_and_click_restack_only_a_pinned_run_in_order()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var pin = settings.ChaosPinOnTop;
            var oldRaise = ChaosRunHost.RaiseGameWindow;
            var oldSchedule = MandatoryVideoOverlay.ScheduleGameRestack;
            var raises = new List<Window>();
            var scheduled = new List<(Action Action, TimeSpan Delay)>();
            try
            {
                settings.ChaosPinOnTop = true;
                ChaosRunHost.RaiseGameWindow = raises.Add;
                MandatoryVideoOverlay.ScheduleGameRestack = (a, d) => scheduled.Add((a, d));
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Empty(raises); // even a visible field is not raised outside a run
                scheduled.Clear();
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 20, 500);
                var field = ChaosFieldFxOverlay.Active!;
                var hud = ChaosRunHost.Hud!;
                var boon = ChaosRunHost.BoonBar!;
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Contains(field, raises);
                Assert.True(raises.IndexOf(field) < raises.IndexOf(boon));
                Assert.True(raises.IndexOf(boon) < raises.IndexOf(hud));
                var first = raises.ToArray();
                Assert.Equal(TimeSpan.FromMilliseconds(60), scheduled[0].Delay);
                raises.Clear(); scheduled[0].Action();
                Assert.Equal(first, raises);
                scheduled.Clear(); raises.Clear();
                MandatoryVideoOverlay.RestackOnVideoClick();
                Assert.Empty(raises);
                Assert.Equal(TimeSpan.FromMilliseconds(50), scheduled[0].Delay);
                scheduled[0].Action();
                Assert.Equal(first, raises);
                raises.Clear(); settings.ChaosPinOnTop = false;
                scheduled[0].Action();
                MandatoryVideoOverlay.RestackOnVideoStart();
                Assert.Empty(raises);
                settings.ChaosPinOnTop = true;
                ChaosRunHost.ForceShutdown();
                scheduled[0].Action();
                Assert.Empty(raises);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                settings.ChaosPinOnTop = pin;
                ChaosRunHost.RaiseGameWindow = oldRaise;
                MandatoryVideoOverlay.ScheduleGameRestack = oldSchedule;
            }
            return Task.CompletedTask;
        });
    }
}
