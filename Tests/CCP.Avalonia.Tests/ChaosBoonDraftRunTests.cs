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

    private static void Ticks(int count) {
        for (int i = 0; i < count; i++) ChaosRunHost.Tick();
    }
    // WPF ChaosModeService.cs:1521, 1555-1561, 1580: all feed messages ship in every language.
    [Fact] public void Draft_feed_translations_preserve_the_english_copy_and_format_in_all_languages() {
        var loc = global::ConditioningControlPanel.Localization.LocalizationManager.Instance; string language = loc.CurrentLanguage;
        string[] keys = { "chaos_draft_reroll_event", "chaos_draft_sin_bonus_event",
            "chaos_draft_shielded_sin_event", "chaos_draft_surrender_event", "chaos_draft_skip_event", "chaos_draft_skip_announce",
            "chaos_treat_faded_label", "chaos_treat_faded", "chaos_treat_faded_streak" };
        try {
            loc.SetLanguage("en"); var english = keys.Select(loc.Get).ToArray();
            Assert.Equal(new[] { "🎲 tempted fate again", "🕯 sin embraced (+{0:0.00}x)",
                "🕯 the candle took the sting (no drawback)", "🕯 you said yes. it gave back (+1 resistance)",
                "♥ resisted → +1 resistance", "+1 RESISTANCE", "FADED", "💨 {0} faded away", "💨 {0} faded… streak halved to x{1}" }, english);
            foreach (var (code, _, _) in global::ConditioningControlPanel.Localization.LocalizationManager.AvailableLanguages) {
                loc.SetLanguage(code);
                for (int i = 0; i < keys.Length; i++) {
                    var text = loc.Get(keys[i]); Assert.NotEqual(keys[i], text);
                    if (code != "en") Assert.NotEqual(english[i], text); // no English fallback
                }
                Assert.Contains(0.25.ToString("0.00"), loc.GetF(keys[1], 0.25)); Assert.DoesNotContain("{0", loc.GetF(keys[1], 0.25));
                Assert.Contains("test", loc.GetF(keys[7], "test")); Assert.DoesNotContain("{0", loc.GetF(keys[7], "test"));
                Assert.Contains("test", loc.GetF(keys[8], "test", 4)); Assert.Contains("4", loc.GetF(keys[8], "test", 4)); Assert.DoesNotContain("{1", loc.GetF(keys[8], "test", 4));
            }
        }
        finally { loc.SetLanguage(language); }
    }
    // WPF ChaosModeService.cs:1441-1483, 1879-1888: incomplete rabbit effects cannot be offered.
    [Theory, InlineData("the_urge"), InlineData("playing_fire"), InlineData("bright_colors")] public async Task Draft_and_reroll_offer_only_implemented_cards(string deferredSin) { await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var all = ChaosBoonPool.All.ToArray();
            try {
                ChaosBoonPool.All.RemoveAll(b => b.Id is not ("golden_touch" or "extra_shield" or "gg_rabbits") && b.Id != deferredSin);
                Assert.Contains(ChaosBoonPool.Draft(false), b => b.Id == "gg_rabbits"); // WPF retains the full pool
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true, AllowCurses = true, SinChance = 1 });
                ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); ChaosRunHost.State!.RerollsLeft = 1;
                Ticks(80); var overlay = ChaosRunHost.Overlay; var cards = overlay.FindControl<StackPanel>("BoonCardHost")!;
                void CheckCards() {
                    Assert.Equal(2, cards.Children.Count);
                    Assert.All(cards.Children, card => Assert.Contains(((TextBlock)((StackPanel)((Border)card).Child!).Children[2]).Text,
                        all.Where(b => b.Id is "golden_touch" or "extra_shield").Select(b => b.Desc)));
                }
                CheckCards(); overlay.FindControl<Button>("BtnReroll")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); CheckCards(); var state = ChaosRunHost.State; int shields = state.Shields;
                state.ApplyBoon(all.First(b => b.Id == "extra_shield")); Assert.Equal(shields + 2, state.Shields); var variant = ChaosBubbleVariants.All.First(v => v.IsLive) with { FuseMinMs = 4000, FuseMaxMs = 4000 };
                state.ApplyBoon(all.First(b => b.Id == "hair_trigger")); Assert.Equal(3000, ChaosBubbleVariants.Build(variant, 0, state.FuseTimeMult).FuseMs);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosBoonPool.All.Clear(); ChaosBoonPool.All.AddRange(all); }
            return Task.CompletedTask;
        }); }
    // WPF ChaosModeService.cs:1476-1479: four choices and a sin after earlier unique picks.
    [Fact] public async Task Successive_four_card_drafts_keep_a_sin_and_the_full_boon_count() {
        await AvaloniaTestDispatcher.RunAsync(async () => {
            Setup();
            try {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true,
                    AllowCurses = true, DraftChoices = 4, SinChance = 0, DraftAutoResumeSec = 0 });
                var overlay = ChaosRunHost.Overlay!; overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); var state = ChaosRunHost.State!; state.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == "welcome_shower"));
                state.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == "hair_trigger")); state.MaxedBoons.Add("surrender"); state.SurrenderShieldUsed = true; state.RerollsLeft = 1;
                Ticks(80); var cards = overlay.FindControl<StackPanel>("BoonCardHost")!; Assert.Equal(4, cards.Children.Count);
                string Description(int i) => ((TextBlock)((StackPanel)((Border)cards.Children[i]).Child!).Children[2]).Text!; var sins = ChaosBoonPool.All.Where(b => b.IsCurse && b.Id != "hair_trigger").ToArray();
                Assert.Contains(sins, b => b.Desc == Description(3)); await Task.Delay(1500); var button = ((StackPanel)((Border)cards.Children[3]).Child!).Children.OfType<Button>().Single();
                Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var deadline = DateTime.UtcNow.AddSeconds(6); var panel = overlay.FindControl<Border>("DraftPanel")!;
                while (panel.IsVisible && DateTime.UtcNow < deadline) await Task.Delay(50);
                Assert.False(panel.IsVisible); overlay.FinishCountdown();
                while (ChaosUnlockCardOverlay.IsShowing) ChaosUnlockCardOverlay.Active!.Close();
                Ticks(80); Assert.True(panel.IsVisible); Assert.Equal(4, cards.Children.Count); Assert.Contains(sins, b => b.Desc == Description(3) && state.ActiveCurses.All(c => c.Id != b.Id));
                state.Config.AllowCurses = false; overlay.FindControl<Button>("BtnReroll")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(4, cards.Children.Count);
                Assert.Contains(Enumerable.Range(0, 4), i => Description(i) == ChaosBoonPool.All.First(b => b.Id == "heavy_drop").Desc);
            }
            finally { ChaosRunHost.ForceShutdown(); }
        }); }
    // WPF ChaosModeService.cs:1138-1142, 1869-1870, 1898-1913; BubbleService.cs:3252,4223-4232.
    [Fact] public async Task Heavy_drop_spawns_every_tenth_pays_triple_and_expires_after_nine_seconds() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var oldPop = ChaosRunHost.PopText; string? faded = null; ChaosRunHost.PopText = (_, _, text, _) => faded = text;
            try {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false }); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); var state = ChaosRunHost.State!;
                state.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == "heavy_drop")); state.ChanceDoubleOdds = 0;
                for (int i = 0; i < 10; i++) { BubbleOverlay.ClearAll(); ChaosRunHost.SpawnTick(); }
                var heavy = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec != null)); var spec = heavy.Spec!; var variant = ChaosBubbleVariants.All.First(v => v.Id == spec.VariantId);
                Assert.Equal(3, spec.PayMult); Assert.Equal(ChaosBubbleVariants.HEAVY_SPEED_MULT, spec.SpeedMult); Assert.Equal(9000, spec.TreatLifeMs);
                Assert.Equal(variant.MaxSize * ChaosBubbleVariants.GLOBAL_SIZE_SCALE * Math.Max(0.5, state.BubbleScale) * ChaosBubbleVariants.HEAVY_SIZE_MULT, spec.SizePx, 6); state.Score = 0; BubbleOverlay.PopChaos(heavy);
                Assert.Equal(ChaosFieldMath.TreatPoints(spec.Strength, state.BenignBaseline, 3, 1, 1,
                    state.TotalMult, state.BlindfoldPayMult), state.Score, 6);
                for (int i = 0; i < 10; i++) { BubbleOverlay.ClearAll(); ChaosRunHost.SpawnTick(); }
                heavy = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec != null)); state.Combo = 8; double score = state.Score; int fired = state.EffectsFired;
                BubbleOverlay.Field.HoldChaos = true; for (int i = 0; i < 10; i++) BubbleOverlay.Field.Step();
                BubbleOverlay.Field.HoldChaos = false; for (int i = 0; i < 281; i++) BubbleOverlay.Field.Step();
                Assert.False(heavy.Popping); Assert.True(heavy.DangerFactor > 0.99); BubbleOverlay.Field.Step(); Assert.True(heavy.Popping);
                Assert.Equal(4, state.Combo); Assert.Equal(score, state.Score); Assert.Equal(fired, state.EffectsFired);
                Assert.Contains(global::ConditioningControlPanel.Localization.Loc.GetF("chaos_treat_faded_streak", heavy.Spec!.Payload.DisplayName, 4), state.RecentEvents);
                Assert.Equal(global::ConditioningControlPanel.Localization.Loc.Get("chaos_treat_faded_label"), faded);
                double scale = heavy.Scale; BubbleOverlay.Field.Step(); Assert.True(heavy.Scale < scale); Assert.Equal(4, state.Combo);
                for (int i = 0; i < 10; i++) { BubbleOverlay.ClearAll(); ChaosRunHost.SpawnTick(); }
                heavy = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec != null)); state.Combo = 1;
                for (int i = 0; i < 282; i++) BubbleOverlay.Field.Step(); Assert.Equal(0, state.Combo);
                Assert.Contains(global::ConditioningControlPanel.Localization.Loc.GetF("chaos_treat_faded", heavy.Spec!.Payload.DisplayName), state.RecentEvents);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.PopText = oldPop; }
            return Task.CompletedTask;
        }); }
    // WPF ChaosModeService.cs:1441-1483, 1517-1619; ChaosOverlayWindow.xaml.cs:467-555.
    [Fact] public async Task Wave_draft_clears_and_holds_field_rerolls_picks_and_resumes_with_shower() { await AvaloniaTestDispatcher.RunAsync(async () => {
            Setup(); ChaosWaveTimerWindow.SkipPlatformChecksForTests = true; var loc = global::ConditioningControlPanel.Localization.LocalizationManager.Instance; string language = loc.CurrentLanguage; loc.SetLanguage("de");
            try {
                ChaosMeta.State.FirstTimesAwarded?.Remove(ChaosFirstTimes.Whisper); ChaosMeta.State.LessonsComplete.Remove("draft4"); ChaosMeta.State.LessonProgress.Remove("draft4");
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                    BoonDraftEnabled = true, AllowCurses = false, DraftAutoResumeSec = 0 });
                var state = ChaosRunHost.State!; state.ShowWaveTimer = true; ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
                state.ApplyBoon(ChaosBoonPool.All.First(b => b.Id == "welcome_shower")); state.RerollsLeft = 1; ChaosRunHost.SpawnTick();
                Assert.True(BubbleOverlay.ChaosCount > 0); BubbleOverlay.Field.Held = true; Ticks(80); var overlay = ChaosRunHost.Overlay!;
                var panel = overlay.FindControl<Border>("DraftPanel")!; var cards = overlay.FindControl<StackPanel>("BoonCardHost")!; Assert.True(panel.IsVisible); Assert.Equal(1, state.WaveIndex);
                Assert.Equal(0, BubbleOverlay.ChaosCount); Assert.False(BubbleOverlay.Field.Held); var watch = ChaosWaveTimerWindow.Active!;
                Assert.False(watch.Pill.IsVisible); watch.UpdateLayout(); Assert.True(watch.Bounds.Width >= 1 && watch.Bounds.Height >= 1);
                double held = state.ElapsedSec; Ticks(4); ChaosRunHost.SpawnTick(); ChaosRunHost.ToggleManualPause();
                Assert.Equal(held, state.ElapsedSec); Assert.Equal(0, BubbleOverlay.ChaosCount); Assert.False(ChaosRunHost.IsManuallyPaused);

                var oldCard = cards.Children[0]; var reroll = overlay.FindControl<Button>("BtnReroll")!; Assert.True(reroll.IsVisible); reroll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, state.RerollsLeft); Assert.Contains("🎲 das Schicksal erneut herausgefordert", state.RecentEvents); Assert.False(reroll.IsVisible); Assert.NotSame(oldCard, cards.Children[0]);
                var first = (Border)cards.Children[0]; var pick = ((StackPanel)first.Child!).Children.OfType<Button>().Single();
                Assert.True(pick.IsEnabled); pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Single(state.RunPickTiles);
                var deadline = DateTime.UtcNow.AddSeconds(6); while (panel.IsVisible && DateTime.UtcNow < deadline) await Task.Delay(50); Assert.False(panel.IsVisible); Assert.Equal(2, state.RunPickTiles.Count);
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper)); Assert.Equal(1, ChaosLessons.Progress("draft4")); Assert.Contains("boon:" + state.RunPickTiles[1].Id, ChaosMeta.State.DiscoveredCodexIds);
                Assert.Equal(2, ChaosRunHost.BoonBar!.Row.Children.Count); Assert.Equal(2, state.WaveIndex); Assert.Equal(held, state.ElapsedSec); Assert.Equal(0, BubbleOverlay.ChaosCount); overlay.FinishCountdown();
                Assert.Equal(6, BubbleOverlay.ChaosCount); Ticks(1); Assert.True(state.ElapsedSec > held);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosWaveTimerWindow.SkipPlatformChecksForTests = false; loc.SetLanguage(language); }
        }); }
    // WPF ChaosModeService.cs:1447-1456, 1577-1593: disabled drafts roll on; skip grants resistance.
    [Fact] public async Task Disabled_drafts_roll_on_and_skip_adds_one_resistance() { await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var meta = ChaosMeta.State; int runs = meta.RunsCompleted; var loc = global::ConditioningControlPanel.Localization.LocalizationManager.Instance; string language = loc.CurrentLanguage;
            loc.SetLanguage("de"); var settings = global::ConditioningControlPanel.CoreSettings.Current; bool announcer = settings.ChaosAnnouncerEnabled;
            try {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false }); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); Ticks(80);
                Assert.Equal(2, ChaosRunHost.State!.WaveIndex); Assert.False(ChaosRunHost.Overlay.FindControl<Border>("DraftPanel")!.IsVisible); ChaosRunHost.ForceShutdown();

                meta.RunsCompleted = 2; ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true });
                ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); Ticks(80); var state = ChaosRunHost.State!;
                int shields = state.Shields; var skip = ChaosRunHost.Overlay.FindControl<Button>("BtnSkipBoon")!; Assert.True(skip.IsVisible); ChaosAnnouncerOverlay.CloseActive(); settings.ChaosAnnouncerEnabled = true;
                skip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(shields + 1, state.Shields); Assert.Contains("♥ widerstanden → +1 Widerstand", state.RecentEvents);
                Assert.Equal("+1 WIDERSTAND", ChaosAnnouncerOverlay.Active!.Current!.Text); Assert.Empty(state.RunPickTiles); Assert.Equal(2, state.WaveIndex); double held = state.ElapsedSec; Ticks(4);
                Assert.Equal(held, state.ElapsedSec); ChaosRunHost.Overlay.FinishCountdown(); Ticks(1); Assert.True(state.ElapsedSec > held);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosAnnouncerOverlay.CloseActive(); settings.ChaosAnnouncerEnabled = announcer; meta.RunsCompleted = runs; loc.SetLanguage(language); }
            return Task.CompletedTask;
        }); }
    // WPF ChaosModeService.cs:1586-1619, 190-205: a lesson defers the shower until dismissal.
    [Fact] public async Task Lesson_earned_at_the_table_holds_the_ready_handoff() { await AvaloniaTestDispatcher.RunAsync(() => {
            Setup(); var meta = ChaosMeta.State; int runs = meta.RunsCompleted;
            try {
                meta.RunsCompleted = 2; meta.LessonsComplete.Remove("draft4"); meta.LessonProgress.Remove("draft4"); meta.LessonsComplete.Remove("intrusive_thoughts"); meta.LessonProgress.Remove("intrusive_thoughts");
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true }); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); var state = ChaosRunHost.State!;
                state.WelcomeShowerEnabled = true; Ticks(80); ChaosLessons.Tick("draft4", ChaosLessons.T_DRAFT4); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.False(ChaosUnlockCardOverlay.IsShowing);
                ChaosRunHost.Overlay.FindControl<Button>("BtnSkipBoon")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); ChaosRunHost.Overlay.FinishCountdown();
                Assert.True(ChaosUnlockCardOverlay.IsShowing); Assert.False(ChaosUnlockCardOverlay.Active!.AutoDismiss);
                int queued = ChaosUnlockCardOverlay.QueuedCount; ChaosLessons.Tick("intrusive_thoughts", ChaosLessons.ById("intrusive_thoughts")!.Target); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(queued + 1, ChaosUnlockCardOverlay.QueuedCount); Assert.Equal(0, BubbleOverlay.ChaosCount); double held = state.ElapsedSec;
                Ticks(1); Assert.Equal(held, state.ElapsedSec); ChaosUnlockCardOverlay.Active.Close(); Assert.Equal(6, BubbleOverlay.ChaosCount); Ticks(1); Assert.True(state.ElapsedSec > held);
            }
            finally { ChaosRunHost.ForceShutdown(); meta.RunsCompleted = runs; }
            return Task.CompletedTask;
        }); }
    // WPF ChaosModeService.cs:146-178: a posted completion after stop shows without claiming a pause.
    [Fact] public async Task Lesson_posted_during_draft_then_stopped_shows_on_the_recap() { await AvaloniaTestDispatcher.RunAsync(() => {
            Setup();
            try {
                ChaosMeta.State.LessonsComplete.Add("silk_touch"); ChaosMeta.State.LessonsComplete.Remove("draft4"); ChaosMeta.State.LessonProgress.Remove("draft4");
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true }); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); Ticks(80);
                Assert.True(ChaosRunHost.Overlay.FindControl<Border>("DraftPanel")!.IsVisible);
                ChaosLessons.Tick("draft4", ChaosLessons.T_DRAFT4); // posts to the UI thread
                ChaosRunHost.RequestStop(); Assert.False(ChaosUnlockCardOverlay.IsShowing); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(ChaosRunHost.Overlay.FindControl<Border>("ResultsPanel")!.IsVisible); Assert.True(ChaosUnlockCardOverlay.IsShowing); Assert.True(ChaosUnlockCardOverlay.Active!.AutoDismiss);
                Assert.Equal(ChaosUnlockCardRules.ForLesson("draft4")!.Title, ChaosUnlockCardOverlay.Active.Current!.Title);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        }); }
    // WPF ChaosModeService.cs:3161-3181: shutdown closes the table and cannot commit a late pick.
    [Fact] public async Task Panic_during_the_pick_beat_discards_the_pending_choice() { await AvaloniaTestDispatcher.RunAsync(async () => {
            Setup();
            try {
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = true });
                ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); var state = ChaosRunHost.State!; Ticks(80); var first = (Border)ChaosRunHost.Overlay.FindControl<StackPanel>("BoonCardHost")!.Children[0];
                ((StackPanel)first.Child!).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var confirm = (global::Avalonia.Threading.DispatcherTimer)typeof(ChaosOverlayWindow)
                    .GetField("_confirmTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .GetValue(ChaosRunHost.Overlay)!;
                Assert.True(confirm.IsEnabled); ChaosRunHost.ForceShutdown(); Assert.False(confirm.IsEnabled); await Task.Delay(1800);
                Assert.Empty(state.RunPickTiles); Assert.Null(ChaosRunHost.BoonBar); Assert.False(ChaosRunHost.IsActive);
            }
            finally { ChaosRunHost.ForceShutdown(); }
        }); }

    // WPF ChaosModeService.cs:1476-1479, 1534-1566: Surrender guarantees a sin,
    // shields its first drawback, adds its multiplier bonus and returns one resistance.
    [Fact] public async Task Surrender_guarantees_and_shields_the_first_sin() { await AvaloniaTestDispatcher.RunAsync(async () => {
            Setup(); var loc = global::ConditioningControlPanel.Localization.LocalizationManager.Instance; string language = loc.CurrentLanguage; loc.SetLanguage("de");
            try {
                ChaosMeta.State.FirstTimesAwarded?.Remove(ChaosFirstTimes.Yes); ChaosMeta.State.LessonsComplete.Remove("surrender"); ChaosMeta.State.LessonProgress.Remove("surrender");
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3,
                    BoonDraftEnabled = true, AllowCurses = true, DraftChoices = 2, SinChance = 0 });
                ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests(); var state = ChaosRunHost.State!;
                state.MaxedBoons.Add("surrender"); state.SinExtraMult = 0.25; double multiplier = state.BoonMult; int shields = state.Shields; Ticks(80);
                var cards = ChaosRunHost.Overlay.FindControl<StackPanel>("BoonCardHost")!; Assert.Equal(2, cards.Children.Count); await Task.Delay(600);
                var sin = (Border)cards.Children[1]; var pick = ((StackPanel)sin.Child!).Children.OfType<Button>().Single(); Assert.True(pick.IsEnabled);
                pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var deadline = DateTime.UtcNow.AddSeconds(6);
                while (state.ActiveCurses.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(50); var boon = Assert.Single(state.ActiveCurses);
                Assert.True(state.SurrenderShieldUsed); Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes)); Assert.Equal(1, ChaosLessons.Progress("surrender")); Assert.Equal(shields + 1, state.Shields);
                Assert.Equal(multiplier + boon.RunMultBonus + 0.25, state.BoonMult, 6); Assert.Contains($"🕯 Sünde angenommen (+{state.SinExtraMult:0.00}x)", state.RecentEvents);
                Assert.Contains("🕯 die Kerze nahm den Schmerz (kein Nachteil)", state.RecentEvents); Assert.Contains("🕯 du hast ja gesagt. sie gab etwas zurück (+1 Widerstand)", state.RecentEvents);
                Assert.Equal(boon.Id, Assert.Single(state.RunPickTiles).Id); Assert.Contains("boon:" + boon.Id, ChaosMeta.State.DiscoveredCodexIds);
            }
            finally { ChaosRunHost.ForceShutdown(); loc.SetLanguage(language); }
        }); }

    // WPF ChaosSfx.cs:20-22, 61-73 and ModResourceResolver.cs:374-377.
    [Theory]
    [InlineData("mp3")]
    [InlineData("wav")]
    public void Wave_clear_cue_prefers_mod_audio_and_falls_back_to_bundled_sound(string extension) {
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-wave-cue-");
        var oldOverride = global::ConditioningControlPanel.CoreModArt.AudioOverridePathProvider;
        var oldAudio = global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider;
        var paths = new System.Collections.Generic.List<string>();
        try {
            var folder = System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir.FullName, "resources", "sounds", "chaos"));
            var file = System.IO.Path.Combine(folder.FullName, "wave_clear." + extension); System.IO.File.WriteAllBytes(file, new byte[1]);
            global::ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = name =>
                global::ConditioningControlPanel.CoreModArt.ModAudioFile(name, dir.FullName);
            global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (path, _, tag, _, _) =>
            { if (tag == "chaos-sfx") paths.Add(path); }; ChaosOverlayWindow.ChaosSfx.PlayWaveClear(); Assert.Equal(file, Assert.Single(paths));
            System.IO.File.Delete(file); ChaosOverlayWindow.ChaosSfx.PlayWaveClear(); Assert.Equal(2, paths.Count); Assert.EndsWith("lvup.mp3", paths[1]);
        }
        finally {
            global::ConditioningControlPanel.CoreModArt.AudioOverridePathProvider = oldOverride; global::ConditioningControlPanel.CoreAudio.PlayOneShotProvider = oldAudio; dir.Delete(true);
        }
    }
}
