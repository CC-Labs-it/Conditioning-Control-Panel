using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class ChaosToyEffectsTests
{
    private static void Setup()
    {
        AmbientBeatLoopsTests.Setup();
        ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 }); ChaosRunHost.Overlay!.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
        BubbleOverlay.Field.Bubbles.Clear();
    }
    // Restore every mutated seam and bank for every case, including assertion failures.
    private static Task Run(Action test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var meta = ChaosMeta.State;
        var awarded = meta.FirstTimesAwarded.ToArray();
        int sparks = meta.Sparks;
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        string language = loc.CurrentLanguage;
        var old = (ChaosRunHost.PopText, ChaosRunHost.ReadToyPointer, ChaosPopText.Prepare,
            ChaosPopText.PlatformCanShow, CoreSettings.Current.ChaosAnnouncerEnabled);
        try { Setup(); test(); }
        finally
        {
            ChaosRunHost.ForceShutdown();
            (ChaosRunHost.PopText, ChaosRunHost.ReadToyPointer, ChaosPopText.Prepare,
                ChaosPopText.PlatformCanShow, CoreSettings.Current.ChaosAnnouncerEnabled) = old;
            loc.SetLanguage(language);
            meta.Sparks = sparks; meta.FirstTimesAwarded.Clear();
            foreach (var id in awarded) meta.FirstTimesAwarded.Add(id);
            ChaosMeta.Save();
        }
        return Task.CompletedTask;
    });
    private static ChaosToyState Equip(string id, double power, bool maxed = false)
    {
        var s = ChaosRunHost.State!;
        s.ActiveToys.Clear();
        s.ToyPower[id] = power;
        if (maxed) s.MaxedBoons.Add(id); else s.MaxedBoons.Remove(id);
        var toy = new ChaosToyState { Id = id, CooldownSec = 20, KeyLabel = "Q" };
        s.ActiveToys.Add(toy);
        return toy;
    }
    private static PixelPoint PointAt(AmbientBubble b, double offset = 0)
    {
        double scale = ChaosRunHost.Overlay!.Screens.All.ElementAtOrDefault(b.Screen)?.Scaling ?? 1;
        return new PixelPoint((int)((b.CenterX + offset) * scale), (int)(b.CenterY * scale));
    }
    private static AmbientBubble Spawn(bool live) => BubbleOverlay.SpawnChaos(new EffectBubbleSpec
        { VariantId = "pink", IsLive = live, FuseMs = 5000, SizePx = 200, Payload = new FlashPayload() });
    // WPF ChaosModeService.cs:210-224: the banked bonus reaches the feed and pop point.
    [Theory][InlineData("en")][InlineData("de")][InlineData("es")][InlineData("fr")][InlineData("it")]
    [InlineData("ja")][InlineData("ko")][InlineData("pt-BR")][InlineData("ru")][InlineData("zh-CN")]
    public Task First_time_award_toasts_once_in_the_current_language(string language) => Run(() =>
    {
        var meta = ChaosMeta.State;
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var calls = new System.Collections.Generic.List<(double X, double Y, string Text, Color Color)>();
        ChaosRunHost.PopText = (x, y, t, c) => calls.Add((x, y, t, c));
        loc.SetLanguage(language);
        BubbleOverlay.ChaosLastPopX = 123; BubbleOverlay.ChaosLastPopY = 456; meta.FirstTimesAwarded.Clear();
        foreach (var id in ChaosFirstTimes.Amounts.Keys)
        {
            Assert.True(ChaosFirstTimes.TryAward(id)); Assert.False(ChaosFirstTimes.TryAward(id));
        }
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, calls.Count);
        foreach (var pair in ChaosFirstTimes.Amounts)
        {
            string label = loc.Get("chaos_bonus_" + pair.Key);
            Assert.NotEqual("chaos_bonus_" + pair.Key, label);
            if (language != "en") Assert.NotEqual(ChaosFirstTimes.Labels[pair.Key], label);
            string text = $"+{pair.Value} {ChaosGlyphs.Drops} {label}";
            var call = Assert.Single(calls.Where(c => c.Text == text));
            Assert.Equal(123, call.X); Assert.Equal(426, call.Y); Assert.Equal(Color.FromRgb(0xC8, 0xA8, 0xFF), call.Color);
            Assert.Single(ChaosRunHost.State!.RecentEvents.Where(e => e == text));
        }
        // A queued award belongs to this run, even if another run starts before dispatch.
        meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play); Assert.True(ChaosFirstTimes.TryAward(ChaosFirstTimes.Play));
        ChaosRunHost.ForceShutdown();
        Setup();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(5, calls.Count); Assert.DoesNotContain(ChaosRunHost.State!.RecentEvents, e => e.Contains("+15"));
    });
    [Fact] public Task First_time_award_uses_the_real_pop_text_when_no_hook_is_installed() => Run(() =>
    {
        var meta = ChaosMeta.State;
        meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play); ChaosRunHost.PopText = null;
        ChaosPopText.Prepare = (w, r) => { w.Position = r.Position; return true; };
        ChaosPopText.PlatformCanShow = () => true;
        CoreSettings.Current.ChaosAnnouncerEnabled = true;
        Assert.True(ChaosFirstTimes.TryAward(ChaosFirstTimes.Play));
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(Assert.Single(ChaosPopText.Pool.All).IsVisible); ChaosRunHost.ForceShutdown(); Assert.Empty(ChaosPopText.Pool.All);
    });
    [Fact] public Task Panic_between_award_and_popup_callbacks_cannot_recreate_the_pool() => Run(() =>
    {
        ChaosRunHost.PopText = null;
        ChaosPopText.PlatformCanShow = () => true;
        int prepared = 0;
        ChaosPopText.Prepare = (w, r) => { prepared++; w.Position = r.Position; return true; };
        CoreSettings.Current.ChaosAnnouncerEnabled = true;
        var meta = ChaosMeta.State;
        meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
        var events = ChaosRunHost.State!.RecentEvents;
        bool panicked = false;
        System.Collections.Specialized.NotifyCollectionChangedEventHandler changed = (_, _) =>
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                panicked = true;
                ChaosRunHost.ForceShutdown(); Assert.Empty(ChaosPopText.Pool.All);
            });
        events.CollectionChanged += changed;
        try
        {
            Assert.True(ChaosFirstTimes.TryAward(ChaosFirstTimes.Play));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(panicked); Assert.Equal(0, prepared); Assert.Empty(ChaosPopText.Pool.All);
        }
        finally { events.CollectionChanged -= changed; }
    });
    [Theory][InlineData("snap_field")][InlineData("vibe_popping")]
    public Task Toys_preserve_touch_only_and_linked_escort_immunity(string id) => Run(() =>
    {
        var tease = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsTease = true, SizePx = 200 });
        var brittle = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsBrittle = true, SizePx = 200 });
        var live = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsLive = true, IsChaperoneLive = true, SizePx = 200, FuseMs = 5000 });
        var escort = Spawn(false);
        live.Escort = escort;
        foreach (var b in BubbleOverlay.Field.Bubbles) { b.X = live.X; b.Y = live.Y; }
        ChaosRunHost.ReadToyPointer = () => (PointAt(live), true, false);
        var toy = Equip(id, 30, maxed: true);
        ChaosRunHost.UseToyById(id);
        if (id == "vibe_popping") ChaosRunHost.SweepToyPointer();
        Assert.False(tease.Popping); Assert.False(brittle.Popping); Assert.False(live.Popping); Assert.True(escort.Popping);
        Assert.Equal(0, ChaosRunHost.State!.Defused);
        toy.CooldownRemainingSec = 0;
        if (id == "vibe_popping") ChaosRunHost.SweepToyPointer();
        else ChaosRunHost.UseToyById(id);
        Assert.True(live.Popping); Assert.Equal(1, ChaosRunHost.State.Defused);
    });
    [Theory][InlineData("en")][InlineData("de")][InlineData("es")][InlineData("fr")][InlineData("it")]
    [InlineData("ja")][InlineData("ko")][InlineData("pt-BR")][InlineData("ru")][InlineData("zh-CN")]
    public Task Toy_events_use_translations_in_every_language(string language) => Run(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        string[] keys = { "chaos_toy_buzz", "chaos_toy_snap_all", "chaos_toy_snap_live", "chaos_toy_afterglow" };
        loc.SetLanguage("en");
        var english = keys.Select(loc.Get).ToArray();
        loc.SetLanguage(language);
        var expected = keys.Select(loc.Get).ToArray();
        for (int i = 0; i < keys.Length; i++)
        {
            Assert.NotEqual(keys[i], expected[i]);
            if (language != "en") Assert.NotEqual(english[i], expected[i]);
        }
        var state = ChaosRunHost.State!;
        state.AfterglowSec = 1;
        Equip("snap_field", 30);
        ChaosRunHost.UseToyById("snap_field");
        Equip("snap_field", 30, maxed: true);
        ChaosRunHost.UseToyById("snap_field");
        Equip("vibe_popping", 1);
        ChaosRunHost.UseToyById("vibe_popping");
        for (int i = 0; i < 4; i++) ChaosRunHost.Tick();
        foreach (var message in expected) Assert.Contains(message, state.RecentEvents);
    });
    [Theory][InlineData(false)][InlineData(true)]
    public Task Snap_pays_for_live_bubbles_and_capstone_also_pays_for_treats(bool maxed) => Run(() =>
    {
        var meta = ChaosMeta.State;
        meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
        int sparks = meta.Sparks;
        var toy = Equip("snap_field", 30, maxed);
        var live = Spawn(true);
        var treat = Spawn(false);
        var freeze = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsFreeze = true, SizePx = 200 });
        var rabbit = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsDarter = true, SizePx = 200 });
        ChaosRunHost.ToggleManualPause(); ChaosRunHost.UseToyById(toy.Id); Assert.False(live.Popping);
        Assert.Equal(0, toy.CooldownRemainingSec); Assert.Equal(sparks, meta.Sparks); ChaosRunHost.ToggleManualPause();
        ChaosRunHost.OnToyKey("Q"); Assert.True(live.Popping); Assert.Equal(maxed, treat.Popping); Assert.False(freeze.Popping);
        Assert.False(rabbit.Popping); Assert.Equal(1, ChaosRunHost.State!.Defused); Assert.True(ChaosRunHost.State.Score > 0);
        Assert.Equal(30, toy.CooldownRemainingSec); Assert.Equal(sparks + 15, meta.Sparks);
        for (int i = 0; i < 3; i++) ChaosRunHost.Tick();
        Assert.True(toy.IsEffectActive); ChaosRunHost.Tick(); Assert.False(toy.IsEffectActive);
        toy.CooldownRemainingSec = 0;
        ChaosRunHost.UseToyById(toy.Id);
        Assert.Equal(sparks + 15, meta.Sparks); // only the first real fire pays
    });
    [Theory][InlineData(false)][InlineData(true)]
    public Task Vibe_requires_a_hold_until_capstone_or_afterglow_and_stops_on_panic(bool maxed) => Run(() =>
    {
        var toy = Equip("vibe_popping", 1, maxed);
        ChaosRunHost.State!.AfterglowSec = 1;
        var live = Spawn(true);
        // Match the head's per-screen coordinate conversion, including scaled displays.
        var point = PointAt(live);
        bool held = false;
        ChaosRunHost.ReadToyPointer = () => (point, held, false); ChaosRunHost.UseToyById(toy.Id); ChaosRunHost.SweepToyPointer();
        Assert.Equal(maxed, live.Popping);
        var freeze = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
            { VariantId = "freeze", IsFreeze = true, SizePx = 200 });
        freeze.X = live.X; freeze.Y = live.Y;
        held = true;
        ChaosRunHost.ToggleManualPause(); ChaosRunHost.SweepToyPointer(); Assert.False(freeze.Popping); ChaosRunHost.ToggleManualPause();
        ChaosRunHost.SweepToyPointer();
        Assert.False(freeze.Popping); // WPF keeps freeze pickup timing manual
        Assert.True(live.Popping); Assert.Equal(1, ChaosRunHost.State.Defused);
        for (int i = 0; i < 4; i++) ChaosRunHost.Tick();
        Assert.True(toy.IsEffectActive); // now in afterglow
        var treat = Spawn(false);
        treat.X = live.X; treat.Y = live.Y;
        held = false;
        ChaosRunHost.SweepToyPointer(); Assert.True(treat.Popping);
        for (int i = 0; i < 4; i++) ChaosRunHost.Tick();
        Assert.False(toy.IsEffectActive);
        var untouched = Spawn(false);
        untouched.X = live.X; untouched.Y = live.Y;
        ChaosRunHost.SweepToyPointer(); Assert.False(untouched.Popping); ChaosRunHost.ForceShutdown();
        Assert.Null(typeof(ChaosRunHost).GetField("_sweepTimer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null));
        Assert.DoesNotContain(BubbleOverlay.Field.Bubbles, b => b.Spec != null);
    });
    [Theory][InlineData(false)][InlineData(true)]
    public Task Sweep_uses_wand_reach_and_live_magnet(bool magnet) => Run(() =>
    {
        Equip("vibe_popping", 1);
        var s = ChaosRunHost.State!;
        s.Config.HitboxScale = magnet ? 1 : 2;
        s.MagnetEnabled = magnet;
        var live = Spawn(true);
        double offset = live.Size * (magnet ? 0.6 : 0.8);
        var point = PointAt(live, offset);
        ChaosRunHost.ReadToyPointer = () => (point, true, false); ChaosRunHost.UseToyById("vibe_popping"); ChaosRunHost.SweepToyPointer();
        Assert.True(live.Popping); // outside the sprite, inside the trained reach
    });
    [Fact] public Task Hidden_overlay_blocks_fire_and_sweep_and_inert_toys_do_not_award_play() => Run(() =>
    {
        ChaosMeta.State.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
        Equip("e_stim", 1);
        ChaosRunHost.UseToyById("e_stim"); Assert.False(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Play));
        var toy = Equip("vibe_popping", 1);
        var live = Spawn(true);
        ChaosRunHost.ReadToyPointer = () => (PointAt(live), true, false); ChaosRunHost.Overlay.Hide(); ChaosRunHost.UseToyById(toy.Id);
        Assert.Equal(0, toy.CooldownRemainingSec); Assert.False(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Play));
        ChaosRunHost.Overlay.Show(); ChaosRunHost.UseToyById(toy.Id); ChaosRunHost.Overlay.Hide(); ChaosRunHost.SweepToyPointer();
        Assert.False(live.Popping);
    });
    [Fact] public Task Toy_tooltip_has_wpf_chrome_delay_and_capstone_copy() => Run(() =>
    {
        var w = new ChaosToyButtonWindow(new ChaosToyState
            { Name = "Toy", Desc = "Description", Flavor = "Flavor", CapstoneDesc = "Capstone", KeyLabel = "Q" }, 0);
        try
        {
            w.Show();
            w.UpdateLayout();
            var disc = w.GetVisualDescendants().OfType<Border>()
                .Single(b => ToolTip.GetTip(b) is ToolTip);
            var tip = Assert.IsType<ToolTip>(ToolTip.GetTip(disc));
            Assert.Equal(new Thickness(10, 8), tip.Padding);
            Assert.Equal(Color.FromArgb(0xF2, 0x15, 0x12, 0x26), ((SolidColorBrush)tip.Background!).Color);
            Assert.Equal(250, ToolTip.GetShowDelay(disc));
            var lines = ((StackPanel)tip.Content!).Children.OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Equal(new[] { "◈ Toy · Q", "Description", "Flavor", "max: Capstone" }, lines);
        }
        finally { w.Close(); }
    });
}
