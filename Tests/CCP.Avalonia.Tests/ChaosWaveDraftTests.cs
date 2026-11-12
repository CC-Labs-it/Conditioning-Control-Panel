using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
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
    // Replace the live state, and restore the exact slot bytes after any saves.
    private static void InFreshMeta(Action body)
    {
        var stateProperty = typeof(ChaosMeta).GetProperty(nameof(ChaosMeta.State))!;
        var original = ChaosMeta.State;
        var paths = Enumerable.Range(1, ChaosMetaStore.SlotCount).Select(ChaosMetaStore.SlotFilePath)
            .Append(Path.Combine(CorePaths.UserData, "chaos_meta.json")).ToArray();
        var bytes = paths.Select(p => File.Exists(p) ? File.ReadAllBytes(p) : null).ToArray();
        try
        {
            stateProperty.SetValue(null, new ChaosMetaState());
            body();
        }
        finally
        {
            try { ChaosRunHost.ForceShutdown(); }
            finally
            {
                stateProperty.SetValue(null, original);
                for (int i = 0; i < paths.Length; i++)
                    if (bytes[i] == null) File.Delete(paths[i]); else File.WriteAllBytes(paths[i], bytes[i]!);
            }
        }
    }

    private static void Choose(ChaosOverlayWindow overlay, ChaosBoon? boon) =>
        typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(overlay, new object?[] { boon });

    private static ChaosOverlayWindow BeginDraft()
    {
        ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true, DraftAutoResumeSec = 0 });
        var overlay = ChaosRunHost.Overlay!;
        overlay.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
        for (int i = 0; i < 80; i++) ChaosRunHost.Tick();
        return overlay;
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Boundary_holds_until_ready_and_resolves_skip_or_sin(bool sin)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            InFreshMeta(() =>
            {
            ChaosMeta.State.LessonsComplete.Add("silk_touch");
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
                Choose(overlay, boon);
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
            });
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Stopping_at_the_draft_cancels_its_pending_pick()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            InFreshMeta(() =>
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
            });
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 1)]
    public async Task Only_picked_cards_advance_their_lessons(int pick, long draft, long surrender)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            InFreshMeta(() =>
            {
                ChaosMeta.State.LessonsComplete.Add("silk_touch");
                var overlay = BeginDraft();
                var boon = pick == 0 ? null : ChaosBoonPool.All.First(b => b.IsCurse == (pick == 2));
                Choose(overlay, boon);
                Assert.Equal(draft, ChaosLessons.Progress("draft4"));
                Assert.Equal(surrender, ChaosLessons.Progress("surrender"));
            });
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Shower_waits_for_go_then_six_treats_fall_from_above()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            InFreshMeta(() =>
            {
                ChaosMeta.State.LessonsComplete.Add("silk_touch");
                var overlay = BeginDraft();
                Choose(overlay, ChaosBoonPool.All.Single(b => b.Id == "welcome_shower"));
                Assert.Empty(BubbleOverlay.Field.Bubbles);
                overlay.FinishCountdown();
                var bubbles = BubbleOverlay.Field.Bubbles.ToArray();
                Assert.Equal(6, bubbles.Length);
                Assert.All(bubbles, b =>
                {
                    Assert.Contains(b.Spec!.VariantId, new[] { "flash", "subliminal" });
                    Assert.Equal(ChaosMotion.RainDown, b.Spec.Motion);
                    Assert.Equal(ConditioningControlPanel.Models.BubbleMotionStyle.Rain, b.Motion);
                    double y = b.Y;
                    b.Step();
                    Assert.True(b.Y > y);
                });
            });
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Boundary_and_pick_lessons_wait_for_go_then_hold_until_last_card()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            InFreshMeta(() =>
            {
                var clock = new SteppedClock();
                var oldClock = ChaosUnlockCardOverlay.Clock;
                ChaosUnlockCardOverlay.Clock = clock;
                try
                {
                    ChaosMeta.State.LessonProgress["draft4"] = ChaosLessons.T_DRAFT4 - 1;
                    ChaosMeta.State.LessonProgress["surrender"] = ChaosLessons.T_SURRENDER - 1;
                    var overlay = BeginDraft();
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(ChaosUnlockCardOverlay.IsShowing);
                    var state = ChaosRunHost.State!;
                    state.WelcomeShowerEnabled = true;
                    Choose(overlay, ChaosBoonPool.All.First(b => b.IsCurse));
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(ChaosUnlockCardOverlay.IsShowing);
                    overlay.FinishCountdown();
                    Assert.True(ChaosUnlockCardOverlay.IsShowing);
                    Assert.Equal(2, ChaosUnlockCardOverlay.QueuedCount);
                    Assert.False(ChaosUnlockCardOverlay.Active!.AutoDismiss);
                    double elapsed = state.ElapsedSec;
                    ChaosRunHost.Tick();
                    ChaosRunHost.SpawnTick();
                    Assert.Equal(elapsed, state.ElapsedSec);
                    Assert.True(BubbleOverlay.Field.HoldChaos);
                    Assert.Empty(BubbleOverlay.Field.Bubbles);
                    var card = ChaosUnlockCardOverlay.Active!;
                    Assert.Equal(ChaosUnlockCardRules.ForLesson("silk_touch")!.Title, card.Current!.Title);
                    card.Dismiss();
                    Assert.Equal(ChaosUnlockCardRules.ForLesson("draft4")!.Title, card.Current!.Title);
                    card.Dismiss();
                    Assert.Equal(ChaosUnlockCardRules.ForLesson("surrender")!.Title, card.Current!.Title);
                    card.Dismiss();
                    clock.Ticks += TimeSpan.FromMilliseconds(ChaosUnlockCardOverlay.OutMs).Ticks;
                    card.Step();
                    Assert.False(ChaosUnlockCardOverlay.IsShowing);
                    Assert.False(BubbleOverlay.Field.HoldChaos);
                    Assert.Equal(6, BubbleOverlay.ChaosCount);
                    ChaosRunHost.Tick();
                    Assert.True(state.ElapsedSec > elapsed);
                }
                finally { ChaosUnlockCardOverlay.Clock = oldClock; }
            });
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Panic_discards_deferred_cards_before_the_next_run()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            InFreshMeta(() =>
            {
                BeginDraft(); // the first clean loop queues silk_touch
                Assert.False(ChaosUnlockCardOverlay.IsShowing);
                ChaosRunHost.ForceShutdown();
                var overlay = BeginDraft();
                Choose(overlay, null);
                overlay.FinishCountdown();
                Assert.False(ChaosUnlockCardOverlay.IsShowing);
                Assert.False(BubbleOverlay.Field.HoldChaos);
            });
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Fresh_meta_restores_live_state_and_all_slot_files()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var original = ChaosMeta.State;
            var paths = Enumerable.Range(1, ChaosMetaStore.SlotCount).Select(ChaosMetaStore.SlotFilePath).ToArray();
            var before = paths.Select(p => File.Exists(p) ? File.ReadAllBytes(p) : null).ToArray();
            InFreshMeta(() => { ChaosFirstTimes.TryAward(ChaosFirstTimes.Whisper); });
            Assert.Same(original, ChaosMeta.State);
            for (int i = 0; i < paths.Length; i++)
                Assert.Equal(before[i], File.Exists(paths[i]) ? File.ReadAllBytes(paths[i]) : null);
            return Task.CompletedTask;
        });
    }
}
