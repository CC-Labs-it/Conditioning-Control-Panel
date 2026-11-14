using System;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
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
        try { stateProperty.SetValue(null, new ChaosMetaState());
            body(); }
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

    private static Task Run(Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        InFreshMeta(body);
        return Task.CompletedTask;
    });

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Boundary_holds_until_ready_and_resolves_skip_or_sin(bool sin) => Run(() =>
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
            var language = LocalizationManager.Instance.CurrentLanguage;
            try { LocalizationManager.Instance.SetLanguage("fr"); reroll.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("🎲 nouvelle donne", state.RecentEvents); }
            finally { LocalizationManager.Instance.SetLanguage(language); }
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
    [Fact]
    public Task Stopping_at_the_draft_cancels_pick_and_start_boon_waits_for_first_go() => Run(() =>
    {
        var overlay = BeginDraft();
        Assert.True(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
        ChaosRunHost.RequestStop();
        Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
        typeof(ChaosOverlayWindow).GetMethod("ChooseBoon", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(overlay, new object?[] { null });
        Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
        Assert.False(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
        Assert.False(BubbleOverlay.Field.HoldChaos);
        ChaosRunHost.ForceShutdown();
        ChaosMeta.State.EquippedStartBoon = "welcome_shower";
        ChaosRunHost.StartRun(new ChaosRunConfig());
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        ChaosRunHost.Overlay!.FinishCountdown();
        Assert.Equal(6, BubbleOverlay.ChaosCount);
        ChaosRunHost.ForceShutdown();
        ChaosRunHost.StartRun(new ChaosRunConfig { ScriptedFirstRun = true });
        ChaosRunHost.Overlay!.FinishCountdown();
        Assert.Equal(0, BubbleOverlay.ChaosCount);
    });
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 1)]
    public Task Only_picked_cards_advance_their_lessons(int pick, long draft, long surrender) => Run(() =>
    {
        var oldAudio = CoreAudio.PlayOneShotProvider; var oldPulse = ChaosRunHost.PulseFx;
        var oldMod = CoreModArt.AudioOverridePathProvider;
        var modCue = Path.GetTempFileName();   // WPF ChaosSfx.cs:69: the active mod wins
        CoreModArt.AudioOverridePathProvider = rel => rel == "chaos/wave_clear.mp3" ? modCue : null;
        bool oldAnnouncer = CoreSettings.Current.ChaosAnnouncerEnabled;
        var cues = new System.Collections.Generic.List<string>();
        CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(Path.GetFileName(path));
        CoreSettings.Current.ChaosAnnouncerEnabled = true;
        try
        {
            ChaosMeta.State.LessonsComplete.Add("silk_touch");
            var overlay = BeginDraft();
            Assert.Contains(Path.GetFileName(modCue), cues);
            Assert.DoesNotContain("lvup.mp3", cues);
            var boon = pick == 0 ? null : pick == 1 ? ChaosBoonPool.All.Single(b => b.Id == "extra_shield") : ChaosBoonPool.All.First(b => b.IsCurse);
            var state = ChaosRunHost.State!;
            if (pick == 2) { state.MaxedBoons.Add("surrender"); state.SinExtraMult = 0.2; }
            var pulses = new System.Collections.Generic.List<(global::Avalonia.Media.Color, double)>();
            ChaosRunHost.PulseFx = (color, strength) => pulses.Add((color, strength));
            var language = LocalizationManager.Instance.CurrentLanguage;
            try { LocalizationManager.Instance.SetLanguage("fr"); Choose(overlay, boon);
                if (pick == 0) Assert.Contains("♥ refus : +1 résistance", state.RecentEvents);
                if (pick == 2) { Assert.Contains($"🕯 péché accepté (+{0.2:0.00}x)", state.RecentEvents); Assert.Contains("🕯 la bougie a retiré le revers", state.RecentEvents); Assert.Contains("🕯 vous avez dit oui (+1 résistance)", state.RecentEvents); } }
            finally { LocalizationManager.Instance.SetLanguage(language); }
            Assert.Equal(new[] { (global::Avalonia.Media.Color.FromRgb(120, 220, 160), 0.22) }, pulses);
            var announcement = ChaosAnnouncerOverlay.Active!.Current!;
            Assert.Equal(pick == 0 ? "+1 RÉSISTANCE" : (pick == 2 ? "☠ " : "◈ ") + boon!.Name, announcement.Text);
            Assert.Equal(pick == 0 ? ChaosAnnounceKind.Willpower : pick == 2 ? ChaosAnnounceKind.Temptation : ChaosAnnounceKind.Mantra, announcement.Kind);
            Assert.Equal(pick == 0 ? "resistance" : boon!.Id, announcement.ArtKey);
            Assert.Equal(pick == 2, cues.Contains("sin_accept.mp3"));
            Assert.Equal(draft, ChaosLessons.Progress("draft4"));
            Assert.Equal(surrender, ChaosLessons.Progress("surrender"));
        }
        finally { CoreAudio.PlayOneShotProvider = oldAudio; ChaosRunHost.PulseFx = oldPulse; CoreModArt.AudioOverridePathProvider = oldMod; File.Delete(modCue); CoreSettings.Current.ChaosAnnouncerEnabled = oldAnnouncer; }
    });

    [Fact]
    public Task Shower_waits_for_go_then_six_treats_fall_from_above() => Run(() =>
    {
        ChaosMeta.State.LessonsComplete.Add("silk_touch");
        var overlay = BeginDraft();
        Choose(overlay, ChaosBoonPool.All.Single(b => b.Id == "welcome_shower"));
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        var language = LocalizationManager.Instance.CurrentLanguage;
        try { LocalizationManager.Instance.SetLanguage("fr"); overlay.FinishCountdown();
            Assert.Contains("pluie de bienvenue : friandises venues du ciel", ChaosRunHost.State!.RecentEvents); }
        finally { LocalizationManager.Instance.SetLanguage(language); }
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

    [Fact]
    public Task Boundary_and_pick_lessons_wait_for_go_then_hold_until_last_card() => Run(() =>
    {
        var clock = new SteppedTime();
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
            clock.Advance(TimeSpan.FromMilliseconds(ChaosUnlockCardOverlay.OutMs));
            card.Step();
            Assert.False(ChaosUnlockCardOverlay.IsShowing);
            Assert.False(BubbleOverlay.Field.HoldChaos);
            Assert.Equal(6, BubbleOverlay.ChaosCount);
            ChaosRunHost.Tick();
            Assert.True(state.ElapsedSec > elapsed);
        }
        finally { ChaosUnlockCardOverlay.Clock = oldClock; }
    });

    [Fact]
    public Task Panic_discards_deferred_cards_before_the_next_run() => Run(() =>
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

    [Fact]
    public Task Fresh_meta_restores_all_slots_and_languages_have_event_keys() => Run(() =>
    {
        var original = ChaosMeta.State;
        var paths = Enumerable.Range(1, ChaosMetaStore.SlotCount).Select(ChaosMetaStore.SlotFilePath).ToArray();
        var before = paths.Select(p => File.Exists(p) ? File.ReadAllBytes(p) : null).ToArray();
        InFreshMeta(() => { ChaosFirstTimes.TryAward(ChaosFirstTimes.Whisper); });
        Assert.Same(original, ChaosMeta.State);
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var strings = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json")));
            foreach (var key in new[] { "chaos_event_reroll", "chaos_event_skip", "chaos_event_shower", "chaos_announce_resist", "chaos_event_sin_bonus", "chaos_event_sin_shielded", "chaos_event_sin_resistance" }) Assert.False(string.IsNullOrWhiteSpace((string?)strings[key]));
        }
        for (int i = 0; i < paths.Length; i++)
            Assert.Equal(before[i], File.Exists(paths[i]) ? File.ReadAllBytes(paths[i]) : null);
    });
}
