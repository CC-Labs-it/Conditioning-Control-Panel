using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
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
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Lesson_pause_rejects_late_field_callbacks(bool defuse) => await Run(_ =>
    {
        Begin();
        Complete("taking_chances");
        var state = ChaosRunHost.State!;
        var bubble = new ConditioningControlPanel.Services.AmbientBubble
        {
            Spec = new EffectBubbleSpec { Payload = new FlashPayload() },
        };
        var before = (state.Score, state.Focus, state.Detonated, state.Shields);
        if (defuse) ChaosRunHost.OnDefused(bubble);
        else ChaosRunHost.OnDetonated(bubble);
        Assert.Equal(before, (state.Score, state.Focus, state.Detonated, state.Shields));
    });

    [Fact]
    public async Task Card_art_uses_explicit_and_convention_paths_with_glyph_fallback() => await Run(_ =>
    {
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "boons"));
        try
        {
            ChaosArt.RootsOverride = () => new[] { root };
            var info = ChaosUnlockCardRules.ForLesson("taking_chances")! with { IconFolder = "boons", IconId = "test" };
            global::Avalonia.Controls.Image[] Images(ChaosUnlockCardInfo card) =>
                global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(ChaosUnlockCardLayer.Build(card, 420).Root)
                    .OfType<global::Avalonia.Controls.Image>().ToArray();
            Assert.Empty(Images(info with { IconId = "missing" }));
            string file = System.IO.Path.Combine(root, "boons", "test.png");
            using (var bitmap = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96)))
                bitmap.Save(file);
            Assert.Single(Images(info));
            Assert.Single(Images(info with { IconPath = file }));
            Assert.Same(ChaosArt.TryLoad(file), ChaosArt.TryLoad(file));
            Assert.Null(ChaosArt.FilePath("../test.png"));
        }
        finally { ChaosArt.ResetForTest(); System.IO.Directory.Delete(root, true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_art_roots_preserve_Wpf_library_path_and_precedence(bool customLibrary) => await Run(_ =>
    {
        // WPF App.xaml.cs:192 and Services/Chaos/ChaosArt.cs:64,142-147.
        // Use production roots and the suite's isolated UserData, not RootsOverride.
        string kind = "art-review-" + Guid.NewGuid().ToString("N");
        string library = System.IO.Path.Combine(ConditioningControlPanel.CorePaths.UserData, "assets", "assets", "Chaos", kind);
        string shipped = System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", kind);
        string effective = customLibrary
            ? System.IO.Path.Combine(ConditioningControlPanel.CorePaths.UserData, kind)
            : System.IO.Path.Combine(ConditioningControlPanel.CorePaths.UserData, "assets");
        string wrong = System.IO.Path.Combine(effective, "Chaos", kind);
        var provider = ConditioningControlPanel.CorePaths.EffectiveAssetsProvider;
        try
        {
            ChaosArt.ResetForTest();
            ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = () => effective;
            foreach (string folder in new[] { library, shipped, wrong })
            {
                System.IO.Directory.CreateDirectory(folder);
                using var bitmap = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96));
                bitmap.Save(System.IO.Path.Combine(folder, "probe.png"));
            }
            string expected = System.IO.Path.Combine(library, "probe.png");
            Assert.Same(ChaosArt.TryLoad(expected), ChaosArt.Resolve(kind, "probe"));
            Assert.Equal(expected, ChaosArt.PathFor(kind, "probe"));
            Assert.Equal(expected, ChaosArt.FilePath(System.IO.Path.Combine(kind, "probe.png")));
            // An unreadable override must fall through to shipped art, like WPF :65-66.
            System.IO.File.WriteAllText(expected, "invalid image");
            ChaosArt.ResetForTest();
            Assert.Same(ChaosArt.TryLoad(System.IO.Path.Combine(shipped, "probe.png")), ChaosArt.Resolve(kind, "probe"));
        }
        finally
        {
            ConditioningControlPanel.CorePaths.EffectiveAssetsProvider = provider;
            ChaosArt.ResetForTest();
            foreach (string folder in new[] { library, shipped, wrong })
                if (System.IO.Directory.Exists(folder)) System.IO.Directory.Delete(folder, true);
            if (customLibrary && System.IO.Directory.Exists(effective)) System.IO.Directory.Delete(effective, true);
        }
    });

    [Fact]
    public async Task Passive_card_style_keeps_clicks_without_activation_or_changing_topmost() => await Run(_ =>
    {
        var window = new ChaosUnlockCardOverlay();
        bool topmost = window.Topmost;
        ConditioningControlPanel.Avalonia.Platform.Win32Overlay.SetPassive(window, IntPtr.Zero);
        Assert.True(ConditioningControlPanel.Avalonia.Platform.Win32Overlay.IsPassive(window));
        Assert.False(window.ShowActivated);
        Assert.Equal(topmost, window.Topmost);
        var style = ConditioningControlPanel.Avalonia.Platform.Win32Overlay.Style(0, false, passive: true);
        Assert.Equal(0u, style & 0x20u); // clickable
        Assert.NotEqual(0u, style & 0x08000000u); // NOACTIVATE
        window.Close();
    });

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
