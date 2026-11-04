using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosWaveDraftTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        ChaosMeta.Init();
        ChaosMeta.State.RunsCompleted = 2; // skip is unlocked
        ChaosMeta.State.SeenSkipDebut = true;
        ChaosMeta.State.LessonsComplete.Add("silk_touch"); // this suite exercises drafts, not lesson cards
    }

    private static Task Run(Action body) => Run(() => { body(); return Task.CompletedTask; });
    internal static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        try { await body(); }
        finally { ChaosRunHost.ForceShutdown(); }
    });

    private static void Ticks(int count) { for (int i = 0; i < count; i++) ChaosRunHost.Tick(); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Start(bool enabled = true)
    {
        ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = enabled,
            AllowCurses = false, DraftAutoResumeSec = 0 });
        ChaosRunHost.Overlay!.FinishCountdown();
        ChaosRunHost.StopTimerForTests();
    }
    // Deal the guaranteed sin and commit its 1.5 s pick beat, as in WPF :1476-1483.
    internal static async Task PickSurrender(ChaosRunState state, ChaosOverlayWindow overlay)
    {
        state.Config.AllowCurses = true;
        state.Config.SinChance = 0;
        state.MaxedBoons.Add("surrender");
        state.SinExtraMult = 0.4;
        Ticks(80);
        var host = overlay.FindControl<StackPanel>("BoonCardHost")!;
        var pick = ((Border)host.Children[2]).GetVisualDescendants().OfType<Button>().Single();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!pick.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.True(pick.IsEnabled);
        Click(pick);
        deadline = DateTime.UtcNow.AddSeconds(10); while (state.WaveIndex == 1 && DateTime.UtcNow < deadline) await Task.Delay(50);
    }
    // WPF ChaosModeService.cs:1441-1483, 1577-1617 and ChaosOverlayWindow.xaml.cs:78-79.
    [Fact]
    public Task Wave_draft_clears_the_field_holds_clock_and_resumes_after_skip_countdown() => Run(() =>
    {
        ChaosWaveTimerWindow.SkipPlatformChecksForTests = true;
        try
        {
            Start();
            var state = ChaosRunHost.State!;
            var overlay = ChaosRunHost.Overlay!;
            state.ShowWaveTimer = true;
            BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsLive = true, FuseMs = 320, SizePx = 200, Payload = new FlashPayload() });
            Ticks(80);
            Assert.True(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
            Assert.Equal(1, state.WaveIndex);
            Assert.Equal(0, BubbleOverlay.ChaosCount);
            Assert.False(ChaosWaveTimerWindow.Active!.Pill.IsVisible);
            double elapsed = state.ElapsedSec;
            Ticks(12);
            ChaosRunHost.SpawnTick();
            ChaosRunHost.ToggleManualPause();
            Assert.Equal(elapsed, state.ElapsedSec);
            Assert.Equal(0, BubbleOverlay.ChaosCount);
            Assert.False(ChaosRunHost.IsManuallyPaused);

            int shields = state.Shields;
            var skip = overlay.FindControl<Button>("BtnSkipBoon")!;
            Assert.True(skip.IsVisible);
            Click(skip);
            Assert.Equal(shields + 1, state.Shields);
            Assert.Equal(2, state.WaveIndex);
            Assert.False(overlay.FindControl<Border>("DraftPanel")!.IsVisible);
            Ticks(4);
            Assert.Equal(elapsed, state.ElapsedSec);
            overlay.FinishCountdown();
            Ticks(4);
            Assert.Equal(elapsed + 1, state.ElapsedSec, 3);
            Assert.True(ChaosWaveTimerWindow.Active!.Pill.IsVisible);
        }
        finally { ChaosRunHost.ForceShutdown(); ChaosWaveTimerWindow.SkipPlatformChecksForTests = false; }
    });

    // WPF :1477-1483, :1507-1526, :1529-1547, :1586-1593.
    [Fact]
    public Task Reroll_and_card_pick_use_the_run_state_and_resume_after_the_pick_beat() => Run(async () =>
    {
        Start();
        var state = ChaosRunHost.State!;
        var overlay = ChaosRunHost.Overlay!;
        state.RerollsLeft = 1;
        Ticks(80);
        var host = overlay.FindControl<StackPanel>("BoonCardHost")!;
        Assert.Equal(3, host.Children.Count);
        Assert.True(ChaosMeta.State.DiscoveredCodexIds.Count(id => id.StartsWith("boon:")) >= 3);
        var reroll = overlay.FindControl<Button>("BtnReroll")!;
        Assert.True(reroll.IsVisible);
        Click(reroll);
        Assert.Equal(0, state.RerollsLeft);
        Assert.False(reroll.IsVisible);
        Assert.Equal(3, host.Children.Count);
        var card = (Border)host.Children[0];
        var pick = card.GetVisualDescendants().OfType<Button>().Single();
        Assert.True(pick.IsEnabled);
        Click(pick);
        Assert.Empty(state.ActiveBoons);
        var deadline = DateTime.UtcNow.AddSeconds(10); while (state.WaveIndex == 1 && DateTime.UtcNow < deadline) await Task.Delay(50);
        Assert.Single(state.ActiveBoons);
        Assert.Single(state.RunPickTiles);
        Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper));
        Assert.Equal(2, state.WaveIndex);
        var elapsed = state.ElapsedSec;
        Ticks(4);
        Assert.Equal(elapsed, state.ElapsedSec);
        overlay.FinishCountdown();
        Ticks(4);
        Assert.Equal(elapsed + 1, state.ElapsedSec, 3);
    });

    // WPF :1477-1479, :1536-1566: Surrender guarantees a sin and shields its first drawback.
    [Fact]
    public Task Surrender_shields_the_first_sin_and_grants_resistance() => Run(async () =>
    {
        Start();
        var state = ChaosRunHost.State!;
        int shields = state.Shields;
        double mult = state.BoonMult;
        await PickSurrender(state, ChaosRunHost.Overlay!);
        var sin = Assert.Single(state.ActiveCurses);
        Assert.True(state.SurrenderShieldUsed);
        Assert.Equal(shields + 1, state.Shields);
        Assert.Equal(mult + sin.RunMultBonus + 0.4, state.BoonMult, 6);
        Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes));
        Assert.Equal(2, state.WaveIndex);
    });

    // WPF ChaosModeService.cs:1521, 1555-1561, 1580-1583: the English wording and translated output.
    [Theory]
    [InlineData("en", "🎲 tempted fate again", "♥ resisted → +1 resistance", "+1 RESISTANCE")]
    [InlineData("es", "🎲 volviste a tentar a la suerte", "♥ resististe → +1 resistencia", "+1 RESISTENCIA")]
    [InlineData("pt-BR", "🎲 tentou a sorte de novo", "♥ resistiu → +1 resistência", "+1 RESISTÊNCIA")]
    [InlineData("fr", "🎲 le destin est tenté à nouveau", "♥ tu as résisté → +1 résistance", "+1 RÉSISTANCE")]
    [InlineData("it", "🎲 hai tentato ancora la sorte", "♥ hai resistito → +1 resistenza", "+1 RESISTENZA")]
    [InlineData("de", "🎲 das Schicksal erneut herausgefordert", "♥ widerstanden → +1 Widerstand", "+1 WIDERSTAND")]
    [InlineData("ru", "🎲 снова испытала судьбу", "♥ сопротивление → +1 сопротивление", "+1 СОПРОТИВЛЕНИЕ")]
    [InlineData("ja", "🎲 再び運命を試した", "♥ 抵抗した → +1 抵抗力", "+1 抵抗力")]
    [InlineData("ko", "🎲 다시 운명에 도전했어", "♥ 저항했어 → +1 저항력", "+1 저항력")]
    [InlineData("zh-CN", "🎲 再次挑战命运", "♥ 抵抗 → +1 抵抗力", "+1 抵抗力")]
    public Task Draft_feedback_uses_the_selected_language(string language, string rerolled, string resisted, string announced) => Run(() =>
    {
        var loc = LocalizationManager.Instance;
        var oldLanguage = loc.CurrentLanguage;
        var settings = global::ConditioningControlPanel.CoreSettings.Current;
        bool oldAnnouncer = settings.ChaosAnnouncerEnabled;
        try
        {
            loc.SetLanguage(language);
            settings.ChaosAnnouncerEnabled = true;
            // Verify the other three keys are present in each actual language, not an English fallback.
            foreach (var key in new[] { "chaos_draft_sin_embraced", "chaos_draft_sin_shielded", "chaos_draft_sin_resistance" })
            {
                Assert.NotEqual(key, Loc.Get(key));
                if (language != "en") Assert.NotEqual(loc.GetEnglish(key), Loc.Get(key));
            }
            Assert.DoesNotContain("{0", Loc.GetF("chaos_draft_sin_embraced", 0.4));
            if (language == "en")
            {
                Assert.Equal("sin embraced (+{0:0.00}x)", loc.GetEnglish("chaos_draft_sin_embraced"));
                Assert.Equal("the candle took the sting (no drawback)", loc.GetEnglish("chaos_draft_sin_shielded"));
                Assert.Equal("you said yes. it gave back (+1 resistance)", loc.GetEnglish("chaos_draft_sin_resistance"));
            }
            Start();
            var state = ChaosRunHost.State!;
            state.RerollsLeft = 1;
            Ticks(80);
            var overlay = ChaosRunHost.Overlay!;
            Click(overlay.FindControl<Button>("BtnReroll")!);
            Assert.Contains(rerolled, state.RecentEvents);
            Click(overlay.FindControl<Button>("BtnSkipBoon")!);
            Assert.Contains(resisted, state.RecentEvents);
            Assert.Equal(announced, ChaosAnnouncerOverlay.Active!.Current!.Text);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            settings.ChaosAnnouncerEnabled = oldAnnouncer;
            loc.SetLanguage(oldLanguage);
        }
    });

    // WPF :1447-1456, :3199-3243 and :3171-3189.
    [Fact]
    public Task Drafts_off_rolls_on_and_stop_or_panic_cannot_resume_a_draft() => Run(() =>
    {
        Start(false);
        Ticks(84);
        Assert.Equal(2, ChaosRunHost.State!.WaveIndex);
        Assert.False(ChaosRunHost.Overlay!.FindControl<Border>("DraftPanel")!.IsVisible);
        ChaosRunHost.ForceShutdown();

        Start();
        var overlay = ChaosRunHost.Overlay!;
        Ticks(80);
        var skip = overlay.FindControl<Button>("BtnSkipBoon")!;
        ChaosRunHost.RequestStop();
        Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
        Click(skip);
        overlay.FinishCountdown();
        Assert.False(ChaosRunHost.IsDescending);
        Assert.True(overlay.FindControl<Border>("ResultsPanel")!.IsVisible);
        Assert.True(overlay.FindControl<global::Avalonia.Controls.Shapes.Rectangle>("Backdrop")!.IsVisible);
        ChaosRunHost.ForceShutdown();

        Start();
        overlay = ChaosRunHost.Overlay!;
        Ticks(80);
        Click(overlay.FindControl<Button>("BtnSkipBoon")!);
        ChaosRunHost.ForceShutdown();
        overlay.FinishCountdown();
        Assert.False(ChaosRunHost.IsActive);
        Assert.False(ChaosRunHost.IsDescending);
    });
}
