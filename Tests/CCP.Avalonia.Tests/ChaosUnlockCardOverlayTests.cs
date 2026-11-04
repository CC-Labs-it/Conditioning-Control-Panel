using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosUnlockCardOverlayTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task A_completed_lesson_holds_the_clock_spawns_and_bubble_input()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosMeta.State.LessonsComplete.Remove("taking_chances");
                ChaosMeta.State.LessonProgress.Remove("taking_chances");
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosRunHost.SpawnTick();
                var bubble = BubbleOverlay.Field.Bubbles.First(b => b.Spec != null && !b.Popping);
                var state = ChaosRunHost.State!;
                ChaosLessons.Tick("taking_chances", ChaosLessons.T_TAKING_CHANCES);
                Dispatcher.UIThread.RunJobs();
                double elapsed = state.ElapsedSec, y = bubble.Y, score = state.Score;
                int count = BubbleOverlay.ChaosCount;
                ChaosRunHost.Tick();
                ChaosRunHost.SpawnTick();
                BubbleOverlay.Field.Step();
                BubbleOverlay.Pop(bubble);
                Assert.Equal(elapsed, state.ElapsedSec);
                Assert.Equal(count, BubbleOverlay.ChaosCount);
                Assert.Equal((y, false, score), (bubble.Y, bubble.Popping, state.Score));
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }
    private sealed class SteppedClock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public void Advance(int ms) => Ticks += TimeSpan.FromMilliseconds(ms).Ticks;
    }

    private static async Task Run(Action<SteppedClock> body)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var clock = new SteppedClock();
            ChaosUnlockCardOverlay.Clock = clock;
            try { body(clock); }
            finally { ChaosRunHost.ForceShutdown(); ChaosUnlockCardOverlay.Clock = TimeProvider.System; }
            return Task.CompletedTask;
        });
    }

    private static void Step(SteppedClock clock, int ms)
    {
        clock.Advance(ms);
        ChaosUnlockCardOverlay.Active?.Step();
    }

    private static void Click()
    {
        var window = ChaosUnlockCardOverlay.Active!;
        window.UpdateLayout();
        window.MouseDown(new Point(10, 10), global::Avalonia.Input.MouseButton.Left);
        window.MouseUp(new Point(10, 10), global::Avalonia.Input.MouseButton.Left);
    }

    private static void Complete(string id)
    {
        ChaosMeta.State.LessonsComplete.Remove(id);
        ChaosMeta.State.LessonProgress.Remove(id);
        ChaosLessons.Tick(id, ChaosLessons.ById(id)!.Target);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Begin()
    {
        ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
        ChaosRunHost.Overlay!.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
    }

    [Fact]
    public async Task Clicks_drain_FIFO_cards_and_callbacks_observe_the_queue_until_last_fade()
    {
        await Run(clock =>
        {
            var callbacks = new System.Collections.Generic.List<(string, bool)>();
            var first = ChaosUnlockCardRules.ForLesson("taking_chances")!;
            var second = ChaosUnlockCardRules.ForLesson("intrusive_thoughts")!;
            ChaosUnlockCardOverlay.Show(first, () => callbacks.Add(("first", ChaosUnlockCardOverlay.IsShowing)));
            ChaosUnlockCardOverlay.Show(second, () => callbacks.Add(("second", ChaosUnlockCardOverlay.IsShowing)));
            var window = ChaosUnlockCardOverlay.Active!;
            Assert.False(window.ShowActivated);
            Assert.True(window.IsHitTestVisible);
            Assert.Same(first, window.Current);
            Assert.Equal(1, ChaosUnlockCardOverlay.QueuedCount);
            Step(clock, 240);
            Click();
            Assert.Same(window, ChaosUnlockCardOverlay.Active);
            Assert.Same(second, window.Current);
            Assert.Equal(1, window.ContentOpacity);   // queued cards swap without scrim fade churn
            Assert.Equal(new[] { ("first", true) }, callbacks);
            Step(clock, 240);
            Click();
            Step(clock, 149);
            Assert.True(ChaosUnlockCardOverlay.IsShowing);
            Step(clock, 1);
            Assert.Equal(new[] { ("first", true), ("second", false) }, callbacks);
            Assert.False(window.IsVisible);
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
        });
    }

    [Fact]
    public async Task Pause_free_cards_expire_at_twelve_seconds_but_pause_holders_wait_for_a_click()
    {
        await Run(clock =>
        {
            ChaosUnlockCardOverlay.Show(ChaosUnlockCardRules.ForLesson("taking_chances")!);
            Step(clock, 11999);
            Assert.True(ChaosUnlockCardOverlay.IsShowing);
            Step(clock, 1);
            Step(clock, 150);
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
            ChaosUnlockCardOverlay.Show(ChaosUnlockCardRules.ForLesson("taking_chances")!, autoDismiss: false);
            Step(clock, 60000);
            Assert.True(ChaosUnlockCardOverlay.IsShowing);
            Click();
            Step(clock, 150);
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
        });
    }

    [Fact]
    public async Task Lesson_pause_survives_queued_cards_and_cannot_be_lifted_by_manual_pause()
    {
        await Run(clock =>
        {
            Begin();
            Complete("taking_chances");
            Complete("intrusive_thoughts");
            var state = ChaosRunHost.State!;
            double elapsed = state.ElapsedSec;
            Assert.False(ChaosUnlockCardOverlay.Active!.AutoDismiss);
            ChaosRunHost.ToggleManualPause();
            Assert.False(ChaosRunHost.IsManuallyPaused);
            Step(clock, 240);
            Click();
            Assert.False(ChaosUnlockCardOverlay.Active!.AutoDismiss);
            Step(clock, 20000);
            ChaosRunHost.Tick();
            Assert.Equal(elapsed, state.ElapsedSec);
            Click();
            Step(clock, 150);
            ChaosRunHost.StopTimerForTests();
            ChaosRunHost.Tick();
            Assert.Equal(elapsed + ChaosRunEngine.TICK_SEC, state.ElapsedSec);
        });
    }

    private static void AssertHeld(ChaosRunState state, double elapsed) { ChaosRunHost.Tick(); Assert.Equal(elapsed, state.ElapsedSec); }
    private static void DismissLast(SteppedClock clock) { Step(clock, 240); Click(); Step(clock, 150); }
    private static ChaosOverlayWindow BeginDraft() { Begin(); for (int i = 0; i < 80; i++) ChaosRunHost.Tick(); return ChaosRunHost.Overlay!; }
    // WPF ChaosModeService.cs:140-180, 1534-1547, 1597-1613: both lessons earned
    // by one sin wait for SINK and hold the field until the last card is dismissed.
    [Fact]
    public Task Draft_pick_defers_both_lessons_until_countdown_then_holds_until_last_dismissal() => ChaosWaveDraftTests.Run(async () =>
    {
        var clock = new SteppedClock(); ChaosUnlockCardOverlay.Clock = clock; var loc = LocalizationManager.Instance;
        string oldLanguage = loc.CurrentLanguage; var oldCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            loc.SetLanguage("fr"); System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            ChaosMeta.State.LessonsComplete.Add("silk_touch");
            foreach (var id in new[] { "draft4", "surrender" })
            {
                ChaosMeta.State.LessonsComplete.Remove(id); ChaosMeta.State.LessonProgress[id] = ChaosLessons.ById(id)!.Target - 1;
            }
            Begin(); var state = ChaosRunHost.State!; var overlay = ChaosRunHost.Overlay!;
            await ChaosWaveDraftTests.PickSurrender(state, overlay);
            Assert.True(ChaosLessons.IsComplete("draft4")); Assert.True(ChaosLessons.IsComplete("surrender"));
            Assert.Contains("🕯 péché accepté (+0.40x)", state.RecentEvents);
            Assert.Contains("🕯 la bougie a ôté la morsure (sans contrepartie)", state.RecentEvents);
            Assert.Contains("🕯 tu as dit oui. elle a rendu la pareille (+1 résistance)", state.RecentEvents); Assert.False(ChaosUnlockCardOverlay.IsShowing);
            double elapsed = state.ElapsedSec; AssertHeld(state, elapsed); overlay.FinishCountdown();
            Assert.Equal(ChaosUnlockCardRules.ForLesson("draft4")!.Title, ChaosUnlockCardOverlay.Active!.Current!.Title);
            Assert.Equal(1, ChaosUnlockCardOverlay.QueuedCount); Assert.False(ChaosUnlockCardOverlay.Active.AutoDismiss);
            Assert.True(BubbleOverlay.Field.HoldChaos); ChaosRunHost.SpawnTick();
            Assert.Equal(0, BubbleOverlay.ChaosCount); Step(clock, 20000); AssertHeld(state, elapsed); Click();
            Assert.Equal(ChaosUnlockCardRules.ForLesson("surrender")!.Title, ChaosUnlockCardOverlay.Active!.Current!.Title);
            Assert.False(ChaosUnlockCardOverlay.Active.AutoDismiss); AssertHeld(state, elapsed); DismissLast(clock);
            Assert.False(ChaosUnlockCardOverlay.IsShowing); Assert.False(BubbleOverlay.Field.HoldChaos); ChaosRunHost.Tick();
            Assert.Equal(elapsed + ChaosRunEngine.TICK_SEC, state.ElapsedSec);
        }
        finally
        {
            ChaosRunHost.ForceShutdown(); ChaosUnlockCardOverlay.Clock = TimeProvider.System; loc.SetLanguage(oldLanguage);
            System.Globalization.CultureInfo.CurrentCulture = oldCulture;
        }
    });
    // WPF :140-144 and :1443-1459: a clean loop's lesson is deferred too, even on a fast skip.
    [Fact]
    public Task Loop_boundary_lesson_waits_for_the_draft_countdown() => Run(clock =>
    {
        ChaosMeta.State.LessonsComplete.Remove("silk_touch"); ChaosMeta.State.LessonProgress.Remove("silk_touch"); ChaosMeta.State.RunsCompleted = 2;
        ChaosMeta.State.SeenSkipDebut = true; var overlay = BeginDraft();
        Assert.True(ChaosLessons.IsComplete("silk_touch")); Assert.False(ChaosUnlockCardOverlay.IsShowing);
        overlay.FindControl<Button>("BtnSkipBoon")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); overlay.FinishCountdown();
        Assert.Equal(ChaosUnlockCardRules.ForLesson("silk_touch")!.Title, ChaosUnlockCardOverlay.Active!.Current!.Title);
        Assert.False(ChaosUnlockCardOverlay.Active.AutoDismiss); double elapsed = ChaosRunHost.State!.ElapsedSec;
        ChaosRunHost.Tick(); Assert.Equal(elapsed, ChaosRunHost.State.ElapsedSec); DismissLast(clock);
        ChaosRunHost.Tick(); Assert.Equal(elapsed + ChaosRunEngine.TICK_SEC, ChaosRunHost.State.ElapsedSec);
    });
    // WPF ForceShutdown / EndRun: queued cards from a stopped run cannot surface in the next one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Stopping_a_draft_discards_its_pending_lesson(bool panic) => Run(_ =>
    {
        ChaosMeta.State.LessonsComplete.Add("silk_touch"); ChaosMeta.State.RunsCompleted = 2; ChaosMeta.State.SeenSkipDebut = true;
        ChaosMeta.State.LessonsComplete.Remove("draft4"); ChaosMeta.State.LessonProgress["draft4"] = ChaosLessons.T_DRAFT4 - 1;
        BeginDraft();
        ChaosLessons.Tick("draft4");
        Assert.False(ChaosUnlockCardOverlay.IsShowing);
        if (panic) ChaosRunHost.ForceShutdown();
        else { ChaosRunHost.RequestStop(); ChaosRunHost.ForceShutdown(); }
        var overlay = BeginDraft();
        overlay.FindControl<Button>("BtnSkipBoon")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        overlay.FinishCountdown();
        Dispatcher.UIThread.RunJobs();
        Assert.False(ChaosUnlockCardOverlay.IsShowing);
    });

    [Fact]
    public async Task A_manually_paused_run_stays_paused_after_its_card_expires()
    {
        await Run(clock =>
        {
            Begin();
            ChaosRunHost.ToggleManualPause();
            Complete("taking_chances");
            Assert.True(ChaosUnlockCardOverlay.Active!.AutoDismiss);
            Step(clock, 12000);
            Step(clock, 150);
            Assert.True(ChaosRunHost.IsManuallyPaused);
            double elapsed = ChaosRunHost.State!.ElapsedSec;
            ChaosRunHost.Tick();
            Assert.Equal(elapsed, ChaosRunHost.State.ElapsedSec);
        });
    }

    [Theory]
    [InlineData("taking_chances")]
    [InlineData("intrusive_thoughts")]
    public async Task Gameplay_completion_opens_a_card_and_shutdown_discards_it_without_resuming(string id)
    {
        await Run(clock =>
        {
            Begin();
            ChaosMeta.State.LessonsComplete.Remove(id);
            ChaosMeta.State.LessonProgress[id] = ChaosLessons.ById(id)!.Target - 1;
            var prism = id == "taking_chances" ? ChaosBubbleVariants.BuildPrism(1, 1, treatOnly: true)
                : ChaosBubbleVariants.Build(ChaosBubbleVariants.All.Single(v => v.Id == "subliminal"), 1);
            ChaosRunHost.OnBubblePopped(new ConditioningControlPanel.Services.AmbientBubble { Spec = prism });
            Dispatcher.UIThread.RunJobs();
            Assert.True(ChaosLessons.IsComplete(id));
            Assert.Equal(ChaosUnlockCardRules.ForLesson(id)!.Title, ChaosUnlockCardOverlay.Active!.Current!.Title);
            Complete(id == "taking_chances" ? "intrusive_thoughts" : "taking_chances");
            Assert.Equal(1, ChaosUnlockCardOverlay.QueuedCount);
            var window = ChaosUnlockCardOverlay.Active;
            var state = ChaosRunHost.State!;
            double elapsed = state.ElapsedSec;
            ChaosRunHost.ForceShutdown();
            Step(clock, 60000);
            window!.Step();
            ChaosRunHost.Tick();
            Assert.Null(ChaosUnlockCardOverlay.Active);
            Assert.Equal(0, ChaosUnlockCardOverlay.QueuedCount);
            Assert.False(ChaosUnlockCardOverlay.IsShowing || window.IsVisible || ChaosRunHost.IsActive);
            Assert.Equal(elapsed, state.ElapsedSec);
        });
    }

    [Fact]
    public async Task Teardown_discards_callbacks_and_a_posted_completion_cannot_reopen_after_panic()
    {
        await Run(_ =>
        {
            int dismissed = 0;
            ChaosUnlockCardOverlay.Show(ChaosUnlockCardRules.ForLesson("taking_chances")!, () => dismissed++);
            ChaosUnlockCardOverlay.Show(ChaosUnlockCardRules.ForLesson("intrusive_thoughts")!, () => dismissed++);
            ChaosRunHost.ForceShutdown();   // also closes an idle caller's overlay
            Assert.Equal(0, dismissed);
            Begin();
            ChaosMeta.State.LessonsComplete.Remove("taking_chances");
            ChaosLessons.Tick("taking_chances", ChaosLessons.T_TAKING_CHANCES);
            ChaosRunHost.ForceShutdown();   // completion is still posted to the UI dispatcher
            Dispatcher.UIThread.RunJobs();
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
            Assert.Null(ChaosUnlockCardOverlay.Active);
        });
    }

    [Fact]
    public async Task A_window_manager_close_releases_the_lesson_pause()
    {
        await Run(_ =>
        {
            Begin();
            Complete("taking_chances");
            Complete("intrusive_thoughts");
            var state = ChaosRunHost.State!;
            double elapsed = state.ElapsedSec;
            ChaosUnlockCardOverlay.Active!.Close();   // Alt+F4, not CloseActive
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
            ChaosRunHost.Tick();
            Assert.Equal(elapsed + ChaosRunEngine.TICK_SEC, state.ElapsedSec);
        });
    }

    [Fact]
    public async Task A_throwing_callback_does_not_wedge_the_next_card()
    {
        await Run(clock =>
        {
            ChaosUnlockCardOverlay.Show(ChaosUnlockCardRules.ForLesson("taking_chances")!, () => throw new InvalidOperationException("test"));
            var next = ChaosUnlockCardRules.ForLesson("intrusive_thoughts")!;
            ChaosUnlockCardOverlay.Show(next);
            Step(clock, 240);
            Click();
            Assert.Same(next, ChaosUnlockCardOverlay.Active!.Current);
            Click();
            Step(clock, 150);
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
        });
    }
}
