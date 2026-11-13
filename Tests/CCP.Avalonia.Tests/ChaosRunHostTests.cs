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
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Localization;
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

    [Theory]
    [InlineData("en", "frozen. the field holds", "Freeze")]
    [InlineData("de", "eingefroren. das Feld hält still", "Einfrieren")]
    [InlineData("es", "congelado. el campo se detiene", "Congelar")]
    [InlineData("fr", "gelé. le champ reste immobile", "Gel")]
    [InlineData("it", "congelato. il campo si ferma", "Gelo")]
    [InlineData("ja", "凍結。フィールドが止まる", "凍結")]
    [InlineData("ko", "얼었다. 필드가 멈춘다", "동결")]
    [InlineData("pt-BR", "congelado. o campo para", "Congelar")]
    [InlineData("ru", "замерло. поле застыло", "Заморозка")]
    [InlineData("zh-CN", "冻结。场域静止", "冻结")]
    public async Task Freeze_pickup_uses_the_selected_language(string language, string eventText, string bannerText)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var localization = ConditioningControlPanel.Localization.LocalizationManager.Instance;
            var previousLanguage = localization.CurrentLanguage;
            var previousSkip = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
            try
            {
                localization.SetLanguage(language);
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                BubbleOverlay.PopChaos(BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsFreeze = true, SizePx = 100 }));
                Assert.Contains(eventText, ChaosRunHost.State!.RecentEvents);
                Assert.Equal(bannerText, Assert.Single(ChaosEffectBannerOverlay.Active!.Strip.Row, e => e.Id == "freeze").Text);
                Assert.Equal(200, BubbleOverlay.Field.FreezeVibrateRemainingMs);
                Assert.True(BubbleOverlay.Field.FreezeAuraOpacity > 0);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                Assert.Equal(0, BubbleOverlay.Field.FreezeAuraOpacity);
                Assert.Equal(0, BubbleOverlay.Field.FreezeVibrateRemainingMs);
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = previousSkip;
                localization.SetLanguage(previousLanguage);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Freeze_catch_shows_its_effect_label_before_freezing_the_field()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var previous = ChaosRunHost.PopText;
            var labels = new List<(double X, double Y, string Word, global::Avalonia.Media.Color Color, bool Frozen)>();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosRunHost.PopText = (x, y, word, color) => labels.Add((x, y, word, color, BubbleOverlay.Field.FreezeChaos));
                var bubble = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { IsFreeze = true, VariantId = "bambifreeze", SizePx = 100, Tint = 0xFF8AE6FF });
                var x = bubble.CenterX;
                var y = bubble.CenterY;
                BubbleOverlay.Press(bubble, x, y);
                var label = Assert.Single(labels);
                Assert.Equal((x, y, "FREEZE", global::Avalonia.Media.Color.FromUInt32(0xFF8AE6FF), false), label);
                Assert.True(BubbleOverlay.Field.FreezeChaos);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.PopText = previous; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Freeze_pickup_holds_field_allows_free_snap_and_thaws()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var audio = CoreAudio.PlayOneShotProvider;
            var cues = new List<string>();
            CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileNameWithoutExtension(path));
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                var live = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsLive = true, FuseMs = 5000, SizePx = 100 });
                var freeze = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsFreeze = true, SizePx = 100 });
                double score = state.Score;
                double mult = state.TotalMult;
                BubbleOverlay.Press(freeze, freeze.CenterX, freeze.CenterY);
                Assert.True(BubbleOverlay.Field.FreezeChaos);
                Assert.Equal(score + 140 * mult, state.Score, 3);
                Assert.Contains("freeze_catch", cues);
                double y = live.Y, fuse = live.FuseRemainingMs;
                BubbleOverlay.Field.Step();
                Assert.Equal(y, live.Y);
                Assert.Equal(fuse, live.FuseRemainingMs);
                state.Focus = 0;
                BubbleOverlay.Press(live, live.CenterX, live.CenterY);
                Assert.True(live.Channeling);
                state.Focus = 1;
                BubbleOverlay.PopChaos(live);
                Assert.Equal(1, state.Focus);
                Ticks(7);
                var refresh = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsFreeze = true, SizePx = 100 });
                BubbleOverlay.PopChaos(refresh);
                Ticks(7);
                Assert.True(BubbleOverlay.Field.FreezeChaos);
                Ticks(7);
                Assert.False(BubbleOverlay.Field.FreezeChaos);
                Assert.Equal(1, EffectPayload.GlobalDurationMult);
                Assert.Contains("freeze_shatter", cues);
            }
            finally { ChaosRunHost.ForceShutdown(); CoreAudio.PlayOneShotProvider = audio; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Freeze_spawns_are_capped_and_pause_during_freeze()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                var cfg = Cfg();
                cfg.EnabledVariants = new List<string> { "bambifreeze" };
                ChaosRunHost.StartRun(cfg);
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosRunHost.SpawnTick();
                ChaosRunHost.SpawnTick();
                ChaosRunHost.SpawnTick();
                Assert.Equal(2, BubbleOverlay.Field.Bubbles.Count(b => b.Spec?.IsFreeze == true));
                var pickup = BubbleOverlay.Field.Bubbles.First(b => b.Spec?.IsFreeze == true);
                BubbleOverlay.PopChaos(pickup);
                int count = BubbleOverlay.Field.Bubbles.Count;
                ChaosRunHost.SpawnTick();
                Assert.Equal(count, BubbleOverlay.Field.Bubbles.Count);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Live_bubble_hover_lights_focus_and_clears_on_exit_and_teardown()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var bar = ChaosRunHost.Hud!.FindControl<StackPanel>("FocusStripBlock")!;
                var bubble = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { IsLive = true, FuseMs = 5000, SizePx = 200 });
                BubbleOverlay.SetHover(bubble.Screen, bubble.CenterX, bubble.CenterY);
                ChaosRunHost.Tick();
                Assert.NotNull(bar.Effect);
                BubbleOverlay.SetHover(bubble.Screen, bubble.CenterX + 1000, bubble.CenterY);
                ChaosRunHost.Tick();
                Assert.Null(bar.Effect);
                BubbleOverlay.SetHover(bubble.Screen, bubble.CenterX, bubble.CenterY);
                ChaosRunHost.Tick();
                Assert.NotNull(bar.Effect);
                BubbleOverlay.EndChaos();
                ChaosRunHost.Tick();
                Assert.Null(bar.Effect);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Focus_cue_finds_a_live_bubble_under_an_overlapping_treat()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var bar = ChaosRunHost.Hud!.FindControl<StackPanel>("FocusStripBlock")!;
                var live = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { IsLive = true, FuseMs = 5000, SizePx = 200 });
                var treat = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { SizePx = 200 });
                treat.Screen = live.Screen;
                treat.X = live.X;
                treat.Y = live.Y;
                BubbleOverlay.SetHover(live.Screen, live.CenterX, live.CenterY);
                Assert.Same(treat, BubbleOverlay.Field.HitTest(live.Screen, live.CenterX, live.CenterY));
                ChaosRunHost.Tick();
                Assert.NotNull(bar.Effect);
                live.Popping = true;
                ChaosRunHost.Tick();
                Assert.Null(bar.Effect);
            }
            finally { ChaosRunHost.ForceShutdown(); }
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
            var fieldNow = ChaosFieldFxOverlay.NowMs;
            var fieldChecks = ChaosFieldFxOverlay.SkipPlatformChecksForTests;
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

                // WPF ChaosModeService.cs:3236: EndRun closes field FX before results.
                // Freeze the FX clock so natural expiry cannot hide a missing EndRun close.
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.NowMs = () => 0;
                ChaosFieldFxOverlay.Ripple(new Point(100, 100), 430, 550);
                var fieldFx = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
                Assert.True(fieldFx.IsVisible && fieldFx.Clock.IsEnabled);

                Ticks(160);   // to 61 s: past the 60 s run
                Assert.False(ChaosRunHost.IsDescending);
                Assert.Null(ChaosRunHost.Hud);
                Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
                Assert.Null(ChaosFieldFxOverlay.Active); // before ForceShutdown in finally
                Assert.False(fieldFx.IsVisible);
                Assert.False(fieldFx.Clock.IsEnabled);
                Assert.True(fieldFx.Model.IsIdle(0, 0));
                overlay.UpdateLayout();
                Assert.Contains(overlay.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "BEST STREAK");
                Assert.Equal(1, meta.RunsCompleted);   // the run counted (payout: see the FALL IN test)
                Assert.Same(meta, ChaosMeta.State);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosFieldFxOverlay.NowMs = fieldNow;
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = fieldChecks;
                FreshMeta();
            }
            return Task.CompletedTask;
        });
    }

    private static Task WithFreshRun(Action action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); FreshMeta();
        try { action(); }
        finally { ChaosRunHost.ForceShutdown(); FreshMeta(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Wave_draft_holds_clears_rerolls_picks_and_resumes() => WithFreshRun(() =>
    {
        ChaosMeta.State.LessonsComplete.Add("silk_touch");
        var cfg = Cfg(); cfg.BoonDraftEnabled = true; cfg.AllowCurses = false;
        ChaosRunHost.StartRun(cfg); var overlay = ChaosRunHost.Overlay!;
        overlay.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
        var state = ChaosRunHost.State!;
        state.RerollsLeft = 1;
        state.AllLiveNextWave = true;
        ChaosRunHost.SpawnTick();
        Assert.True(BubbleOverlay.ChaosCount > 0);
        Ticks(80);
        Assert.Equal(1, state.WaveIndex);
        Assert.False(state.AllLiveNextWave);
        Assert.Equal(0, BubbleOverlay.ChaosCount);
        double elapsed = state.ElapsedSec;
        Ticks(4); ChaosRunHost.SpawnTick();
        Assert.Equal(elapsed, state.ElapsedSec);
        Assert.Equal(0, BubbleOverlay.ChaosCount);
        overlay.FindControl<Button>("BtnReroll")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, state.RerollsLeft);
        Assert.Null(ChaosRunHost.RerollDraft());
        var cards = (System.Collections.IList)typeof(ChaosOverlayWindow).GetField("_draftCards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
        var card = cards[0]!;
        var boon = (ChaosBoon)card.GetType().GetField("Boon")!.GetValue(card)!;
        typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(overlay, new object?[] { boon });
        Assert.Single(state.ActiveBoons);
        Assert.Equal(2, state.WaveIndex);
        Ticks(4); Assert.Equal(elapsed, state.ElapsedSec);
        overlay.FinishCountdown(); Ticks(4);
        Assert.True(state.ElapsedSec > elapsed);
    });

    [Fact]
    public Task Wave_draft_skip_and_panic_are_safe() => WithFreshRun(() =>
    {
        ChaosMeta.State.LessonsComplete.Add("silk_touch");
        var cfg = Cfg(); cfg.BoonDraftEnabled = true;
        ChaosRunHost.StartRun(cfg);
        var overlay = ChaosRunHost.Overlay!; overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests();
        var state = ChaosRunHost.State!; Ticks(80);
        int shields = state.Shields;
        typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(overlay, new object?[] { null });
        Assert.Equal(shields + 1, state.Shields);
        overlay.FinishCountdown(); Ticks(80);
        Assert.Equal(2, state.WaveIndex);
        ChaosRunHost.ForceShutdown();
        Assert.False(ChaosRunHost.IsActive);
        Assert.Null(ChaosRunHost.State);
        Assert.Equal(0, ChaosMeta.State.RunsCompleted);
        Assert.Equal(0, BubbleOverlay.ChaosCount);
        Assert.Null(ChaosRunHost.RerollDraft());
    });

    [Theory]
    [InlineData(false), InlineData(true)]
    public Task Draft_picks_award_first_times_and_advance_lessons(bool sin) => WithFreshRun(() =>
    {
        var meta = ChaosMeta.State;
        meta.LessonsComplete.Add("silk_touch");
        var cfg = Cfg(); cfg.BoonDraftEnabled = true;
        ChaosRunHost.StartRun(cfg);
        ChaosRunHost.Overlay!.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
        Ticks(80);
        var card = ChaosBoonPool.All.First(b => b.IsCurse == sin);
        ChaosRunHost.ChooseBoon(card);
        Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper));
        Assert.Equal(sin, ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes));
        Assert.Equal(sin ? 25 : 10, meta.Sparks);
        Assert.Equal(1, ChaosLessons.Progress("draft4"));
        Assert.Equal(sin ? 1 : 0, ChaosLessons.Progress("surrender"));
    });

    [Theory]
    [InlineData(true), InlineData(false)]
    public Task First_descent_deals_one_scripted_draft_after_the_threat_or_fallback(bool snapped) => WithFreshRun(() =>
    {
        ChaosMeta.State.LessonsComplete.Add("silk_touch");
        var web = new GameWindow(GameWindow.Games[GameWindow.DtrhId]); web.Show();
        web.HandleMessage("{\"type\":\"boot-error\"}");
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(ChaosRunHost.State!.Config.ScriptedFirstRun);
        var overlay = ChaosRunHost.Overlay!;
        overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); Ticks(216);
        var threat = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.VariantId == "pink"));
        if (snapped) ChaosRunHost.OnDefused(threat);
        Ticks(180);
        if (!snapped) { Assert.False(overlay.FindControl<Border>("DraftPanel")!.IsVisible); Ticks(108); }
        Assert.True(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
        var cards = (System.Collections.IList)typeof(ChaosOverlayWindow).GetField("_draftCards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
        var ids = new[] { "extra_shield", "defuse_chain", "golden_touch", "welcome_shower", "heavy_drop" };
        Assert.Equal(3, cards.Count);
        Assert.All(cards.Cast<object>(), c => Assert.Contains(((ChaosBoon)c.GetType().GetField("Boon")!.GetValue(c)!).Id, ids));
        var state = ChaosRunHost.State!; int wave = state.WaveIndex; double elapsed = state.ElapsedSec;
        Ticks(4); Assert.Equal(elapsed, state.ElapsedSec);
        ChaosRunHost.ChooseBoon(null); overlay.FinishCountdown();
        Assert.Equal(wave, state.WaveIndex); Ticks(4); Assert.True(state.ElapsedSec > elapsed);
        Ticks(60); Assert.False(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
    });

    [Theory]
    [InlineData(true, true), InlineData(false, true), InlineData(false, false)]
    public Task Fourth_descent_rigs_first_sin_and_spends_demo_on_pick_or_skip(bool pick, bool allow) => WithFreshRun(() =>
    {
        var meta = ChaosMeta.State; meta.RunsCompleted = 3; meta.LessonsComplete.Add("silk_touch");
        CoreSettings.Current.ChaosAnnouncerEnabled = true;
        var cfg = Cfg(); cfg.BoonDraftEnabled = true; cfg.AllowCurses = allow; cfg.SinChance = 0;
        ChaosRunHost.StartRun(cfg); var overlay = ChaosRunHost.Overlay!;
        overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); Ticks(80);
        var cards = (System.Collections.IList)typeof(ChaosOverlayWindow).GetField("_draftCards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
        Assert.Equal(allow, cards.Cast<object>().Any(c => ((ChaosBoon)c.GetType().GetField("Boon")!.GetValue(c)!).Id == "double_or_nothing"));
        Assert.False(meta.SeenFirstSin);
        if (allow) { Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("chaos_first_sin_demo"), ChaosAnnouncerOverlay.Active!.Current!.Text); Assert.Equal(3000, ChaosAnnouncerOverlay.Active.Current.HoldMs); }
        bool shielded = false, downside = false;
        var sin = new ChaosBoon("double_or_nothing", "test", "", ChaosRarity.Rare, true, 0, _ => downside = true) { ApplyShielded = _ => shielded = true };
        ChaosRunHost.ChooseBoon(pick ? sin : null);
        Assert.Equal(allow, meta.SeenFirstSin); Assert.Equal(pick && allow, shielded); Assert.False(downside);
        overlay.FinishCountdown(); Ticks(80);
        cards = (System.Collections.IList)typeof(ChaosOverlayWindow).GetField("_draftCards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
        Assert.DoesNotContain(cards.Cast<object>(), c => ((ChaosBoon)c.GetType().GetField("Boon")!.GetValue(c)!).Id == "double_or_nothing");
    });

    [Fact]
    public Task Eligible_loadout_deals_the_duo_demo_once() => WithFreshRun(() =>
    {
        var oldEngine = BarkHead.Engine; var settings = CoreSettings.Current;
        var oldAvatar = settings.AvatarEnabled; var oldVolume = settings.MasterVolume;
        int spoken = 0;
        var rule = new BarkRule { Id = "duo", Trigger = "ChaosDuoDemo", VariantPool = new() { new BarkVariant("duo") } };
        var engine = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = _ => spoken++ };
        engine.Start(); BarkHead.Engine = engine; settings.AvatarEnabled = true; settings.MasterVolume = 50;
        try
        {
            var meta = ChaosMeta.State; meta.LessonsComplete.Add("silk_touch");
            foreach (var id in new[] { "the_spanker", "e_stim" }) { meta.LifetimeBoonLevels[id] = 1; meta.ActiveLifetimeBoons.Add(id); }
            CoreSettings.Current.ChaosAnnouncerEnabled = true;
            var cfg = Cfg(); cfg.BoonDraftEnabled = true; cfg.AllowCurses = false;
            ChaosRunHost.StartRun(cfg); var overlay = ChaosRunHost.Overlay!;
            overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); Ticks(80);
            var cards = (System.Collections.IList)typeof(ChaosOverlayWindow).GetField("_draftCards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overlay)!;
            Assert.Contains(cards.Cast<object>(), c => ((ChaosBoon)c.GetType().GetField("Boon")!.GetValue(c)!).Id == "electrified_rabbits");
            Assert.True(meta.SeenDuoDemo); Assert.Equal(1, spoken);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("chaos_duo_demo"), ChaosAnnouncerOverlay.Active!.Current!.Text);
            Assert.Equal(3000, ChaosAnnouncerOverlay.Active.Current.HoldMs);
            ChaosAnnouncerOverlay.CloseActive();
            engine = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = _ => spoken++ }; engine.Start(); BarkHead.Engine = engine;
            ChaosRunHost.ChooseBoon(null); overlay.FinishCountdown(); Ticks(80);
            Assert.True(meta.SeenDuoDemo); Assert.Equal(1, spoken); Assert.NotEqual(ConditioningControlPanel.Localization.Loc.Get("chaos_duo_demo"), ChaosAnnouncerOverlay.Active?.Current?.Text);   // the skip announces +1 resistance (#4776)
            ChaosRunHost.ForceShutdown(); ChaosRunHost.StartRun(cfg); overlay = ChaosRunHost.Overlay!;
            overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); Ticks(80);
            Assert.Equal(1, spoken); Assert.Null(ChaosAnnouncerOverlay.Active);
        }
        finally { BarkHead.Engine = oldEngine; settings.AvatarEnabled = oldAvatar; settings.MasterVolume = oldVolume; }
    });

    [Fact]
    public Task Draft_events_use_the_active_language() => WithFreshRun(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        string language = loc.CurrentLanguage;
        try
        {
            foreach (var (code, _, _) in ConditioningControlPanel.Localization.LocalizationManager.AvailableLanguages)
            {
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
                var strings = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
                foreach (var key in new[] { "chaos_draft_reroll", "chaos_welcome_shower", "chaos_first_sin_demo", "chaos_duo_demo",
                    "chaos_event_skip", "chaos_event_sin_bonus", "chaos_event_sin_shielded", "chaos_event_sin_resistance", "chaos_announce_resist" }) Assert.False(string.IsNullOrWhiteSpace((string?)strings[key]));
            }
            loc.SetLanguage("de"); ChaosMeta.State.LessonsComplete.Add("silk_touch");
            var cfg = Cfg(); cfg.BoonDraftEnabled = true;
            ChaosRunHost.StartRun(cfg); var overlay = ChaosRunHost.Overlay!;
            overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            var state = ChaosRunHost.State!; state.RerollsLeft = 1; Ticks(80);
            ChaosRunHost.RerollDraft(); Assert.Contains(loc.Get("chaos_draft_reroll"), state.RecentEvents);
            state.WelcomeShowerEnabled = true; ChaosRunHost.ChooseBoon(null); overlay.FinishCountdown();
            Assert.Contains(loc.Get("chaos_welcome_shower"), state.RecentEvents);
        }
        finally { loc.SetLanguage(language); }
    });

    // Hand-ported from #4776: WPF OnBoonChosen's feedback (ChaosModeService.cs:1546-1584).
    [Fact]
    public Task Draft_choices_report_resistance_and_sins() => WithFreshRun(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        ChaosMeta.State.LessonsComplete.Add("silk_touch");
        CoreSettings.Current.ChaosAnnouncerEnabled = true;
        var cfg = Cfg(); cfg.BoonDraftEnabled = true;
        ChaosRunHost.StartRun(cfg); var overlay = ChaosRunHost.Overlay!;
        overlay.FinishCountdown(); ChaosRunHost.StopTimerForTests(); Ticks(80);
        var state = ChaosRunHost.State!;
        ChaosRunHost.ChooseBoon(null);
        Assert.Contains(loc.Get("chaos_event_skip"), state.RecentEvents);
        Assert.Equal(loc.Get("chaos_announce_resist"), ChaosAnnouncerOverlay.Active!.Current!.Text);
        overlay.FinishCountdown(); ChaosAnnouncerOverlay.CloseActive();
        state.SinExtraMult = 0.25; state.MaxedBoons.Add("surrender");
        Ticks(80);
        var sin = ChaosBoonPool.All.First(b => b.IsCurse);
        ChaosRunHost.ChooseBoon(sin);
        Assert.Contains(loc.GetF("chaos_event_sin_bonus", 0.25), state.RecentEvents);
        Assert.Contains(loc.Get("chaos_event_sin_shielded"), state.RecentEvents);
        Assert.Contains(loc.Get("chaos_event_sin_resistance"), state.RecentEvents);
        Assert.EndsWith(sin.Name, ChaosAnnouncerOverlay.Active!.Current!.Text);
    });

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
            var oldPointer = ChaosRunHost.ReadToyPointer;
            ChaosRunHost.ReadToyPointer = () => null;
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
                ChaosRunHost.ReadToyPointer = oldPointer;
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

    /// <summary>The countdown skip and the panic key ride separate hooks in no fixed order: a skip that
    /// lands while the panic is still queued must never start the run (no boons, HUD clock or toys).</summary>
    [Fact]
    public async Task A_skip_landing_before_a_queued_panic_never_starts_the_run()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var s = CoreSettings.Current;
            (bool pe, string pk) = (s.PanicKeyEnabled, s.PanicKey);
            var oldTeardown = ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown;
            ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown = () => { };
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                var overlay = ChaosRunHost.Overlay!;
                // Win32Input.OnPanicPress: the hook thread queues the panic...
                ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.QueueWatched(
                    () => shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0)));
                overlay.FinishCountdown();   // ...and the skip runs first
                Assert.True(ChaosRunHost.IsActive);
                Assert.False(ChaosRunHost.IsDescending);
                Assert.Null(ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKeys);   // StartToys never ran
                Assert.Null(ChaosRunHost.BoonBar);

                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();   // the panic, then the deferred BeginRun
                Assert.False(ChaosRunHost.IsActive || ChaosRunHost.IsDescending);
                Assert.Null(ConditioningControlPanel.Avalonia.Platform.X11PanicKey.ToyKeys);
                Assert.Null(ChaosRunHost.BoonBar);
                Assert.False(ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.PanicPending);

                // A queued press that stops nothing (a lock card took it) only delays the start.
                ChaosRunHost.StartRun(Cfg());
                ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.QueueWatched(() => { });
                ChaosRunHost.Overlay!.FinishCountdown();
                Assert.False(ChaosRunHost.IsDescending);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(ChaosRunHost.IsDescending);
                ChaosRunHost.StopTimerForTests();
            }
            finally
            {
                ChaosRunHost.ForceShutdown(); shell.Close();
                ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown = oldTeardown;
                (s.PanicKeyEnabled, s.PanicKey) = (pe, pk);
            }
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
                var settings = new SettingsService();
                CoreSettings.ServiceProvider = () => settings;
                settings.Current.ChaosBoonDraftEnabled = false;
                hub = new ChaosHubWindow();
                hub.FindControl<Button>("BtnMenuFallIn")!
                    .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.True(ChaosRunHost.IsActive);
                Assert.Equal(ChaosPlayMode.Story, ChaosRunHost.State!.Config.PlayMode);
                Assert.False(ChaosRunHost.State.Config.ScriptedFirstRun);
                Assert.False(ChaosRunHost.State.Config.BoonDraftEnabled);

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

    private sealed class ChaperoneMissDice : Random
    {
        private bool _rolled;
        public override double NextDouble() { if (_rolled) return 0; _rolled = true; return 0.99; }
        public override int Next(int minValue, int maxValue) => minValue;
    }
    private sealed class TeaseDice : Random
    {
        public override double NextDouble() => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }
    // Starts a German, announced, TeaseDice run; every hook set here is restored after the body.
    private static Task TeaseRun(Action<ChaosRunState, List<string>> body, bool fx = false) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); FreshMeta();
        var (rng, popText, language, announcer, bark, firer) = (ChaosRunHost.Rng, ChaosRunHost.PopText,
            LocalizationManager.Instance.CurrentLanguage, CoreSettings.Current.ChaosAnnouncerEnabled, ChaosRunHost.TeaseBark, EffectPayload.Firer);
        var (fxOn, composited, skipChecks) = (CoreSettings.Current.ChaosSkiaFxEnabled, ChaosSkiaFxOverlay.Composited, ChaosSkiaFxOverlay.SkipPlatformChecksForTests);
        try
        {
            var barks = new List<string>(); ChaosRunHost.TeaseBark = (t, n) => barks.Add($"{t}:{n}");
            ChaosRunHost.Rng = new TeaseDice(); if (fx) (CoreSettings.Current.ChaosSkiaFxEnabled, ChaosSkiaFxOverlay.Composited, ChaosSkiaFxOverlay.SkipPlatformChecksForTests) = (true, () => true, true);
            ChaosRunHost.StartRun(Cfg()); ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
            LocalizationManager.Instance.SetLanguage("de"); CoreSettings.Current.ChaosAnnouncerEnabled = true; ChaosAnnouncerOverlay.CloseActive();
            body(ChaosRunHost.State!, barks);
        }
        finally
        {
            ChaosRunHost.ForceShutdown(); (ChaosRunHost.Rng, ChaosRunHost.PopText, ChaosRunHost.TeaseBark, EffectPayload.Firer) = (rng, popText, bark, firer);
            CoreSettings.Current.ChaosAnnouncerEnabled = announcer; LocalizationManager.Instance.SetLanguage(language);
            (CoreSettings.Current.ChaosSkiaFxEnabled, ChaosSkiaFxOverlay.Composited, ChaosSkiaFxOverlay.SkipPlatformChecksForTests) = (fxOn, composited, skipChecks);
            FreshMeta();
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task Tease_spawn_respects_rank_and_scripted_run_and_replaces_a_slot() => TeaseRun((state, barks) =>
    {
        var cfg = state.Config; ChaosMeta.State.RunsCompleted = 0;
        Assert.False(ChaosRunHost.TrySpawnTease(cfg, 0.5));
        ChaosMeta.State.RunsCompleted = 10; cfg.ScriptedFirstRun = true;
        Assert.False(ChaosRunHost.TrySpawnTease(cfg, 0.5));
        // WPF rolls the Chaperone first (ChaosModeService.cs:1264): miss it, then the Tease roll hits.
        cfg.ScriptedFirstRun = false; ChaosRunHost.Rng = new ChaperoneMissDice(); ChaosRunHost.SpawnTick();
        Assert.Single(BubbleOverlay.Field.Bubbles, b => b.Spec?.IsTease == true);
        Assert.True(ChaosMeta.State.SeenTease); Assert.Equal("Tease: nicht berühren", ChaosAnnouncerOverlay.Active!.Current!.Text);
        Assert.Equal(new[] { "ChaosTeaseDebut:" }, barks);
    });
    [Fact]
    public Task Tease_press_bursts_once_shield_blocks_payload_and_combo_halves() => TeaseRun((state, barks) =>
    {
        int fired = 0; EffectPayload.Firer = _ => fired++;
        var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsTease = true, SizePx = 200, Payload = new FlashPayload() });
        state.Combo = 9; state.Shields = 1; state.Heat = 0.5;
        BubbleOverlay.PopChaos(b); Assert.False(b.Popping);
        BubbleOverlay.HoldChaos(true); BubbleOverlay.Press(b, b.CenterX, b.CenterY); Assert.False(b.Popping);
        BubbleOverlay.HoldChaos(false); BubbleOverlay.Press(b, b.CenterX, b.CenterY);
        var fx = ChaosSkiaFxOverlay.Active!; Assert.True(fx.Fx.ParticleCount > 0);
        Assert.Equal((1, 0, 4, 0), (state.Detonated, state.Shields, state.Combo, fired)); Assert.Equal(0.3, state.Heat, 5);
        int particles = fx.Fx.ParticleCount; BubbleOverlay.Press(b, b.CenterX, b.CenterY);
        Assert.Equal(particles, fx.Fx.ParticleCount);
        var c = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsTease = true, SizePx = 200, Payload = new FlashPayload() });
        state.Combo = 3; CoreSettings.Current.ChaosSkiaFxEnabled = false;
        BubbleOverlay.Press(c, c.CenterX, c.CenterY);
        Assert.Equal(particles, fx.Fx.ParticleCount);
        Assert.Equal((2, 1, 1, 1), (state.Detonated, state.Combo, state.EffectsFired, fired));
        Assert.Equal(new[] { "ChaosTeaseClicked:", "ChaosTeaseClicked:" }, barks);
    }, fx: true);
    [Theory]
    [InlineData(true), InlineData(false)]
    public Task Tease_expires_once_and_rewards_restraint(bool hint) => TeaseRun((state, barks) =>
    {
        state.Focus = 20; string? label = null;
        ChaosRunHost.PopText = (_, _, text, _) => label = text;
        var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsTease = true, SizePx = 200 });
        double x = b.X, y = b.Y; BubbleOverlay.HoldChaos(true);
        for (int i = 0; i < 200; i++) BubbleOverlay.Field.Step();
        Assert.False(b.Popping); Assert.Equal((x, y), (b.X, b.Y));
        BubbleOverlay.HoldChaos(false); for (int i = 0; i < 234; i++) BubbleOverlay.Field.Step();
        Assert.False(b.Popping);
        Assert.NotEqual((x, y), (b.X, b.Y)); Assert.True(BubbleOverlay.ShowsTeaseHint(b));
        var other = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsTease = true, SizePx = 200 });
        BubbleOverlay.Field.Step();
        Assert.False(BubbleOverlay.ShowsTeaseHint(other));   // learned: retired from live bubbles too
        Assert.True(b.Popping && b.Deflating);
        Assert.Equal((30, "WIDERSTANDEN", 5, 0), (state.Focus, label, ChaosMeta.State.Gold, state.Detonated));
        Assert.True(state.Score > 0);
        if (hint) Assert.Contains("tease", ChaosMeta.State.BubbleHintsLearned);
        else Assert.Equal("Widerstanden. +5 Gold", ChaosAnnouncerOverlay.Active!.Current!.Text);
        double score = state.Score; for (int i = 0; i < 30; i++) BubbleOverlay.Field.Step();
        Assert.Equal((score, 5), (state.Score, ChaosMeta.State.Gold));
        for (int i = 0; i < 5; i++) ChaosRunHost.OnTeaseDenied(b);
        Assert.Equal(new[] { "ChaosTeaseDenied:1", "ChaosTeaseDenied:2", "ChaosTeaseDenied:3", "ChaosTeaseDenied:4",
            "ChaosTeaseDenied:5", "ChaosTeaseDeniedStreak:5", "ChaosTeaseDenied:6" }, barks);
    });

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
    private static Action<double, double, string, global::Avalonia.Media.Color>? _previousPopText;

    private static System.Collections.Generic.List<string> Juice()
    {
        var log = new System.Collections.Generic.List<string>();
        _previousPopText = ChaosRunHost.PopText;
        ChaosRunHost.PopText = (_, y, w, _) => log.Add("label:" + w);
        ChaosRunHost.PulseFx = (c, s) => log.Add(FormattableString.Invariant($"pulse:{c.R},{c.G},{c.B}@{s:0.00}"));   // invariant: the expected strings use a dot on every culture
        ChaosRunHost.ShakeFx = (i, ms) => log.Add($"shake:{i:0.000}/{ms}");
        CoreAudio.PlayOneShotProvider = (p, _, _, _, _) => log.Add("sfx:" + System.IO.Path.GetFileNameWithoutExtension(p));
        return log;
    }

    private static void Unjuice()
    {
        ChaosRunHost.PopText = _previousPopText; ChaosRunHost.PulseFx = null; ChaosRunHost.ShakeFx = null;
        CoreAudio.PlayOneShotProvider = null;
    }

    [Fact]
    public void Juice_restores_the_previous_pop_text_sink()
    {
        var previous = ChaosRunHost.PopText;
        try { Juice(); }
        finally { Unjuice(); }
        Assert.Same(previous, ChaosRunHost.PopText);
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
                Assert.Equal(new[] { "sfx:defuse_hiss", "label:SNAP", "pulse:90,255,150@0.16",
                    "pulse:255,170,80@0.05" }, log.Where(e => !e.StartsWith("label:+")));

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
                Assert.Equal(noFocus.Concat(new[] { "sfx:trigger", FormattableString.Invariant($"pulse:255,50,50@{0.4 + s * 0.35:0.00}"),
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
                Assert.Equal(new[] { "sfx:", "label:SPIRAL", "pulse:255,200,235@0.10", "pulse:255,170,80@0.05" },   // + WPF benign pulse, combo micro pulse
                    log.Where(e => !e.StartsWith("label:+")).Select(e => e.StartsWith("sfx:Pop") ? "sfx:" : e));
            }
            finally { Unjuice(); ChaosRunHost.ForceShutdown(); FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF CheckComboMilestone (:3081-3084): crossing ×25 plays streak_milestone, announces
    /// "STREAK ×25" and fires the big gold pulse once.</summary>
    [Fact]
    public async Task Crossing_a_big_combo_threshold_plays_the_cue_and_announces_the_streak()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var log = Juice();
            var s = CoreSettings.Current;
            bool was = s.ChaosAnnouncerEnabled;
            s.ChaosAnnouncerEnabled = true;
            try
            {
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosAnnouncerOverlay.CloseActive();
                ChaosRunHost.State!.Combo = 24;
                log.Clear();
                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { VariantId = "spiral", SizePx = 200, Payload = new FlashPayload() });
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                Assert.Equal(new[] { "sfx:streak_milestone", "pulse:255,230,120@0.55" },
                    log.SkipWhile(e => e != "sfx:streak_milestone").Take(2));
                Assert.Equal("STREAK ×25", ChaosAnnouncerOverlay.Active?.Current?.Text);
            }
            finally
            {
                ChaosAnnouncerOverlay.CloseActive();
                s.ChaosAnnouncerEnabled = was;
                Unjuice(); ChaosRunHost.ForceShutdown(); FreshMeta();
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF OnDefused (:1984-2052): the first snap banks its first-time bonus with a feed line
    /// (OnFirstTimeAwarded :209), each snap raises this loop's snap_field (ChaosLessonHooks :161) and
    /// feeds Slow Recovery (CountRegenPop :2427); last_breath counts only a held brink snap.
    /// Combo milestones are lane fin2-chaos-fx-window's.</summary>
    [Fact]
    public async Task Snaps_bank_the_first_time_raise_snap_field_and_regrow_resistance()
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
                var state = ChaosRunHost.State!;
                state.ShieldRegenPops = 2;
                state.StartShields = 3; state.Shields = 2;
                long sparks0 = ChaosMeta.State.Sparks;
                void Snap() { log.Clear(); BubbleOverlay.SpawnChaos(LiveSpec()); BubbleOverlay.Field.DefuseAllLive(); }

                Snap();
                Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Snap));
                Assert.Equal(sparks0 + ChaosFirstTimes.Amounts[ChaosFirstTimes.Snap], ChaosMeta.State.Sparks);
                Assert.Contains($"+10 {ChaosGlyphs.Drops} first snap", state.RecentEvents);
                Assert.Equal(1, ChaosLessons.Progress("snap_field"));
                Assert.Equal(2, state.Shields);
                Snap();
                Assert.Equal(sparks0 + 10, ChaosMeta.State.Sparks);   // once
                Assert.Equal(2, ChaosLessons.Progress("snap_field"));
                Assert.Equal(3, state.Shields);   // the second pop regrows one
                Assert.Contains("pulse:120,220,160@0.18", log);
                Assert.Contains("\u2665 resistance regrows", state.RecentEvents);

                // last_breath (ChaosLessonHooks :159): only a HELD snap started with <= 0.8 s of fuse.
                Assert.Equal(0, ChaosLessons.Progress("last_breath"));   // free snaps never count
                state.Focus = 100;
                var t = DateTime.UnixEpoch;
                BubbleOverlay.Field.Clock = () => t = t.AddMilliseconds(32);
                var brink = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { VariantId = "pink", IsLive = true, FuseMs = 700, SizePx = 200, Payload = new FlashPayload() });
                BubbleOverlay.Press(brink, brink.CenterX, brink.CenterY);
                for (int i = 0; i < 40 && !brink.Popping; i++) BubbleOverlay.Field.Step();
                Assert.True(brink.DefusedViaChannel);
                Assert.Equal(1, ChaosLessons.Progress("last_breath"));
            }
            finally { Unjuice(); ChaosRunHost.ForceShutdown(); BubbleOverlay.Field.Clock = () => DateTime.UtcNow; FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService :1768/:1844/:2427: treats share the snap counter,
    // full resistance clears partial credit, and a fresh run starts at zero.
    [Fact]
    public async Task Treats_bank_once_and_regen_discards_full_shield_and_previous_run_credit()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var log = Juice();
            try
            {
                void Start()
                {
                    ChaosRunHost.StartRun(Cfg());
                    ChaosRunHost.Overlay!.FinishCountdown();
                    ChaosRunHost.State!.ShieldRegenPops = 2;
                    ChaosRunHost.State.StartShields = 3;
                    ChaosRunHost.State.Shields = 2;
                }
                void Pop() => BubbleOverlay.PopChaos(BubbleOverlay.SpawnChaos(
                    new EffectBubbleSpec { VariantId = "flash", SizePx = 200, Payload = new FlashPayload() }));
                Start();
                long before = ChaosMeta.State.Sparks;
                Pop();
                Assert.Equal(before + 5, ChaosMeta.State.Sparks);
                Assert.Contains("label:+5 " + ChaosGlyphs.Drops + " first taste", log);
                var state = ChaosRunHost.State!;
                Assert.Equal(2, state.Shields);
                state.Shields = 3;
                Pop();  // full clears partial credit
                state.Shields = 2;
                Pop();
                Assert.Equal(2, state.Shields);
                Pop();
                Assert.Equal(3, state.Shields);
                Assert.Equal(before + 5, ChaosMeta.State.Sparks);
                state.Shields = 2;
                Pop();  // leave one credit behind
                ChaosRunHost.ForceShutdown();
                Start();
                Pop();
                Assert.Equal(2, ChaosRunHost.State!.Shields);
                Pop();
                Assert.Equal(3, ChaosRunHost.State.Shields);
            }
            finally { Unjuice(); ChaosRunHost.ForceShutdown(); FreshMeta(); }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService :2434: preserve English and localize the same regen event.
    [Theory]
    [InlineData("en", "♥ resistance regrows")]
    [InlineData("de", "♥ Widerstand regeneriert sich")]
    [InlineData("es", "♥ la resistencia se regenera")]
    [InlineData("fr", "♥ la résistance se régénère")]
    [InlineData("it", "♥ la resistenza si rigenera")]
    [InlineData("ja", "♥ 抵抗力が回復")]
    [InlineData("ko", "♥ 저항력 회복")]
    [InlineData("pt-BR", "♥ a resistência se regenera")]
    [InlineData("ru", "♥ сопротивление восстанавливается")]
    [InlineData("zh-CN", "♥ 抵抗力恢复")]
    public async Task Regen_feed_uses_the_selected_language(string language, string expected)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var localization = ConditioningControlPanel.Localization.LocalizationManager.Instance;
            string previousLanguage = localization.CurrentLanguage;
            try
            {
                localization.SetLanguage(language);
                Assert.Equal(expected.Substring(2), localization.Get("chaos_resistance_regrows"));
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var state = ChaosRunHost.State!;
                state.ShieldRegenPops = 1;
                state.StartShields = 3;
                state.Shields = 2;
                BubbleOverlay.PopChaos(BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                    { VariantId = "flash", SizePx = 200, Payload = new FlashPayload() }));
                Assert.Equal(3, state.Shields);
                Assert.Contains(expected, state.RecentEvents);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                FreshMeta();
                localization.SetLanguage(previousLanguage);
            }
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
    [Theory]
    [InlineData(1, 1, 0.7, 0.8, "en", "Porn DVD", "📀 now loading")]
    [InlineData(2, 1, 0.85, 0.9, "fr", "DVD porno", "📀 chargement")]
    [InlineData(4, 2, 1.0, 1.0, "ja", "ポルノDVD", "📀 読み込み中")]
    public async Task DvdToyKeyLaunchesRankedLogosAndGlowFollowsTheirLifetime(int level, int count, double speed, double scale, string language, string banner, string loading)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosEffectBannerOverlay.SkipPlatformChecksForTests, CoreSettings.Current.ChaosAccessoryKey1, CoreSettings.Current.PanicKey);
            var previousLanguage = ConditioningControlPanel.Localization.LocalizationManager.Instance.CurrentLanguage;
            var snapshot = ProcessStateSnapshot.Take();
            try
            {
                Setup();
                ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage(language);
                FreshMeta();
                var meta = ChaosMeta.State;
                meta.ToyPockets = 1;
                meta.ActiveLifetimeBoons.Add("porn_dvd");
                meta.LifetimeBoonLevels["porn_dvd"] = level;
                CoreSettings.Current.ChaosAccessoryKey1 = "Q";
                CoreSettings.Current.PanicKey = "F8";
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
                try
                {
                    ChaosRunHost.StartRun(Cfg());
                    ChaosRunHost.Overlay!.FinishCountdown();
                    ChaosRunHost.StopTimerForTests();
                    var toy = Assert.Single(ChaosRunHost.ToyButtons).Toy;
                    ChaosRunHost.ToggleManualPause();
                    ChaosRunHost.OnToyKey("Q");
                    Assert.False(ChaosDvdOverlay.AnyActive);
                    ChaosRunHost.ToggleManualPause();
                    ChaosRunHost.OnToyKey("Q");
                    Assert.Equal(count, ChaosDvdOverlay.Flock.Active.Count);
                    foreach (var logo in ChaosDvdOverlay.Flock.Active)
                    {
                        Assert.Equal(scale, logo.FontScale);
                        Assert.Equal(230 * speed, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6);
                        Assert.Equal(ChaosRunHost.State!.ToyPower["porn_dvd"], logo.RemainingSec);
                    }
                    Assert.True(ChaosEffectBannerOverlay.Active!.Strip.IsLive("dvd"));
                    Assert.Equal(banner, ChaosEffectBannerOverlay.Active.Strip.Row.Single(e => e.Id == "dvd").Text);
                    Assert.Contains(loading, ChaosRunHost.State!.RecentEvents);
                    Assert.Equal(60, toy.CooldownRemainingSec);
                    ChaosRunHost.OnToyKey("Q"); // cooldown must not spawn more
                    Assert.Equal(count, ChaosDvdOverlay.Flock.Active.Count);
                    ChaosRunHost.Tick();
                    Assert.True(toy.IsEffectActive);
                    ChaosDvdOverlay.Advance(30);
                    ChaosDvdOverlay.Advance(0.3);
                    ChaosRunHost.Tick();
                    Assert.False(toy.IsEffectActive);
                    Assert.False(ChaosEffectBannerOverlay.Active!.Strip.IsLive("dvd"));
                    Assert.NotNull(ChaosDvdOverlay.Window);
                    ChaosRunHost.ForceShutdown();
                    Assert.Null(ChaosDvdOverlay.Window);
                }
                finally { ChaosRunHost.ForceShutdown(); FreshMeta(); }
            }
            finally
            {
                snapshot.Restore();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosEffectBannerOverlay.SkipPlatformChecksForTests, CoreSettings.Current.ChaosAccessoryKey1, CoreSettings.Current.PanicKey) = saved;
                ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage(previousLanguage);
            }
            Assert.Equal(saved, (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosEffectBannerOverlay.SkipPlatformChecksForTests, CoreSettings.Current.ChaosAccessoryKey1, CoreSettings.Current.PanicKey));
            Assert.Equal(previousLanguage, ConditioningControlPanel.Localization.LocalizationManager.Instance.CurrentLanguage);
            return Task.CompletedTask;
        });
    }

    /// <summary>WPF ChaosModeService.cs:1844/1873/2025 + AchievementService.cs:570-609: prism, treat and defuse
    /// pops each count toward the lifetime pop total (the 100-pop Sparkle) and credit quests (#3240).</summary>
    [Fact]
    public async Task Chaos_treat_prism_and_defuse_pops_count_toward_lifetime_pops_and_quests()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            FreshMeta();
            var prop = typeof(global::ConditioningControlPanel.Avalonia.App).GetProperty("Achievements",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;
            var (achWas, firerWas) = (prop.GetValue(null), EffectPayload.Firer);
            var dir = System.IO.Directory.CreateTempSubdirectory("ccp-chaos-pops-").FullName;
            var questsWas = global::ConditioningControlPanel.Avalonia.App.Quests;
            using var quests = new QuestService(null, dir);
            quests.Progress.DailyQuests = new List<ConditioningControlPanel.Models.ActiveQuest> { new("pop_parade_d") };
            global::ConditioningControlPanel.Avalonia.App.Quests = quests;
            try
            {
                var engine = new AchievementEngine(new AchievementStore(System.IO.Path.Combine(dir, "a.json")));
                prop.SetValue(null, engine);
                EffectPayload.Firer = null;
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = new EffectBubbleSpec { Payload = new FlashPayload() } });
                Assert.Equal(1, quests.Progress.DailyQuests[0]!.CurrentProgress);
                ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = new EffectBubbleSpec { Payload = new FlashPayload(), IsPrism = true } });
                Assert.Equal(2, quests.Progress.DailyQuests[0]!.CurrentProgress);
                ChaosRunHost.OnDefused(new AmbientBubble { Spec = new EffectBubbleSpec { Payload = new FlashPayload() } });
                Assert.Equal(3, quests.Progress.DailyQuests[0]!.CurrentProgress);
                Assert.Equal(3, engine.Progress.TotalBubblesPopped);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                global::ConditioningControlPanel.Avalonia.App.Quests = questsWas;
                prop.SetValue(null, achWas);
                EffectPayload.Firer = firerWas;
                FreshMeta();
            }
            return Task.CompletedTask;
        });
    }
}
