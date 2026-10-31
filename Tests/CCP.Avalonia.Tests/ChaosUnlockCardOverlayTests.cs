using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
using ConditioningControlPanel;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wave_draft_and_ready_go_defer_cards_until_the_last_dismissal(bool flashes) => await Run(clock =>
    {
        var pulses = new System.Collections.Generic.List<(uint, double)>();
        var cues = new System.Collections.Generic.List<string>();
        ChaosRunHost.PulseFx = (c, strength) => pulses.Add((c.ToUInt32(), strength));
        ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (p, _, _, _, _) => cues.Add(System.IO.Path.GetFileNameWithoutExtension(p));
        ConditioningControlPanel.CoreSettings.Current.ChaosAnnouncerEnabled = true;
        ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
            BoonDraftEnabled = true, ColorFlashesEnabled = flashes });
        var overlay = ChaosRunHost.Overlay!;
        overlay.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
        var state = ChaosRunHost.State!;
        state.WelcomeShowerEnabled = true;
        ChaosMeta.State.LessonsComplete.Remove("silk_touch");
        ChaosMeta.State.LessonProgress["silk_touch"] = ChaosLessons.ById("silk_touch")!.Target - 1;
        state.ElapsedSec = 40; // entering the final loop
        ChaosRunHost.Tick();
        Assert.True(overlay.FindControl<global::Avalonia.Controls.Border>("DraftPanel")!.IsVisible);
        Assert.Equal(flashes, pulses.Contains((0xFF96C8FFu, 0.30)));
        Assert.Contains("lvup", cues); // shipped fallback when the dedicated wave-clear asset is absent
        Dispatcher.UIThread.RunJobs();
        Assert.True(ChaosLessons.IsComplete("silk_touch"));
        Assert.False(ChaosUnlockCardOverlay.IsShowing);
        double elapsed = state.ElapsedSec;
        ChaosRunHost.Tick();
        ChaosRunHost.SpawnTick();
        Assert.Equal(elapsed, state.ElapsedSec);
        Assert.Equal(0, BubbleOverlay.ChaosCount);
        int shields = state.Shields;
        // Drive the overlay's actual selection callback, including HideDraft.
        typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.Invoke(overlay, new object?[] { null });
        Assert.Equal(shields + 1, state.Shields);
        Assert.Equal(flashes, pulses.Contains((0xFF78DCA0u, 0.22)));
        Assert.Equal(("resistance", ChaosAnnounceKind.Willpower, ConditioningControlPanel.Localization.Loc.Get("Chaos_DraftResistance")),
            (ChaosAnnouncerOverlay.Active!.Current!.ArtKey, ChaosAnnouncerOverlay.Active.Current.Kind, ChaosAnnouncerOverlay.Active.Current.Text));
        ChaosAnnouncerOverlay.CloseActive(); // final-loop delivery below is a distinct announcement
        Complete("intrusive_thoughts"); // a second completion during ReadyGo
        Assert.False(ChaosUnlockCardOverlay.IsShowing);
        overlay.FinishCountdown();
        Assert.Equal(1, ChaosUnlockCardOverlay.QueuedCount);
        Assert.False(ChaosUnlockCardOverlay.Active!.AutoDismiss);
        Step(clock, 240); Click();
        ChaosRunHost.Tick();
        Assert.Equal(elapsed, state.ElapsedSec);
        Step(clock, 240); Click(); Step(clock, 150);
        ChaosRunHost.StopTimerForTests();
        ChaosRunHost.Tick();
        Assert.Equal(elapsed + ChaosRunEngine.TICK_SEC, state.ElapsedSec);
        Assert.Equal(3, state.WaveIndex);
        Assert.True(state.WelcomeShowerEnabled);
        Assert.False(ChaosUnlockCardOverlay.IsShowing);
        Assert.Equal(6, BubbleOverlay.ChaosCount);
        Assert.All(BubbleOverlay.Field.Bubbles, b => Assert.Equal(ChaosMotion.RainDown, b.Spec!.Motion));
        double y = BubbleOverlay.Field.Bubbles[0].Y;
        BubbleOverlay.Field.Step();
        Assert.True(BubbleOverlay.Field.Bubbles[0].Y > y);
        Assert.Equal("final_loop", ChaosAnnouncerOverlay.Active!.Current!.ArtKey);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Draft_pick_earns_a_deferred_lesson_and_shutdown_drops_the_handoff(int pick) => await Run(clock =>
    {
        bool curse = pick == 1;
        var pulses = new System.Collections.Generic.List<(uint, double)>();
        var cues = new System.Collections.Generic.List<string>();
        ChaosRunHost.PulseFx = (c, strength) => pulses.Add((c.ToUInt32(), strength));
        ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (p, _, _, _, _) => cues.Add(System.IO.Path.GetFileNameWithoutExtension(p));
        ConditioningControlPanel.CoreSettings.Current.ChaosAnnouncerEnabled = true;
        Begin();
        var state = ChaosRunHost.State!;
        state.ElapsedSec = 20;
        state.RerollsLeft = 1;
        ChaosMeta.State.LessonsComplete.Add("silk_touch");
        ChaosMeta.State.LessonsComplete.Remove("draft4");
        ChaosMeta.State.LessonProgress["draft4"] = ChaosLessons.ById("draft4")!.Target - 1;
        ChaosRunHost.Tick();
        var overlay = ChaosRunHost.Overlay!;
        var reroll = overlay.FindControl<Button>("BtnReroll")!;
        Assert.True(reroll.IsVisible);
        reroll.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, state.RerollsLeft);
        Assert.False(reroll.IsVisible);
        var boon = curse ? ChaosBoonPool.Draft(true, 3, guaranteeCurse: true).First(b => b.IsCurse)
            : ChaosBoonPool.All.Single(b => b.Id == (pick == 2 ? "extra_shield" : "golden_touch"));
        int shields = state.Shields;
        double mult = state.BoonMult;
        state.SinExtraMult = 0.2;
        if (curse) state.MaxedBoons.Add("surrender");
        ChaosMeta.State.FirstTimesAwarded.Remove(ChaosFirstTimes.Whisper);
        ChaosMeta.State.FirstTimesAwarded.Remove(ChaosFirstTimes.Yes);
        typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.Invoke(overlay, new object?[] { boon });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(boon, curse ? state.ActiveCurses : state.ActiveBoons);
        Assert.Equal(mult + boon.RunMultBonus + (curse ? 0.2 : 0), state.BoonMult, 8);
        Assert.Equal(shields + (curse ? 1 : pick == 2 ? 2 : 0), state.Shields);
        Assert.Equal(pick != 0, pulses.Contains((0xFF78DCA0u, 0.22)));
        Assert.Equal(curse, cues.Contains("sin_accept"));
        Assert.Equal((boon.Id, curse ? ChaosAnnounceKind.Temptation : ChaosAnnounceKind.Mantra, (curse ? "☠ " : "◈ ") + boon.Name),
            (ChaosAnnouncerOverlay.Active!.Current!.ArtKey, ChaosAnnouncerOverlay.Active.Current.Kind, ChaosAnnouncerOverlay.Active.Current.Text));
        Assert.Equal(curse, state.SurrenderShieldUsed);
        Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper));
        Assert.Equal(curse, ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes));
        Assert.True(ChaosLessons.IsComplete("draft4"));
        Assert.False(ChaosUnlockCardOverlay.IsShowing);
        ChaosRunHost.ForceShutdown();
        overlay.FinishCountdown(); // an old GO cannot revive cards or spawns
        Dispatcher.UIThread.RunJobs();
        Assert.False(ChaosUnlockCardOverlay.IsShowing);
        Assert.False(BubbleOverlay.Field.HoldChaos);
        Assert.Equal(0, BubbleOverlay.ChaosCount);
        Begin();
        Assert.False(ChaosUnlockCardOverlay.IsShowing);
    });

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Draft_pulses_render_in_the_real_fx_window_without_a_hook(bool flashes, int pick) => await Run(clock =>
    {
        // WPF ChaosModeService.cs:1467,1548,1562,1581,1672; ChaosFxWindow.cs:151.
        Assert.Null(ChaosRunHost.PulseFx);
        var accept = ChaosFxWindow.Accept;
        var time = ChaosFxWindow.Clock;
        ChaosFxWindow.Accept = _ => true; // headless has no native click-through surface
        ChaosFxWindow.Clock = clock;
        try
        {
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                BoonDraftEnabled = true, ColorFlashesEnabled = flashes });
            var overlay = ChaosRunHost.Overlay!;
            overlay.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            var fx = ChaosRunHost.Fx!;
            Assert.True(fx.IsVisible);
            var state = ChaosRunHost.State!;
            ChaosMeta.State.LessonsComplete.Add("silk_touch");
            state.ElapsedSec = 20;
            ChaosRunHost.Tick();
            clock.Advance(40); fx.Frame();
            Assert.Equal(flashes ? 0.37 : 0, fx.VignetteLayer.Opacity, 6);
            if (flashes) Assert.Equal(global::Avalonia.Media.Color.FromRgb(150, 200, 255),
                ((global::Avalonia.Media.IRadialGradientBrush)fx.VignetteLayer.Background!).GradientStops[2].Color);
            clock.Advance(300); fx.Frame();
            Assert.Equal(0, fx.VignetteLayer.Opacity);
            ChaosBoon? boon = pick == 0 ? null : pick == 1
                ? ChaosBoonPool.All.Single(b => b.Id == "extra_shield") : ChaosBoonPool.All.First(b => b.IsCurse);
            if (pick == 2) state.MaxedBoons.Add("surrender");
            typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)!.Invoke(overlay, new object?[] { boon });
            clock.Advance(40); fx.Frame();
            Assert.Equal(flashes ? 0.33 : 0, fx.VignetteLayer.Opacity, 6);
            if (flashes) Assert.Equal(global::Avalonia.Media.Color.FromRgb(120, 220, 160),
                ((global::Avalonia.Media.IRadialGradientBrush)fx.VignetteLayer.Background!).GradientStops[2].Color);
            clock.Advance(300); fx.Frame();
            Assert.Equal(0, fx.VignetteLayer.Opacity);
            Assert.Null(ChaosRunHost.PulseFx);
        }
        finally { ChaosRunHost.ForceShutdown(); ChaosFxWindow.Clock = time; ChaosFxWindow.Accept = accept; }
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Freeze_pickup_keeps_its_remaining_window_across_a_lesson_or_manual_pause(bool manual) => await Run(clock =>
    {
        ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
            EnabledVariants = new System.Collections.Generic.List<string> { "bambifreeze" } });
        ChaosRunHost.Overlay!.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
        ChaosMeta.State.LessonsComplete.Remove("freeze_trigger");
        ChaosMeta.State.LessonProgress["freeze_trigger"] = ChaosLessons.ById("freeze_trigger")!.Target - 1;
        ChaosRunHost.SpawnTick();
        var bubble = Assert.Single(BubbleOverlay.Field.Bubbles, b => b.Spec?.IsFreeze == true);
        BubbleOverlay.Pop(bubble);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ChaosLessons.IsComplete("freeze_trigger"));
        Assert.Equal(3.5, ChaosRunHost.FreezeRemainingSec);
        Assert.True(BubbleOverlay.Field.FreezeChaos);
        for (int i = 0; i < 30; i++) ChaosRunHost.Tick();
        Assert.Equal(3.5, ChaosRunHost.FreezeRemainingSec);
        Step(clock, 240); Click(); Step(clock, 150);
        ChaosRunHost.StopTimerForTests();
        ChaosRunHost.Tick();
        Assert.Equal(3.25, ChaosRunHost.FreezeRemainingSec);
        // WPF ChaosModeService.cs:1930,1991: frozen defuses need and spend no focus.
        var state = ChaosRunHost.State!;
        state.Focus = 0;
        var live = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
            { IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() });
        var savedClock = BubbleOverlay.Field.Clock;
        var now = DateTime.UnixEpoch;
        try
        {
            BubbleOverlay.Field.Clock = () => now = now.AddMilliseconds(32);
            BubbleOverlay.Press(live, live.CenterX, live.CenterY);
            Assert.True(live.Channeling);
            state.Focus = 50; // completion must preserve focus as well as admitting a zero-focus press
            for (int i = 0; i < 32; i++) BubbleOverlay.Field.Step();
            Assert.Equal(1, state.Defused);
            Assert.Equal(50, state.Focus);
        }
        finally { BubbleOverlay.Field.Clock = savedClock; }
        if (manual) ChaosRunHost.ToggleManualPause();
        else Complete("taking_chances");
        for (int i = 0; i < 30; i++) ChaosRunHost.Tick();
        Assert.Equal(3.25, ChaosRunHost.FreezeRemainingSec);
        if (manual) ChaosRunHost.ToggleManualPause();
        else { Step(clock, 240); Click(); Step(clock, 150); }
        ChaosRunHost.StopTimerForTests();
        int count = BubbleOverlay.ChaosCount; // the caught pickup's pop burst may already be gone
        ChaosRunHost.SpawnTick();
        Assert.Equal(count, BubbleOverlay.ChaosCount);
        Assert.True(BubbleOverlay.Field.FreezeChaos);
        Assert.Equal(2.5, EffectPayload.GlobalDurationMult);
        for (int i = 0; i < 13; i++) ChaosRunHost.Tick();
        Assert.Equal(0, ChaosRunHost.FreezeRemainingSec);
        Assert.False(BubbleOverlay.Field.FreezeChaos);
        Assert.Equal(1, EffectPayload.GlobalDurationMult);
        ChaosRunHost.SpawnTick();
        Assert.True(BubbleOverlay.ChaosCount > count);
        ChaosRunHost.ForceShutdown();
        Assert.Equal(0, ChaosRunHost.FreezeRemainingSec);
        Assert.False(BubbleOverlay.Field.FreezeChaos);
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
            var pulse = ChaosRunHost.PulseFx;
            var audio = ConditioningControlPanel.CoreAudio.PlayOneShotProvider;
            try { body(clock); }
            finally { ChaosRunHost.ForceShutdown(); ChaosUnlockCardOverlay.Clock = TimeProvider.System;
                ChaosRunHost.PulseFx = pulse; ConditioningControlPanel.CoreAudio.PlayOneShotProvider = audio; }
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
