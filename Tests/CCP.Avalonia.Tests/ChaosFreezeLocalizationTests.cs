using System;
using System.Linq;
using System.IO;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Slot_is_restored_after_award_even_on_exception(bool existed)
    {
        using var original = new ChaosMetaSlotSnapshot();
        var path = ChaosMetaStore.SlotFilePath(ChaosMetaStore.ActiveSlot);
        var meta = ChaosMeta.State;
        int sparks = meta.Sparks;
        bool hadAward = meta.FirstTimesAwarded.Contains(ChaosFirstTimes.Play);
        try
        {
            if (existed) File.WriteAllText(path, "{\"probe\":7}");
            else File.Delete(path);
            byte[]? before = existed ? File.ReadAllBytes(path) : null;
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var snapshot = new ChaosMetaSlotSnapshot();
                meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
                meta.Sparks = 100;
                Assert.True(ChaosFirstTimes.TryAward(ChaosFirstTimes.Play));
                Assert.Equal(115, ChaosMetaStore.Load().Sparks);
                throw new InvalidOperationException();
            }));
            void CheckSlot()
            {
                Assert.Equal(existed, File.Exists(path));
                if (existed) Assert.Equal(before, File.ReadAllBytes(path));
            }
            CheckSlot();
            meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
            await new ChaosFreezeLocalizationTests().Freeze_trigger_posts_localized_feed();
            CheckSlot();
            await new ChaosFreezeGameplayTests().Freeze_stops_spawning_until_it_expires();
            CheckSlot();
            await new ChaosEffectClockTests().Freeze_refresh_pause_expiry_and_shutdown_are_run_scoped();
            CheckSlot();
        }
        finally
        {
            meta.Sparks = sparks;
            if (hadAward) meta.FirstTimesAwarded.Add(ChaosFirstTimes.Play);
            else meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
        }
    }

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
        using var slot = new ChaosMetaSlotSnapshot();
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

/// <summary>Restore the active Chaos slot exactly, including when it did not exist.</summary>
internal sealed class ChaosMetaSlotSnapshot : IDisposable
{
    private readonly string _path = ChaosMetaStore.SlotFilePath(ChaosMetaStore.ActiveSlot);
    private readonly byte[]? _contents;
    internal ChaosMetaSlotSnapshot() => _contents = File.Exists(_path) ? File.ReadAllBytes(_path) : null;
    public void Dispose()
    {
        if (_contents is null) File.Delete(_path);
        else File.WriteAllBytes(_path, _contents);
    }
}
