using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosFreezeLocalizationTests
{
    [Fact]
    public Task Freeze_trigger_awards_first_toy_only_when_fired() => WithRun(state =>
    {
        var meta = ChaosMeta.State;
        meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
        int before = meta.Sparks;
        state.ActivesDisabled = true;
        ChaosRunHost.UseToyById("freeze_trigger");
        Assert.False(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Play));
        Assert.Equal(before, meta.Sparks);
        state.ActivesDisabled = false;
        ChaosRunHost.UseToyById("freeze_trigger");
        Assert.True(ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Play));
        Assert.Equal(before + 15, meta.Sparks);
        state.ActiveToys.Single().CooldownRemainingSec = 0;
        ChaosRunHost.UseToyById("freeze_trigger");
        Assert.Equal(before + 15, meta.Sparks);
    });

    [Fact]
    public Task Freeze_trigger_posts_localized_feed() => WithRun(state =>
    {
        LocalizationManager.Instance.SetLanguage("fr");
        ChaosRunHost.UseToyById("freeze_trigger");
        Assert.Equal("❄ tout reste immobile", state.RecentEvents[0]);
    });

    [Fact]
    public Task Freeze_banner_supplies_localized_fallback() => WithRun(state =>
    {
        LocalizationManager.Instance.SetLanguage("fr");
        ChaosRunHost.UseToyById("freeze_trigger");
        var banner = ChaosEffectBannerOverlay.Active!;
        var text = Assert.Single(banner.Strip.Row, e => e.Id == "freeze").Text;
        Assert.Equal("Gel", text);
        ChaosEffectBannerOverlay.Show("missing-freeze-test", text, Colors.LightBlue);
        Assert.Equal("GEL", Assert.IsType<OutlinedTextBlock>(banner.Row.Children.Last()).Text);
    });

    private static Task WithRun(Action<ChaosRunState> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var loc = LocalizationManager.Instance;
        string language = loc.CurrentLanguage;
        var meta = ChaosMeta.State;
        bool hadAward = meta.FirstTimesAwarded.Contains(ChaosFirstTimes.Play);
        int sparks = meta.Sparks;
        bool checks = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        try
        {
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            var state = ChaosRunHost.State!;
            state.ActiveToys.Clear();
            state.ActiveToys.Add(new ChaosToyState { Id = "freeze_trigger", ChargesLeft = 3 });
            test(state);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = checks;
            loc.SetLanguage(language);
            meta.Sparks = sparks;
            if (hadAward) meta.FirstTimesAwarded.Add(ChaosFirstTimes.Play);
            else meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
        }
        return Task.CompletedTask;
    });
}
