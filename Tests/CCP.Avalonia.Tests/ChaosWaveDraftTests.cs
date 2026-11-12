using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosWaveDraftTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Boundary_holds_until_ready_and_resolves_skip_or_sin(bool sin)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            for (int slot = 1; slot <= 3; slot++) ChaosMetaStore.Delete(slot);
            ChaosMeta.Init();
            bool oldWatchChecks = ChaosWaveTimerWindow.SkipPlatformChecksForTests;
            ChaosWaveTimerWindow.SkipPlatformChecksForTests = true;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true, DraftAutoResumeSec = 0 });
                var overlay = ChaosRunHost.Overlay!;
                overlay.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                state.RerollsLeft = 1;
                state.ShowWaveTimer = true;
                BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsLive = true, FuseMs = 5000, SizePx = 100 });
                for (int i = 0; i < 80; i++) ChaosRunHost.Tick();
                Assert.True(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
                Assert.Equal(1, state.WaveIndex);
                Assert.False(ChaosWaveTimerWindow.Active!.Pill.IsVisible);
                var reroll = overlay.FindControl<Button>("BtnReroll")!;
                Assert.True(reroll.IsVisible);
                reroll.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, state.RerollsLeft);
                Assert.False(reroll.IsVisible);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                Assert.True(BubbleOverlay.Field.HoldChaos);
                ChaosRunHost.ToggleManualPause();
                Assert.False(ChaosRunHost.IsManuallyPaused);
                double elapsed = state.ElapsedSec;
                ChaosRunHost.Tick();
                Assert.Equal(elapsed, state.ElapsedSec);
                int shields = state.Shields;
                var boon = sin ? ChaosBoonPool.All.First(b => b.IsCurse) : null;
                typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, new object?[] { boon });
                Assert.Equal(2, state.WaveIndex);
                if (sin)
                {
                    Assert.Contains(boon!, state.ActiveCurses);
                    Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper));
                    Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes));
                }
                else Assert.Equal(shields + 1, state.Shields);
                ChaosRunHost.Tick();
                Assert.Equal(elapsed, state.ElapsedSec);
                overlay.FinishCountdown();
                Assert.False(BubbleOverlay.Field.HoldChaos);
                ChaosRunHost.Tick();
                Assert.True(state.ElapsedSec > elapsed);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosWaveTimerWindow.SkipPlatformChecksForTests = oldWatchChecks;
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Stopping_at_the_draft_cancels_its_pending_pick()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true, DraftAutoResumeSec = 0 });
                var overlay = ChaosRunHost.Overlay!;
                overlay.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                for (int i = 0; i < 80; i++) ChaosRunHost.Tick();
                Assert.True(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
                ChaosRunHost.RequestStop();
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, new object?[] { null });
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                Assert.False(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
                Assert.False(BubbleOverlay.Field.HoldChaos);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }
}
