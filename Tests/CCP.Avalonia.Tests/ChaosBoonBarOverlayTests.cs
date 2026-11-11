using System.Linq;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The run-pick ribbon from a real run: born empty at BeginRun (WPF ChaosModeService :417),
/// one tile per drafted pick in order with only newcomers popping in, closed at run end and on panic.</summary>
public sealed class ChaosBoonBarOverlayTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static ChaosRunConfig Cfg() => new() { DurationSec = 60, WaveCount = 3 };

    [Fact]
    public async Task Pinned_run_reasserts_chrome_then_bubbles_without_activation()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var settings = global::ConditioningControlPanel.CoreSettings.Current;
            bool pin = settings.ChaosPinOnTop;
            var raised = new List<Window>();
            try
            {
                settings.ChaosPinOnTop = true;
                ChaosRunHost.RaiseChromeWindow = raised.Add;
                var bubble = new BubbleOverlayWindow();
                ChaosRunHost.RaiseBubbles = () => raised.Add(bubble);
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                for (int i = 0; i < 4; i++) ChaosRunHost.Tick();
                var bar = ChaosRunHost.BoonBar!;
                Assert.Contains(bar, raised);
                Assert.False(bar.ShowActivated);
                Assert.True(raised.FindIndex(w => w is BubbleOverlayWindow) > raised.IndexOf(bar));
                raised.Clear();
                settings.ChaosPinOnTop = false;
                for (int i = 0; i < 4; i++) ChaosRunHost.Tick();
                Assert.Empty(raised);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosRunHost.RaiseChromeWindow = w => ConditioningControlPanel.Avalonia.Platform.X11Overlay.RaiseManaged(w);
                ChaosRunHost.RaiseBubbles = () => BubbleOverlay.RaiseAll(ChaosRunHost.RaiseOverlayWindow);
                settings.ChaosPinOnTop = pin;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Picks_land_live_in_order_and_panic_closes_the_ribbon()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                Assert.Null(ChaosRunHost.BoonBar);   // countdown: not yet
                ChaosRunHost.Overlay!.FinishCountdown();
                var bar = ChaosRunHost.BoonBar!;
                Assert.True(bar.IsVisible);
                Assert.False(bar.Pill.IsVisible);    // no picks: pill collapsed

                var state = ChaosRunHost.State!;
                var boon = ChaosBoonPool.All.First(b => !b.IsCurse);
                var curse = ChaosBoonPool.All.First(b => b.IsCurse);
                state.ApplyBoon(boon);
                Assert.True(bar.Pill.IsVisible);
                Assert.Equal(0, Assert.Single(bar.Row.Children).Opacity);   // newcomer pops in from 0
                bar.StepPops(1000);
                Assert.Equal(1, bar.Row.Children[0].Opacity);

                state.ApplyBoon(curse);
                Assert.Equal(2, bar.Row.Children.Count);
                Assert.Equal(1, bar.Row.Children[0].Opacity);   // already shown: not re-animated
                Assert.Equal(0, bar.Row.Children[1].Opacity);
                Assert.Equal(new[] { boon.Name, curse.Name },
                    bar.Row.Children.Select(c => ((TextBlock)((StackPanel)ToolTip.GetTip(c)!).Children[0]).Text));

                ChaosRunHost.ForceShutdown();   // the 'chaos' panic surface
                Assert.Null(ChaosRunHost.BoonBar);
                Assert.False(bar.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Run_end_closes_the_ribbon()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            bool pin = global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop;
            try
            {
                global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop = false;   // WPF BornTopmost follows the pin
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var bar = ChaosRunHost.BoonBar!;
                Assert.False(bar.Topmost);
                ChaosRunHost.RequestStop();   // WPF EndRun :3235
                Assert.Null(ChaosRunHost.BoonBar);
                Assert.False(bar.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop = pin; }
            return Task.CompletedTask;
        });
    }
}
