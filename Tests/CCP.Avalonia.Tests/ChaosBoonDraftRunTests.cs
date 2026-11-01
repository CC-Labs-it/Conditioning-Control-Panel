using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosBoonDraftRunTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static void Ticks(int count)
    {
        for (int i = 0; i < count; i++) ChaosRunHost.Tick();
    }

    // WPF ChaosModeService.cs:1441-1483, 1517-1619; ChaosOverlayWindow.xaml.cs:467-555.
    [Fact]
    public async Task Wave_draft_clears_and_holds_field_rerolls_picks_and_resumes_with_shower()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            ChaosWaveTimerWindow.SkipPlatformChecksForTests = true;
            try
            {
                ChaosMeta.State.FirstTimesAwarded?.Remove(ChaosFirstTimes.Whisper);
                ChaosMeta.State.LessonsComplete.Remove("draft4");
                ChaosMeta.State.LessonProgress.Remove("draft4");
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                    BoonDraftEnabled = true, AllowCurses = false, DraftAutoResumeSec = 0 });
                var state = ChaosRunHost.State!;
                state.ShowWaveTimer = true;
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                state.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == "welcome_shower"));
                state.RerollsLeft = 1;
                ChaosRunHost.SpawnTick();
                Assert.True(BubbleOverlay.ChaosCount > 0);
                BubbleOverlay.Field.Held = true;
                Ticks(80);
                var overlay = ChaosRunHost.Overlay!;
                var panel = overlay.FindControl<Border>("DraftPanel")!;
                var cards = overlay.FindControl<StackPanel>("BoonCardHost")!;
                Assert.True(panel.IsVisible);
                Assert.Equal(1, state.WaveIndex);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                Assert.False(BubbleOverlay.Field.Held);
                var watch = ChaosWaveTimerWindow.Active!;
                Assert.False(watch.Pill.IsVisible);
                watch.UpdateLayout();
                Assert.True(watch.Bounds.Width >= 1 && watch.Bounds.Height >= 1);
                double held = state.ElapsedSec;
                Ticks(4);
                ChaosRunHost.SpawnTick();
                ChaosRunHost.ToggleManualPause();
                Assert.Equal(held, state.ElapsedSec);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                Assert.False(ChaosRunHost.IsManuallyPaused);

                var oldCard = cards.Children[0];
                var reroll = overlay.FindControl<Button>("BtnReroll")!;
                Assert.True(reroll.IsVisible);
                reroll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, state.RerollsLeft);
                Assert.False(reroll.IsVisible);
                Assert.NotSame(oldCard, cards.Children[0]);
                var first = (Border)cards.Children[0];
                var pick = ((StackPanel)first.Child!).Children.OfType<Button>().Single();
                Assert.True(pick.IsEnabled);
                pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Single(state.RunPickTiles);
                var deadline = DateTime.UtcNow.AddSeconds(6);
                while (panel.IsVisible && DateTime.UtcNow < deadline) await Task.Delay(50);
                Assert.False(panel.IsVisible);
                Assert.Equal(2, state.RunPickTiles.Count);
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper));
                Assert.Equal(1, ChaosLessons.Progress("draft4"));
                Assert.Contains("boon:" + state.RunPickTiles[1].Id, ChaosMeta.State.DiscoveredCodexIds);
                Assert.Equal(2, ChaosRunHost.BoonBar!.Row.Children.Count);
                Assert.Equal(2, state.WaveIndex);
                Assert.Equal(held, state.ElapsedSec);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                overlay.FinishCountdown();
                Assert.Equal(6, BubbleOverlay.ChaosCount);
                Ticks(1);
                Assert.True(state.ElapsedSec > held);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosWaveTimerWindow.SkipPlatformChecksForTests = false;
            }
        });
    }

    // WPF ChaosModeService.cs:1447-1456, 1577-1593: disabled drafts roll on; skip grants resistance.
    [Fact]
    public async Task Disabled_drafts_roll_on_and_skip_adds_one_resistance()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var meta = ChaosMeta.State;
            int runs = meta.RunsCompleted;
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                Ticks(80);
                Assert.Equal(2, ChaosRunHost.State!.WaveIndex);
                Assert.False(ChaosRunHost.Overlay.FindControl<Border>("DraftPanel")!.IsVisible);
                ChaosRunHost.ForceShutdown();

                meta.RunsCompleted = 2;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                Ticks(80);
                var state = ChaosRunHost.State!;
                int shields = state.Shields;
                var skip = ChaosRunHost.Overlay.FindControl<Button>("BtnSkipBoon")!;
                Assert.True(skip.IsVisible);
                skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(shields + 1, state.Shields);
                Assert.Empty(state.RunPickTiles);
                Assert.Equal(2, state.WaveIndex);
                double held = state.ElapsedSec;
                Ticks(4);
                Assert.Equal(held, state.ElapsedSec);
                ChaosRunHost.Overlay.FinishCountdown();
                Ticks(1);
                Assert.True(state.ElapsedSec > held);
            }
            finally { ChaosRunHost.ForceShutdown(); meta.RunsCompleted = runs; }
            return Task.CompletedTask;
        });
    }
    // WPF ChaosModeService.cs:1586-1619, 190-205: a lesson defers the shower until dismissal.
    [Fact]
    public async Task Lesson_earned_at_the_table_holds_the_ready_handoff()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var meta = ChaosMeta.State;
            int runs = meta.RunsCompleted;
            try
            {
                meta.RunsCompleted = 2;
                meta.LessonsComplete.Remove("draft4");
                meta.LessonProgress.Remove("draft4");
                meta.LessonsComplete.Remove("intrusive_thoughts");
                meta.LessonProgress.Remove("intrusive_thoughts");
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                state.WelcomeShowerEnabled = true;
                Ticks(80);
                ChaosLessons.Tick("draft4", ChaosLessons.T_DRAFT4);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.False(ChaosUnlockCardOverlay.IsShowing);
                ChaosRunHost.Overlay.FindControl<Button>("BtnSkipBoon")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ChaosRunHost.Overlay.FinishCountdown();
                Assert.True(ChaosUnlockCardOverlay.IsShowing);
                Assert.False(ChaosUnlockCardOverlay.Active!.AutoDismiss);
                int queued = ChaosUnlockCardOverlay.QueuedCount;
                ChaosLessons.Tick("intrusive_thoughts", ChaosLessons.ById("intrusive_thoughts")!.Target);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(queued + 1, ChaosUnlockCardOverlay.QueuedCount);
                Assert.Equal(0, BubbleOverlay.ChaosCount);
                double held = state.ElapsedSec;
                Ticks(1);
                Assert.Equal(held, state.ElapsedSec);
                ChaosUnlockCardOverlay.Active.Close();
                Assert.Equal(6, BubbleOverlay.ChaosCount);
                Ticks(1);
                Assert.True(state.ElapsedSec > held);
            }
            finally { ChaosRunHost.ForceShutdown(); meta.RunsCompleted = runs; }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService.cs:3161-3181: shutdown closes the table and cannot commit a late pick.
    [Fact]
    public async Task Panic_during_the_pick_beat_discards_the_pending_choice()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                Ticks(80);
                var cards = ChaosRunHost.Overlay.FindControl<StackPanel>("BoonCardHost")!;
                var first = (Border)cards.Children[0];
                ((StackPanel)first.Child!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var confirm = (global::Avalonia.Threading.DispatcherTimer)typeof(ChaosOverlayWindow)
                    .GetField("_confirmTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(ChaosRunHost.Overlay)!;
                Assert.True(confirm.IsEnabled);
                ChaosRunHost.ForceShutdown();
                Assert.False(confirm.IsEnabled);
                await Task.Delay(1800);
                Assert.Empty(state.RunPickTiles);
                Assert.Null(ChaosRunHost.BoonBar);
                Assert.False(ChaosRunHost.IsActive);
            }
            finally { ChaosRunHost.ForceShutdown(); }
        });
    }

    // WPF ChaosModeService.cs:1476-1479, 1534-1566: Surrender guarantees a sin,
    // shields its first drawback, adds its multiplier bonus and returns one resistance.
    [Fact]
    public async Task Surrender_guarantees_and_shields_the_first_sin()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            try
            {
                ChaosMeta.State.FirstTimesAwarded?.Remove(ChaosFirstTimes.Yes);
                ChaosMeta.State.LessonsComplete.Remove("surrender");
                ChaosMeta.State.LessonProgress.Remove("surrender");
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                    BoonDraftEnabled = true, AllowCurses = true, DraftChoices = 2, SinChance = 0 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                state.MaxedBoons.Add("surrender");
                state.SinExtraMult = 0.25;
                double multiplier = state.BoonMult;
                int shields = state.Shields;
                Ticks(80);
                var cards = ChaosRunHost.Overlay.FindControl<StackPanel>("BoonCardHost")!;
                Assert.Equal(2, cards.Children.Count);
                await Task.Delay(600);
                var sin = (Border)cards.Children[1];
                var pick = ((StackPanel)sin.Child!).Children.OfType<Button>().Single();
                Assert.True(pick.IsEnabled);
                pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(6);
                while (state.ActiveCurses.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(50);
                var boon = Assert.Single(state.ActiveCurses);
                Assert.True(state.SurrenderShieldUsed);
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes));
                Assert.Equal(1, ChaosLessons.Progress("surrender"));
                Assert.Equal(shields + 1, state.Shields);
                Assert.Equal(multiplier + boon.RunMultBonus + 0.25, state.BoonMult, 6);
                Assert.Equal(boon.Id, Assert.Single(state.RunPickTiles).Id);
                Assert.Contains("boon:" + boon.Id, ChaosMeta.State.DiscoveredCodexIds);
            }
            finally { ChaosRunHost.ForceShutdown(); }
        });
    }

    // WPF ChaosSfx.cs:20-22, 61-73 and ModResourceResolver.cs:374-377.
    [Theory]
    [InlineData("mp3")]
    [InlineData("wav")]
    public void Wave_clear_cue_prefers_mod_audio_and_falls_back_to_bundled_sound(string extension)
    {
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-wave-cue-");
        var oldOverride = global::ConditioningControlPanel.CoreModArt.AudioOverridePathProvider;
        var oldAudio = global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider;
        var paths = new System.Collections.Generic.List<string>();
        try
        {
            var folder = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir.FullName, "resources", "sounds", "chaos"));
            var file = System.IO.Path.Combine(folder.FullName, "wave_clear." + extension);
            System.IO.File.WriteAllBytes(file, new byte[1]);
            global::ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = name =>
                global::ConditioningControlPanel.CoreModArt.ModAudioFile(name, dir.FullName);
            global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (path, _, tag, _, _) =>
            { if (tag == "chaos-sfx") paths.Add(path); };
            ChaosOverlayWindow.ChaosSfx.PlayWaveClear();
            Assert.Equal(file, Assert.Single(paths));
            System.IO.File.Delete(file);
            ChaosOverlayWindow.ChaosSfx.PlayWaveClear();
            Assert.Equal(2, paths.Count);
            Assert.EndsWith("lvup.mp3", paths[1]);
        }
        finally
        {
            global::ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = oldOverride;
            global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider = oldAudio;
            dir.Delete(true);
        }
    }

}
