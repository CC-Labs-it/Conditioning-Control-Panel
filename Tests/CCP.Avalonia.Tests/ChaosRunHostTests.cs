using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using ConditioningControlPanel.Services;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The run-lifecycle slice of WPF ChaosModeService on a stepped clock: countdown -> 4 Hz
/// ticks -> recap; nothing ticks while the overlay is hidden or paused; panic during the countdown
/// or mid-run tears everything down and pays nothing; FALL IN starts a run whose end banks the payout.</summary>
public sealed class ChaosRunHostTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static ChaosRunConfig Cfg() => new() { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false };

    private static void Ticks(int n) { for (int i = 0; i < n; i++) ChaosRunHost.Tick(); }

    /// <summary>A fresh in-memory save (TestIsolation puts the files back afterwards).</summary>
    private static void FreshMeta()
    {
        for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
        ChaosMeta.Init();
    }

    private static void CommitDraft(ChaosOverlayWindow overlay, ChaosBoon? boon) =>
        typeof(ChaosOverlayWindow).GetMethod("ChooseBoon",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(overlay, new object?[] { boon });

    // WPF ChaosModeService.cs:1440-1485,1533-1620; ChaosWaveTimerOverlay.cs:62-69.
    [Fact]
    public async Task Wave_draft_blanks_watch_clears_without_detonating_and_waits_for_pick_and_go()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            ChaosWaveTimerWindow.SkipPlatformChecksForTests = true;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                    BoonDraftEnabled = true, DraftAutoResumeSec = 0 });
                var state = ChaosRunHost.State!;
                state.ShowWaveTimer = true;
                var overlay = ChaosRunHost.Overlay!;
                overlay.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                Ticks(79);
                var watch = ChaosWaveTimerWindow.Active!;
                Assert.True(watch.Pill.IsVisible);
                BubbleOverlay.SpawnChaos(LiveSpec());
                int detonated = state.Detonated;
                Ticks(1);
                Assert.True(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
                Assert.Same(watch, ChaosWaveTimerWindow.Active);
                Assert.False(watch.Pill.IsVisible);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                Assert.Equal(detonated, state.Detonated);
                Assert.Equal(1, state.WaveIndex);
                double elapsed = state.ElapsedSec;
                var toy = new ChaosToyState { Id = "vibe_popping", CooldownSec = 20 };
                state.ActiveToys.Add(toy);
                ChaosRunHost.UseToyById(toy.Id);
                Assert.Equal(0, toy.CooldownRemainingSec);
                Ticks(8);
                ChaosRunHost.SpawnTick();
                ChaosRunHost.ToggleManualPause();
                Assert.False(ChaosRunHost.IsManuallyPaused);
                Assert.Equal(elapsed, state.ElapsedSec);
                Assert.Equal(0, BubbleOverlay.ChaosCount);

                int shields = state.Shields;
                var pick = ChaosBoonPool.All.Single(b => b.Id == "extra_shield");
                CommitDraft(overlay, pick);   // the draft's actual commit callback
                Assert.Contains(pick, state.ActiveBoons);
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper));
                Assert.Equal(1, ChaosLessons.Progress("draft4"));
                Assert.Equal(shields + 2, state.Shields);
                Assert.Equal(2, state.WaveIndex);
                Ticks(8);
                Assert.Equal(elapsed, state.ElapsedSec);
                Assert.False(watch.Pill.IsVisible);
                overlay.FinishCountdown();
                Ticks(1);
                Assert.True(state.ElapsedSec > elapsed);
                Assert.True(watch.Pill.IsVisible);
                Assert.Same(watch, ChaosWaveTimerWindow.Active);
                ChaosRunHost.UseToyById(toy.Id);
                Assert.Equal(20, toy.CooldownRemainingSec);
                ChaosRunHost.SpawnTick();
                Assert.True(BubbleOverlay.ChaosCount > 0);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosWaveTimerWindow.SkipPlatformChecksForTests = false;
                FreshMeta();
            }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService.cs:1520-1530,1540-1564,1577-1599.
    [Fact]
    public async Task Draft_rerolls_spend_charges_skip_adds_resistance_and_surrender_shields_the_first_sin()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                    BoonDraftEnabled = true, DraftAutoResumeSec = 0 });
                var state = ChaosRunHost.State!;
                var overlay = ChaosRunHost.Overlay!;
                overlay.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                state.RerollsLeft = 1;
                state.MaxedBoons.Add("surrender");
                state.ApplyBoon(ChaosBoonPool.All.Single(b => b.Id == "gold_digger"));
                Ticks(80);
                var reroll = (Func<(List<ChaosBoon> options, int rerollsLeft)?>)typeof(ChaosOverlayWindow)
                    .GetField("_rerollFunc", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(overlay)!;
                var deal = reroll();
                Assert.NotNull(deal);
                Assert.Equal(state.Config.DraftChoices, deal.Value.options.Count);
                Assert.Equal(0, deal.Value.rerollsLeft);
                Assert.Contains(deal.Value.options, b => b.IsCurse);
                Assert.DoesNotContain(deal.Value.options, b => b.Id == "gold_digger");
                Assert.All(deal.Value.options, b => Assert.Contains("boon:" + b.Id, ChaosMeta.State.DiscoveredCodexIds));
                Assert.Equal(0, state.RerollsLeft);
                Assert.Null(reroll());
                int shields = state.Shields;
                CommitDraft(overlay, null);
                Assert.Equal(shields + 1, state.Shields);
                overlay.FinishCountdown();
                Ticks(80);
                state.MaxedBoons.Add("surrender");
                state.SinExtraMult = 0.2;
                var sin = ChaosBoonPool.All.Single(b => b.Id == "hair_trigger");
                double fuse = state.FuseTimeMult;
                double mult = state.BoonMult;
                shields = state.Shields;
                CommitDraft(overlay, sin);
                Assert.True(state.SurrenderShieldUsed);
                Assert.Equal(fuse, state.FuseTimeMult);   // the shield keeps the upside, drops the drawback
                Assert.Contains(sin, state.ActiveCurses);
                Assert.Equal(shields + 1, state.Shields);
                Assert.Equal(mult + sin.RunMultBonus + 0.2, state.BoonMult, 6);
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes));
                ChaosRunHost.ForceShutdown();
                // A stale GO callback cannot restart the stopped run.
                overlay.FinishCountdown();
                Assert.False(ChaosRunHost.IsDescending);
                Assert.Null(ChaosWaveTimerWindow.Active);
            }
            finally { ChaosRunHost.ForceShutdown(); FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    private sealed class CardClock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }

    // WPF ChaosModeService.cs:1602-1620: queued lessons take over the draft's pause after GO.
    [Fact]
    public async Task Draft_lessons_wait_for_go_and_last_card_dismissal_resumes_the_field()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var oldClock = ChaosUnlockCardOverlay.Clock;
            var clock = new CardClock();
            ChaosUnlockCardOverlay.Clock = clock;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                    BoonDraftEnabled = true, DraftAutoResumeSec = 0 });
                var overlay = ChaosRunHost.Overlay!;
                overlay.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                Ticks(80);
                ChaosLessons.Tick("taking_chances", ChaosLessons.T_TAKING_CHANCES);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(ChaosUnlockCardOverlay.IsShowing);
                CommitDraft(overlay, null);
                double elapsed = ChaosRunHost.State!.ElapsedSec;
                overlay.FinishCountdown();
                Assert.True(ChaosUnlockCardOverlay.IsShowing);
                Ticks(8);
                ChaosRunHost.SpawnTick();
                Assert.Equal(elapsed, ChaosRunHost.State.ElapsedSec);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                int cards = 0;
                while (ChaosUnlockCardOverlay.IsShowing && cards++ < 10)
                {
                    var card = ChaosUnlockCardOverlay.Active!;
                    Assert.False(card.AutoDismiss);
                    clock.Ticks += TimeSpan.FromSeconds(1).Ticks;
                    card.Step();
                    card.Dismiss();
                    clock.Ticks += TimeSpan.FromMilliseconds(150).Ticks;
                    card.Step();
                }
                Assert.False(ChaosUnlockCardOverlay.IsShowing);
                Ticks(1);
                Assert.True(ChaosRunHost.State.ElapsedSec > elapsed);
                ChaosRunHost.SpawnTick();
                Assert.True(BubbleOverlay.ChaosCount > 0);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosUnlockCardOverlay.Clock = oldClock;
                FreshMeta();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task A_run_counts_down_ticks_and_ends_on_the_recap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var meta = ChaosMeta.State;
            try
            {
                ChaosRunHost.StartRun(Cfg());
                var overlay = ChaosRunHost.Overlay!;
                var state = ChaosRunHost.State!;
                Assert.True(overlay.IsVisible);
                Assert.Empty(overlay.FindControl<StackPanel>("ResultsBody")!.Children);   // ForRun: no sample recap (its bark/sfx)

                Ticks(8);   // countdown still up: the clock has not started
                Assert.Equal(0, state.ElapsedSec);

                overlay.FinishCountdown();   // the countdown's completion is BeginRun
                Assert.True(ChaosRunHost.IsDescending);
                Ticks(84);   // 21 s
                Assert.Equal(21, state.ElapsedSec, 3);
                Assert.Equal(2, state.WaveIndex);

                overlay.Hide();   // P01: hidden overlay -> nothing ticks
                Ticks(40);
                Assert.Equal(21, state.ElapsedSec, 3);
                overlay.Show();

                ChaosRunHost.ToggleManualPause();   // held: the clock waits
                Ticks(40);
                Assert.Equal(21, state.ElapsedSec, 3);
                ChaosRunHost.ToggleManualPause();

                Ticks(160);   // to 61 s: past the 60 s run
                Assert.False(ChaosRunHost.IsDescending);
                Assert.Null(ChaosRunHost.Hud);
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                overlay.UpdateLayout();
                Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "BEST STREAK");
                Assert.Equal(1, meta.RunsCompleted);   // the run counted (payout: see the FALL IN test)
                Assert.Same(meta, ChaosMeta.State);
            }
            finally { ChaosRunHost.ForceShutdown(); FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF ChaosToyButtonWindow: one hero button per equipped toy; a real click fires the
    /// toy (cooldown starts, the status counts down on the run clock); the toy key rides the panic
    /// key's listener; panic closes every button and unhooks the key.</summary>
    [Fact]
    public async Task Toy_button_fires_on_click_counts_down_and_panic_closes_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var meta = ChaosMeta.State;
            int pockets = meta.ToyPockets;
            bool hadActive = meta.ActiveLifetimeBoons.Contains("vibe_popping");
            bool hadLevel = meta.LifetimeBoonLevels.TryGetValue("vibe_popping", out int level);
            var s = CoreSettings.Current;
            (bool pe, string pk, string k1) = (s.PanicKeyEnabled, s.PanicKey, s.ChaosAccessoryKey1);
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            s.ChaosAccessoryKey1 = "Q";
            meta.ToyPockets = 1;
            meta.ActiveLifetimeBoons.Add("vibe_popping");
            meta.LifetimeBoonLevels["vibe_popping"] = 1;
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();   // P08: only stepped Ticks move the run clock
                var btn = Assert.Single(ChaosRunHost.ToyButtons);
                Assert.True(btn.IsVisible);
                Assert.Equal("ready", btn.Status.Text);
                Assert.Equal(new[] { "Q" }, ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKeys!());

                // Hit testing reads the compositor's last frame: wait until a frame with the button is drawn
                // (one forced tick is not enough once the run opens its other overlay windows).
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var batch = global::Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(btn)!.Compositor.RequestCompositionBatchCommitAsync();
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (!batch.Rendered.IsCompleted)
                {
                    Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10), "the compositor never drew the toy button");
                    global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                }
                var mid = new Point(btn.Width / 2, btn.Height / 2);
                btn.MouseDown(mid, global::Avalonia.Input.MouseButton.Left);
                btn.MouseUp(mid, global::Avalonia.Input.MouseButton.Left);
                Assert.Equal(20, btn.Toy.CooldownRemainingSec);   // VibePopping's 20 s cooldown
                Assert.Equal("20s", btn.Status.Text);
                Ticks(4);   // one run-clock second
                Assert.Equal("19s", btn.Status.Text);

                ChaosRunHost.OnToyKey("F8");   // the panic key never fires a toy
                ChaosRunHost.OnToyKey("Q");    // cooling: denied, the cooldown is untouched
                Assert.Equal(19, btn.Toy.CooldownRemainingSec, 3);

                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
                Assert.False(btn.IsVisible);
                Assert.Empty(ChaosRunHost.ToyButtons);
                Assert.Null(ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKey);
            }
            finally
            {
                ChaosRunHost.ForceShutdown(); shell.Close();
                meta.ToyPockets = pockets;
                if (!hadActive) meta.ActiveLifetimeBoons.Remove("vibe_popping");
                if (hadLevel) meta.LifetimeBoonLevels["vibe_popping"] = level; else meta.LifetimeBoonLevels.Remove("vibe_popping");
                (s.PanicKeyEnabled, s.PanicKey, s.ChaosAccessoryKey1) = (pe, pk, k1);
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>The hub offers digit toy keys ("1"-"4"); on the Win32 hook the digit row is D1..D4.</summary>
    [Fact]
    public void Win32_hook_fires_a_digit_toy_key()
    {
        var fired = new List<string>();
        try
        {
            ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKeys = () => new[] { "1" };
            ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKey = fired.Add;
            ConditioningControlPanel.Avalonia.Platform.Win32PanicKey.OnDown(0x31, DateTime.UtcNow);   // VK '1'
            ConditioningControlPanel.Avalonia.Platform.Win32PanicKey.OnUp(0x31);
        }
        finally
        {
            ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKey = null;
            ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKeys = null;
        }
        Assert.Equal(new[] { "1" }, fired);
    }

    /// <summary>WPF EndRun (ChaosModeService.cs:3228) closes the braindrain wash itself, so it never sits over the results card.</summary>
    [Fact]
    public async Task The_run_ending_closes_the_wash_before_the_results_card()
    {
        var gif = ChaosFlashOverlayTests.WriteGif(out var dir);
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var schedule = ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.Schedule;
            try
            {
                ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.SkipPlatformChecksForTests = true;
                ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.Schedule = (_, _) => { };
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                await ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.ShowPath(
                    new[] { new PixelRect(0, 0, 1920, 1080) }, gif, 60000, 0.30);
                Assert.True(ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.IsShowing);
                Ticks(244);   // past the 60 s run
                Assert.True(ChaosRunHost.Overlay!.FindControl<Border>("ResultsPanel")!.IsVisible);
                Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.IsShowing);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.CloseActive();
                ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.SkipPlatformChecksForTests = false;
                ConditioningControlPanel.Avalonia.Views.Overlays.ChaosFlashOverlay.Schedule = schedule;
                System.IO.Directory.Delete(dir, true);
            }
        });
    }

    /// <summary>Decisions 2026-10-08 (panic-surfaces): the safe word, the only exit under Lockdown, ends a run.</summary>
    [Fact]
    public async Task The_safe_word_ends_the_run()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                Assert.True(ChaosRunHost.IsDescending);
                shell.VoicePanic();
                Assert.False(ChaosRunHost.IsActive || ChaosRunHost.IsDescending);
            }
            finally { ChaosRunHost.ForceShutdown(); shell.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Panic_during_the_countdown_and_mid_run_ends_the_run()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            var shell = new MainShellWindow();
            shell.Show();
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            try
            {
                // During the countdown.
                ChaosRunHost.StartRun(Cfg());
                var overlay = ChaosRunHost.Overlay!;
                var hud = ChaosRunHost.Hud!;
                shell.HandlePanicKeyPress(t0);
                Assert.False(ChaosRunHost.IsActive);
                Assert.False(overlay.IsVisible || hud.IsVisible);
                overlay.FinishCountdown();   // a late countdown completion must not revive it
                Assert.False(ChaosRunHost.IsDescending);

                // Mid-run, 3 s on (outside the 2 s double-press window).
                ChaosRunHost.StartRun(Cfg());
                overlay = ChaosRunHost.Overlay!;
                hud = ChaosRunHost.Hud!;
                overlay.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.Score = 500;
                var meta = ChaosMeta.State;
                var before = (meta.RunsCompleted, meta.Sparks, meta.Gold, meta.BestScore);
                bool paidXp = false;   // the Core* seam is restored by TestIsolation
                CoreProgression.AddXPProvider = (_, _) => paidXp = true;
                Ticks(8);
                shell.HandlePanicKeyPress(t0.AddSeconds(3));
                Assert.False(ChaosRunHost.IsActive || ChaosRunHost.IsDescending);
                // WPF ForceShutdown: no results, no payout.
                Assert.Equal(before, (meta.RunsCompleted, meta.Sparks, meta.Gold, meta.BestScore));
                Assert.False(paidXp);
                Assert.False(overlay.IsVisible || hud.IsVisible);
                double at = state.ElapsedSec;
                Ticks(8);   // a stale timer tick after the teardown does nothing
                Assert.Equal(at, state.ElapsedSec);
            }
            finally { ChaosRunHost.ForceShutdown(); shell.Close(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF MainWindow.xaml.cs:1621,1626: the press that ends a descent does not arm the
    /// double-press exit, so a reflexive double tap mid-run ends the run and keeps the app up.</summary>
    [Fact]
    public async Task Two_quick_panic_presses_during_a_run_end_it_without_quitting()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var s = CoreSettings.Current;
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            var shell = new MainShellWindow();
            shell.Show();
            var closed = false;
            shell.Closed += (_, _) => closed = true;
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                Ticks(4);
                shell.HandlePanicKeyPress(t0);
                shell.HandlePanicKeyPress(t0.AddSeconds(0.5));
                Assert.False(ChaosRunHost.IsActive);
                Assert.False(closed);
            }
            finally { ChaosRunHost.ForceShutdown(); if (!closed) shell.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Hud_exit_surfaces_the_run_onto_the_recap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                var overlay = ChaosRunHost.Overlay!;
                overlay.FinishCountdown();
                Ticks(4);
                var exit = ChaosRunHost.Hud!.GetVisualDescendants().OfType<Button>()
                    .Single(b => b.Content is ConditioningControlPanel.Avalonia.Controls.IconLabel { Text: "wake up", Kind: FluentIcons.Common.Symbol.Dismiss });
                exit.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.False(ChaosRunHost.IsDescending);
                Assert.True(ChaosRunHost.IsActive);   // recap up until dismissed
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                overlay.Close();   // "wake up": OnDismissed tears down
                Assert.False(ChaosRunHost.IsActive);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF SpawnTick + OnBenignPopped on BubbleOverlay: stepped spawns fill the field,
    /// a pop scores, and panic clears every chaos bubble.</summary>
    [Fact]
    public async Task Spawn_ticks_fill_the_field_a_pop_scores_and_panic_clears_it()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                var cfg = Cfg();
                cfg.EnabledVariants = new() { "flash", "subliminal" };   // treats: this test pops by click
                ChaosRunHost.StartRun(cfg);
                ChaosRunHost.SpawnTick();   // countdown up: nothing spawns
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                for (int i = 0; i < 3; i++) ChaosRunHost.SpawnTick();
                Assert.True(BubbleOverlay.ChaosCount >= 3);
                var b = BubbleOverlay.Field.Bubbles.First(x => x.Spec != null && !x.Popping);
                Assert.False(b.Spec!.IsLive);   // this click-to-pop test explicitly enables only treats
                BubbleOverlay.Pop(b);
                Assert.True(state.Score > 0);
                Assert.Equal(1, state.Combo);

                // WPF ToggleManualPause :265-268: a held field neither moves nor takes clicks.
                var held = BubbleOverlay.Field.Bubbles.First(x => x.Spec != null && !x.Popping);
                double score = state.Score, y = held.Y;
                ChaosRunHost.ToggleManualPause();
                BubbleOverlay.Field.Step();
                BubbleOverlay.Pop(held);
                Assert.Equal((y, false, score), (held.Y, held.Popping, state.Score));
                ChaosRunHost.ToggleManualPause();
                BubbleOverlay.Field.Step();
                Assert.True(held.Y < y);   // moving again

                // Headless never opens windows, so fake the chaos-only state: it is not the ambient game.
                var t = typeof(BubbleOverlay);
                const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
                t.GetField("_running", F)!.SetValue(null, true);
                t.GetField("_chaosOpenedWindows", F)!.SetValue(null, true);
                try { Assert.False(BubbleOverlay.IsRunning); }
                finally { t.GetField("_running", F)!.SetValue(null, false); t.GetField("_chaosOpenedWindows", F)!.SetValue(null, false); }

                ChaosRunHost.ForceShutdown();
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                Assert.DoesNotContain(BubbleOverlay.Field.Bubbles, x => x.Spec != null);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF ChaosHubWindow BtnBegin_Click :1718 -> StartRun; EndRun :3243-3268 banks
    /// AddXP(base), Sparks (AwardRunRewards) and the loop tips. Expected values use WPF's formulas
    /// written out here, not ChaosPayout.</summary>
    [Fact]
    public async Task Fall_in_starts_a_run_and_its_end_banks_the_wpf_payout()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var xp = new System.Collections.Generic.List<(double Amount, string Source)>();
            CoreProgression.AddXPProvider = (a, src) => xp.Add((a, src));   // TestIsolation restores it
            ChaosHubWindow? hub = null;
            try
            {
                hub = new ChaosHubWindow();
                hub.FindControl<Button>("BtnMenuFallIn")!
                    .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(ChaosRunHost.IsActive);
                Assert.Equal(ChaosPlayMode.Story, ChaosRunHost.State!.Config.PlayMode);

                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.Score = 1234;
                var meta = ChaosMeta.State;
                int gold0 = meta.Gold;
                for (int i = 0; i < 4 * 1000 && ChaosRunHost.IsDescending; i++)
                {
                    ChaosRunHost.Tick();
                    var overlay = ChaosRunHost.Overlay!;
                    if (overlay.FindControl<Border>("DraftPanel")!.IsVisible)
                    {
                        CommitDraft(overlay, null);
                        overlay.FinishCountdown();
                    }
                }
                Assert.False(ChaosRunHost.IsDescending);

                double durMin = state.RunDurationSec / 60.0, diff = state.Config.DifficultyMult;
                var paid = Assert.Single(xp);
                Assert.Equal("Chaos", paid.Source);
                Assert.Equal(Math.Min(1234, 250.0 * durMin * diff), paid.Amount, 6);
                int sparks = (int)Math.Round((1.5 * Math.Sqrt(1234) + 35.0 * diff * Math.Min(1.0, durMin / 3.0))
                                             * state.Config.SparkGainMult) + ChaosMeta.FIRST_FALL_BONUS;
                Assert.Equal(sparks, meta.Sparks);
                Assert.Equal((1, 1234L), (meta.RunsCompleted, meta.BestScore));
                // One loop tip per loop (the last on the full course), each >= 2 x round(3 x diff) clean.
                Assert.True(meta.Gold - gold0 >= state.WaveCount * 2 * (int)Math.Round(3 * diff),
                    $"gold {gold0} -> {meta.Gold}");
                Assert.True(ChaosRunHost.Overlay!.FindControl<Border>("ResultsPanel")!.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); hub?.Close(); FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF OnDetonated (:2055) -> _waveDetonations -> AwardLoopTip (:1743): a loop with a
    /// detonation tips round(roll x diff), a clean one twice that, and only a clean loop teaches
    /// silk_touch (ChaosLessonHooks.OnLoopCompleted :193). Expected gold uses WPF's formula with
    /// the host's seeded dice. Panic (ForceShutdown) after a detonation still pays nothing.</summary>
    [Fact]
    public async Task A_detonation_costs_the_clean_loop_tip_and_silk_touch_and_panic_pays_nothing()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            CoreProgression.AddXPProvider = (_, _) => { };   // TestIsolation restores it
            var oldRng = ChaosRunHost.Rng;
            static EffectBubbleSpec Live() => new()
                { VariantId = "pink", IsLive = true, FuseMs = 320, SizePx = 200, Payload = new FlashPayload() };
            void Detonate()
            {
                var b = BubbleOverlay.SpawnChaos(Live());
                for (int i = 0; i < 10; i++) BubbleOverlay.Field.Step();   // 10 x 32 ms burns the fuse
                Assert.True(b.Popping);
            }
            (int Gold, bool SilkAfterLoop1, int Detonated, long Popup) Run(bool detonate)
            {
                FreshMeta();
                ChaosRunHost.Rng = new Random(7);
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                int gold0 = ChaosMeta.State.Gold;
                state.Shields = 2;   // one absorbs the detonation, one is left at the end
                if (detonate) Detonate();
                int loop1 = state.WaveIndex;
                while (state.WaveIndex == loop1) ChaosRunHost.Tick();
                bool silk = ChaosLessons.IsComplete("silk_touch");
                for (int i = 0; i < 4 * 1000 && ChaosRunHost.IsDescending; i++) ChaosRunHost.Tick();
                Assert.False(ChaosRunHost.IsDescending);
                int detonated = state.Detonated;
                ChaosRunHost.ForceShutdown();
                return (ChaosMeta.State.Gold - gold0, silk, detonated, ChaosLessons.Progress("popup_notification"));
            }
            try
            {
                var clean = Run(detonate: false);
                var dirty = Run(detonate: true);
                double diff = Cfg().DifficultyMult;
                var r = new Random(7);
                int t1 = (int)Math.Round(r.Next(3, 7) * diff), t2 = (int)Math.Round(r.Next(3, 7) * diff),
                    t3 = (int)Math.Round(r.Next(3, 7) * diff);
                Assert.Equal((2 * (t1 + t2 + t3), true, 0, 1L), clean);   // popup_notification: ended with resistance held
                Assert.Equal((t1 + 2 * (t2 + t3), false, 1, 1L), dirty);

                // Panic mid-run after a detonation: no tip, no XP, no Sparks, no run counted.
                FreshMeta();
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var meta = ChaosMeta.State;
                var before = (meta.Gold, meta.Sparks, meta.RunsCompleted);
                Detonate();
                Ticks(4 * 25);   // across the first loop boundary (60 s / 3 loops)
                int tipped = meta.Gold - before.Gold;
                ChaosRunHost.ForceShutdown();
                Assert.Equal((before.Gold + tipped, before.Sparks, before.RunsCompleted), (meta.Gold, meta.Sparks, meta.RunsCompleted));
                Assert.DoesNotContain(BubbleOverlay.Field.Bubbles, x => x.Spec != null);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.Rng = oldRng; FreshMeta(); }
            return Task.CompletedTask;
        });
    }
    /// <summary>WPF OnPlayerPress -> CanChannelDefuse (:1927) -> TickChannel -> OnDefused (:1984): a press
    /// on a live bubble with the focus to pay holds it to a snap that costs DEFUSE_COST and scores;
    /// without the focus it triggers in the grip.</summary>
    [Fact]
    public async Task A_held_press_snaps_a_live_and_no_focus_sets_it_off()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.Focus = 100;
                var t = DateTime.UnixEpoch;
                BubbleOverlay.Field.Clock = () => t = t.AddMilliseconds(32);
                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() });
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                for (int i = 0; i < 40 && !b.Popping; i++) BubbleOverlay.Field.Step();
                Assert.Equal((1, 0, 100 - ChaosTuning.DEFUSE_COST), (state.Defused, state.Detonated, state.Focus));
                Assert.True(state.Score > 0);

                state.Focus = 0;
                var c = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() });
                BubbleOverlay.Press(c, c.CenterX, c.CenterY);
                Assert.Equal((1, "nofocus"), (state.Detonated, c.BrokenReason));
            }
            finally { ChaosRunHost.ForceShutdown(); BubbleOverlay.Field.Clock = () => DateTime.UtcNow; FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    private static EffectBubbleSpec LiveSpec() => new()
        { VariantId = "pink", IsLive = true, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() };

    /// <summary>WPF polls the physical button (BubbleService.cs:545): a release the window never saw
    /// (capture lost: alt-tab, compositor grab) still breaks the hold and sets the bubble off.</summary>
    [Fact]
    public async Task A_lost_pointer_capture_breaks_the_hold()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.Focus = 100;
                var t = DateTime.UnixEpoch;
                BubbleOverlay.Field.Clock = () => t;
                var b = BubbleOverlay.SpawnChaos(LiveSpec());
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                Assert.True(b.Channeling);
                t = t.AddMilliseconds(500);   // held past a click, short of the snap
                var w = new BubbleOverlayWindow();
                w.RaiseEvent(new global::Avalonia.Input.PointerCaptureLostEventArgs(w,
                    new global::Avalonia.Input.Pointer(global::Avalonia.Input.Pointer.GetNextFreeId(), global::Avalonia.Input.PointerType.Mouse, true)));
                BubbleOverlay.Field.Step();
                Assert.Equal((1, "release"), (state.Detonated, b.BrokenReason));
            }
            finally { ChaosRunHost.ForceShutdown(); BubbleOverlay.Field.Clock = () => DateTime.UtcNow; FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF Snap Chain (ChaosModeService.cs:2000, :2079) is a wall-clock window after a snap:
    /// a detonation inside it holds the combo, one after it breaks it, with the run clock standing still.</summary>
    [Fact]
    public async Task The_snap_chain_window_runs_on_wall_time()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.Focus = 100;
                state.DefuseInvulnMs = 900;
                var t = DateTime.UnixEpoch;
                BubbleOverlay.Field.Clock = () => t;
                BubbleOverlay.SpawnChaos(LiveSpec());
                BubbleOverlay.Field.DefuseAllLive();
                Assert.Equal(1, state.Combo);

                t = t.AddMilliseconds(500);
                state.Focus = 0;
                var c = BubbleOverlay.SpawnChaos(LiveSpec());
                BubbleOverlay.Press(c, c.CenterX, c.CenterY);
                Assert.Equal((1, 1), (state.Detonated, state.Combo));   // inside the window: held

                t = t.AddMilliseconds(500);
                var d = BubbleOverlay.SpawnChaos(LiveSpec());
                BubbleOverlay.Press(d, d.CenterX, d.CenterY);
                Assert.Equal((2, 0), (state.Detonated, state.Combo));   // past it: the combo breaks
            }
            finally { ChaosRunHost.ForceShutdown(); BubbleOverlay.Field.Clock = () => DateTime.UtcNow; FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>The run's juice hooks recorded in order: labels (PopText), cues (CoreAudio), pulses, shakes.</summary>
    private static System.Collections.Generic.List<string> Juice()
    {
        var log = new System.Collections.Generic.List<string>();
        ChaosRunHost.PopText = (_, y, w, _) => log.Add("label:" + w);
        ChaosRunHost.PulseFx = (c, s) => log.Add($"pulse:{c.R},{c.G},{c.B}@{s:0.00}");
        ChaosRunHost.ShakeFx = (i, ms) => log.Add($"shake:{i:0.000}/{ms}");
        CoreAudio.PlayOneShotProvider = (p, _, _, _, _) => log.Add("sfx:" + System.IO.Path.GetFileNameWithoutExtension(p));
        return log;
    }

    private static void Unjuice()
    {
        ChaosRunHost.PopText = null; ChaosRunHost.PulseFx = null; ChaosRunHost.ShakeFx = null;
        CoreAudio.PlayOneShotProvider = null;
    }

    /// <summary>WPF CompleteDefuse (SNAP, :4577) + the hiss (:1571) + OnDefused's green confirm (:2049);
    /// OnPlayerPress's NO FOCUS (:4502) + OnChannelBroken (:1943) + Detonate's effect word (:4862);
    /// and OnDetonated's three outcomes (:2091 shield, :2104 collar, :2126 bare) with their cue,
    /// HUD flash, pulse and shake, at the run's shake intensity.</summary>
    [Fact]
    public async Task Snap_no_focus_and_each_detonation_outcome_label_cue_pulse_and_shake_like_wpf()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var log = Juice();
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, ShakeIntensity = 0.5 });
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.Focus = 100;
                var t = DateTime.UnixEpoch;
                BubbleOverlay.Field.Clock = () => t = t.AddMilliseconds(32);
                log.Clear();   // the countdown's own ticks
                var b = BubbleOverlay.SpawnChaos(LiveSpec());
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                for (int i = 0; i < 40 && !b.Popping; i++) BubbleOverlay.Field.Step();
                Assert.Equal(new[] { "sfx:defuse_hiss", "label:SNAP", "pulse:90,255,150@0.16" }, log.Where(e => !e.StartsWith("label:+")));

                string[] Press(int shields, int collars)
                {
                    log.Clear();
                    state.Focus = 0; state.Shields = shields; state.CollarSaves = collars;
                    var c = BubbleOverlay.SpawnChaos(LiveSpec());
                    BubbleOverlay.Press(c, c.CenterX, c.CenterY);
                    return log.ToArray();
                }
                double s = LiveSpec().Strength / 100.0;
                string[] noFocus = { "label:NO FOCUS", "sfx:focus_empty", "pulse:255,80,80@0.22", "label:PINK" };
                Assert.Equal(noFocus.Concat(new[] { "sfx:resist_absorb", "pulse:80,160,255@0.28",
                    $"shake:{(0.25 + s * 0.3) * 0.5:0.000}/280" }), Press(2, 0));
                Assert.Equal("sfx:resist_crumble", Press(1, 0)[4]);   // the last point has its own cue
                Assert.Equal(noFocus.Concat(new[] { "sfx:collar_save", "pulse:255,215,0@0.32",
                    $"shake:{(0.25 + s * 0.3) * 0.5:0.000}/280" }), Press(0, 1));
                Assert.Equal(noFocus.Concat(new[] { "sfx:trigger", $"pulse:255,50,50@{0.4 + s * 0.35:0.00}",
                    $"shake:{(0.4 + s * 0.5) * 0.5:0.000}/380" }), Press(0, 0));
            }
            finally { Unjuice(); ChaosRunHost.ForceShutdown(); BubbleOverlay.Field.Clock = () => DateTime.UtcNow; FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF onBenignPop (BubbleService.cs:1565): the ambient pop sound, then Bubble.Pop's
    /// effect word in the variant tint (:4685) before the treat scores.</summary>
    [Fact]
    public async Task A_treat_pop_plays_the_pop_and_flashes_its_effect_word()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var log = Juice();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                log.Clear();
                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { VariantId = "spiral", SizePx = 200, Payload = new FlashPayload() });
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                Assert.Equal(new[] { "sfx:", "label:SPIRAL" },
                    log.Where(e => !e.StartsWith("label:+")).Select(e => e.StartsWith("sfx:Pop") ? "sfx:" : e));
            }
            finally { Unjuice(); ChaosRunHost.ForceShutdown(); FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF Pulse/Shake (:1672) honour the run's flash and shake toggles; panic clears a live
    /// field without a label, cue or effect (EndChaosMode fires nothing).</summary>
    [Fact]
    public async Task Flash_and_shake_toggles_mute_the_juice_and_panic_fires_nothing()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var log = Juice();
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                    ColorFlashesEnabled = false, ScreenShakeEnabled = false });
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.Focus = 0; state.Shields = 0; state.CollarSaves = 0;
                var c = BubbleOverlay.SpawnChaos(LiveSpec());
                BubbleOverlay.Press(c, c.CenterX, c.CenterY);
                Assert.Equal(1, state.Detonated);
                Assert.DoesNotContain(log, e => e.StartsWith("pulse:") || e.StartsWith("shake:"));
                Assert.Contains("sfx:trigger", log);

                BubbleOverlay.SpawnChaos(LiveSpec());
                log.Clear();
                ChaosRunHost.ForceShutdown();
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                Assert.Empty(log);
            }
            finally { Unjuice(); ChaosRunHost.ForceShutdown(); FreshMeta(); }
            return Task.CompletedTask;
        });
    }
}
