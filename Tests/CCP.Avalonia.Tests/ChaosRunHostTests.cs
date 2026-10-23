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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static ChaosRunConfig Cfg() => new() { DurationSec = 60, WaveCount = 3 };

    private static void Ticks(int n) { for (int i = 0; i < n; i++) ChaosRunHost.Tick(); }

    /// <summary>A fresh in-memory save (TestIsolation puts the files back afterwards).</summary>
    private static void FreshMeta()
    {
        for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
        ChaosMeta.Init();
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
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.SpawnTick();   // countdown up: nothing spawns
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                for (int i = 0; i < 3; i++) ChaosRunHost.SpawnTick();
                Assert.True(BubbleOverlay.ChaosCount >= 3);
                var b = BubbleOverlay.Field.Bubbles.First(x => x.Spec != null && !x.Popping);
                Assert.False(b.Spec!.IsLive);   // treats only until live bubbles land
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
                for (int i = 0; i < 4 * 1000 && ChaosRunHost.IsDescending; i++) ChaosRunHost.Tick();
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

    /// <summary>WPF ChaosModeService.cs:1837/1866: a prism pop and a treat pop each count toward the
    /// lifetime pop total that pays the 100-pop sparkle milestones.</summary>
    [Fact]
    public async Task Chaos_treat_and_prism_pops_count_toward_lifetime_pops()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var prop = typeof(global::ConditioningControlPanel.Avalonia.App).GetProperty("Achievements",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
            var (achWas, firerWas) = (prop.GetValue(null), EffectPayload.Firer);
            var dir = System.IO.Directory.CreateTempSubdirectory("ccp-chaos-pops-").FullName;
            try
            {
                var engine = new AchievementEngine(new AchievementStore(System.IO.Path.Combine(dir, "a.json")));
                prop.SetValue(null, engine);
                EffectPayload.Firer = null;
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = new EffectBubbleSpec { Payload = new FlashPayload() } });
                ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = new EffectBubbleSpec { Payload = new FlashPayload(), IsPrism = true } });
                Assert.Equal(2, engine.Progress.TotalBubblesPopped);
            }
            finally { ChaosRunHost.ForceShutdown(); prop.SetValue(null, achWas); EffectPayload.Firer = firerWas; FreshMeta(); }
            return Task.CompletedTask;
        });
    }
}
