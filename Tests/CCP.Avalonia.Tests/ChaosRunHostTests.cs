using System;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The run-lifecycle slice of WPF ChaosModeService on a stepped clock: countdown -> 4 Hz
/// ticks -> recap; nothing ticks while the overlay is hidden or paused; panic during the countdown
/// or mid-run tears everything down; the recap never saves.</summary>
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

    [Fact]
    public async Task A_run_counts_down_ticks_and_ends_on_the_recap_without_saving()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var meta = ChaosMeta.State;
            int runs = meta.RunsCompleted; long best = meta.BestScore; int sparks = meta.Sparks;
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
                Assert.Equal((runs, best, sparks), (meta.RunsCompleted, meta.BestScore, meta.Sparks));   // no payout
                Assert.Same(meta, ChaosMeta.State);
            }
            finally { ChaosRunHost.ForceShutdown(); }
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
                var btn = Assert.Single(ChaosRunHost.ToyButtons);
                Assert.True(btn.IsVisible);
                Assert.Equal("ready", btn.Status.Text);
                Assert.Equal(new[] { "Q" }, ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKeys!());

                global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var mid = new Point(btn.Width / 2, btn.Height / 2);
                btn.MouseDown(mid, global::Avalonia.Input.MouseButton.Left);
                btn.MouseUp(mid, global::Avalonia.Input.MouseButton.Left);
                // The real 4 Hz run timer may tick inside the input pump, so read the cooldown back.
                double cd = btn.Toy.CooldownRemainingSec;
                Assert.InRange(cd, 1, 20);    // 20 s VibePopping cooldown started by the click
                Assert.Equal($"{Math.Ceiling(cd):0}s", btn.Status.Text);
                Ticks(4);   // one run-clock second
                Assert.Equal(cd - 1, btn.Toy.CooldownRemainingSec, 3);
                Assert.Equal($"{Math.Ceiling(cd - 1):0}s", btn.Status.Text);

                ChaosRunHost.OnToyKey("F8");   // the panic key never fires a toy
                ChaosRunHost.OnToyKey("Q");    // cooling: denied, the cooldown is untouched
                Assert.Equal(cd - 1, btn.Toy.CooldownRemainingSec, 3);

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
                Ticks(8);
                shell.HandlePanicKeyPress(t0.AddSeconds(3));
                Assert.False(ChaosRunHost.IsActive || ChaosRunHost.IsDescending);
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
}
