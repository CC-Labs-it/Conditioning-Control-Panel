using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosWaveDraftTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        ChaosMeta.Init();
        ChaosMeta.State.RunsCompleted = 2; // skip is unlocked
        ChaosMeta.State.SeenSkipDebut = true;
    }

    private static void Ticks(int count) { for (int i = 0; i < count; i++) ChaosRunHost.Tick(); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Start(bool enabled = true)
    {
        ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = enabled,
            AllowCurses = false, DraftAutoResumeSec = 0 });
        ChaosRunHost.Overlay!.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
    }

    // WPF ChaosModeService.cs:1441-1483, 1577-1617 and ChaosOverlayWindow.xaml.cs:78-79.
    [Fact]
    public async Task Wave_draft_clears_the_field_holds_clock_and_resumes_after_skip_countdown()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            ChaosWaveTimerWindow.SkipPlatformChecksForTests = true;
            try
            {
                Start();
                var state = ChaosRunHost.State!;
                var overlay = ChaosRunHost.Overlay!;
                state.ShowWaveTimer = true;
                BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsLive = true, FuseMs = 320, SizePx = 200, Payload = new FlashPayload() });
                Ticks(80);
                Assert.True(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
                Assert.Equal(1, state.WaveIndex);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                Assert.False(ChaosWaveTimerWindow.Active!.Pill.IsVisible);
                double elapsed = state.ElapsedSec;
                Ticks(12);
                ChaosRunHost.SpawnTick();
                ChaosRunHost.ToggleManualPause();
                Assert.Equal(elapsed, state.ElapsedSec);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                Assert.False(ChaosRunHost.IsManuallyPaused);

                int shields = state.Shields;
                var skip = overlay.FindControl<Button>("BtnSkipBoon")!;
                Assert.True(skip.IsVisible);
                Click(skip);
                Assert.Equal(shields + 1, state.Shields);
                Assert.Equal(2, state.WaveIndex);
                Assert.False(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
                Ticks(4);
                Assert.Equal(elapsed, state.ElapsedSec);
                overlay.FinishCountdown();
                Ticks(4);
                Assert.Equal(elapsed + 1, state.ElapsedSec, 3);
                Assert.True(ChaosWaveTimerWindow.Active!.Pill.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosWaveTimerWindow.SkipPlatformChecksForTests = false; }
            return Task.CompletedTask;
        });
    }

    // WPF :1477-1483, :1507-1526, :1529-1547, :1586-1593.
    [Fact]
    public async Task Reroll_and_card_pick_use_the_run_state_and_resume_after_the_pick_beat()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            try
            {
                Start();
                var state = ChaosRunHost.State!;
                var overlay = ChaosRunHost.Overlay!;
                state.RerollsLeft = 1;
                Ticks(80);
                var host = overlay.FindControl<StackPanel>("BoonCardHost")!;
                Assert.Equal(3, host.Children.Count);
                Assert.True(ChaosMeta.State.DiscoveredCodexIds.Count(id => id.StartsWith("boon:")) >= 3);
                var reroll = overlay.FindControl<Button>("BtnReroll")!;
                Assert.True(reroll.IsVisible);
                Click(reroll);
                Assert.Equal(0, state.RerollsLeft);
                Assert.False(reroll.IsVisible);
                Assert.Equal(3, host.Children.Count);
                var card = (Border)host.Children[0];
                var pick = card.GetVisualDescendants().OfType<Button>().Single();
                Assert.True(pick.IsEnabled);
                Click(pick);
                Assert.Empty(state.ActiveBoons);
                await Task.Delay(1800); // WPF's 1.5 s confirmation beat
                Assert.Single(state.ActiveBoons);
                Assert.Single(state.RunPickTiles);
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper));
                Assert.Equal(2, state.WaveIndex);
                var elapsed = state.ElapsedSec;
                Ticks(4);
                Assert.Equal(elapsed, state.ElapsedSec);
                overlay.FinishCountdown();
                Ticks(4);
                Assert.Equal(elapsed + 1, state.ElapsedSec, 3);
            }
            finally { ChaosRunHost.ForceShutdown(); }
        });
    }

    // WPF :1477-1479, :1536-1566: Surrender guarantees a sin and shields its first drawback.
    [Fact]
    public async Task Surrender_shields_the_first_sin_and_grants_resistance()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            try
            {
                Start();
                var state = ChaosRunHost.State!;
                state.Config.AllowCurses = true;
                state.Config.SinChance = 0;
                state.MaxedBoons.Add("surrender");
                state.SinExtraMult = 0.4;
                int shields = state.Shields;
                double mult = state.BoonMult;
                Ticks(80);
                var overlay = ChaosRunHost.Overlay!;
                var host = overlay.FindControl<StackPanel>("BoonCardHost")!;
                await Task.Delay(1100); // the third card's 450 ms stagger
                var pick = ((Border)host.Children[2]).GetVisualDescendants().OfType<Button>().Single();
                Assert.True(pick.IsEnabled);
                Click(pick);
                await Task.Delay(1800);
                var sin = Assert.Single(state.ActiveCurses);
                Assert.True(state.SurrenderShieldUsed);
                Assert.Equal(shields + 1, state.Shields);
                Assert.Equal(mult + sin.RunMultBonus + 0.4, state.BoonMult, 6);
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes));
                Assert.Equal(2, state.WaveIndex);
            }
            finally { ChaosRunHost.ForceShutdown(); }
        });
    }

    // WPF :1447-1456, :3199-3243 and :3171-3189.
    [Fact]
    public async Task Drafts_off_rolls_on_and_stop_or_panic_cannot_resume_a_draft()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                Start(false);
                Ticks(84);
                Assert.Equal(2, ChaosRunHost.State!.WaveIndex);
                Assert.False(ChaosRunHost.Overlay!.FindControl<Border>("DraftPanel")!.IsVisible);
                ChaosRunHost.ForceShutdown();

                Start();
                var overlay = ChaosRunHost.Overlay!;
                Ticks(80);
                var skip = overlay.FindControl<Button>("BtnSkipBoon")!;
                ChaosRunHost.RequestStop();
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                Click(skip);
                overlay.FinishCountdown();
                Assert.False(ChaosRunHost.IsDescending);
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                Assert.True(overlay.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("Backdrop")!.IsVisible);
                ChaosRunHost.ForceShutdown();

                Start();
                overlay = ChaosRunHost.Overlay!;
                Ticks(80);
                Click(overlay.FindControl<Button>("BtnSkipBoon")!);
                ChaosRunHost.ForceShutdown();
                overlay.FinishCountdown();
                Assert.False(ChaosRunHost.IsActive);
                Assert.False(ChaosRunHost.IsDescending);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }
}
