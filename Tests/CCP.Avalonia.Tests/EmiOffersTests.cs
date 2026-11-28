using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiOffersTests
{
    [Fact]
    public void ShrinkOfferResizesAndPersists() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var desk = EmiDeskService.Instance;
        try
        {
            var win = desk.BeginPresentation()!;
            win.ApplyBodyWidth(EmiDeskWindow.MinBodyWidth + 50);
            Assert.True(EmiOffers.EffectFeasible("shrink"));
            EmiOffers.Run("shrink", true);
            Assert.Equal(EmiDeskWindow.MinBodyWidth, win.BodyWidth);
            Assert.Equal(EmiDeskWindow.MinBodyWidth, CoreSettings.Current.EmiDeskWidth);
            Assert.False(EmiOffers.EffectFeasible("shrink"));
        }
        finally
        {
            desk.Window?.ShutDown();
            CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public void OfferFeasibilityChecksTargetsAndBedtime()
    {
        var state = EmiState.Current;
        var pins = state.Pins.ToList();
        var bedtime = state.BedtimeUntil;
        try
        {
            state.Pins.Clear();
            state.BedtimeUntil = System.DateTime.MinValue;
            Assert.False(EmiOffers.EffectFeasible("open:not-a-door"));
            Assert.True(EmiOffers.EffectFeasible("open:settings"));
            Assert.True(EmiOffers.EffectFeasible("pinTop:settings"));
            state.Pins.Add("settings");
            Assert.False(EmiOffers.EffectFeasible("pinTop:settings"));
            Assert.True(EmiOffers.EffectFeasible("bedtime"));
            state.BedtimeUntil = System.DateTime.UtcNow.AddHours(1);
            Assert.False(EmiOffers.EffectFeasible("bedtime"));
            Assert.True(EmiOffers.EffectFeasible("none"));
            Assert.False(EmiOffers.EffectFeasible("unknown"));
        }
        finally { state.Pins.Clear(); state.Pins.AddRange(pins); state.BedtimeUntil = bedtime; }
    }

    [Fact]
    public void BedtimeEffectSetsNextSix() => AvaloniaTestDispatcher.Run(() =>
    {
        var state = EmiState.Current;
        var previous = state.BedtimeUntil;
        try
        {
            var now = System.DateTime.Now;
            var six = now.Date.AddHours(6);
            if (now >= six) six = six.AddDays(1);
            EmiOffers.Run("bedtime", true);
            Assert.Equal(six.ToUniversalTime(), state.BedtimeUntil);
        }
        finally { state.BedtimeUntil = previous; EmiState.SaveSoon(); }
    });

    [Fact]
    public void AvailabilityRefusesUnknownAndUnavailableDoors()
    {
        var desk = EmiDeskService.Instance;
        Assert.False(desk.IsTargetAvailable("not-a-door"));
        Assert.True(desk.IsTargetAvailable("settings"));
        var before = ConditioningControlPanel.Services.JustDrop.JustDropService.ServerEnabledForTests;
        try
        {
            ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(false);
            Assert.False(desk.IsTargetAvailable("justdrop"));
        }
        finally { ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(before); }
    }

    [Fact]
    public void PinTopPromotesKnownDoorAndIgnoresUnknown() => AvaloniaTestDispatcher.Run(() =>
    {
        var pins = EmiState.Current.Pins.ToList();
        try
        {
            EmiState.Current.Pins.Clear();
            EmiDeskService.Instance.PinTop("settings");
            Assert.Equal("settings", EmiState.Current.Pins[0]);
            EmiDeskService.Instance.PinTop("not-a-door");
            Assert.Single(EmiState.Current.Pins);
        }
        finally { EmiState.Current.Pins.Clear(); EmiState.Current.Pins.AddRange(pins); }
    });

    [Fact]
    public void OpenTargetUsesRingPick() => AvaloniaTestDispatcher.Run(() =>
    {
        var state = EmiState.Current;
        var hadUsage = state.Usage.TryGetValue("vault", out var usage);
        var hadScore = state.OpenScore.TryGetValue("vault", out var score);
        var hadAt = state.UsageAt.TryGetValue("vault", out var at);
        try
        {
            var before = EmiSuggester.ScoreOf("vault");
            EmiDeskService.Instance.OpenTarget("vault");
            Assert.True(EmiSuggester.ScoreOf("vault") > before);
        }
        finally
        {
            if (hadUsage) state.Usage["vault"] = usage; else state.Usage.Remove("vault");
            if (hadScore) state.OpenScore["vault"] = score; else state.OpenScore.Remove("vault");
            if (hadAt) state.UsageAt["vault"] = at; else state.UsageAt.Remove("vault");
        }
    });

    [Fact]
    public void AvailabilityTestRestoresServerFlag()
    {
        var before = ConditioningControlPanel.Services.JustDrop.JustDropService.ServerEnabledForTests;
        try
        {
            ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(true);
            AvailabilityRefusesUnknownAndUnavailableDoors();
            Assert.True(ConditioningControlPanel.Services.JustDrop.JustDropService.ServerEnabledForTests);
        }
        finally { ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(before); }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void OpenTargetTestRestoresUsageEntries(bool scoreExists, bool atExists, bool usageExists)
    {
        var state = EmiState.Current;
        var hadUsage = state.Usage.TryGetValue("vault", out var oldUsage);
        var hadScore = state.OpenScore.TryGetValue("vault", out var oldScore);
        var hadAt = state.UsageAt.TryGetValue("vault", out var oldAt);
        try
        {
            if (usageExists) state.Usage["vault"] = 7; else state.Usage.Remove("vault");
            if (scoreExists) state.OpenScore["vault"] = 10; else state.OpenScore.Remove("vault");
            if (atExists) state.UsageAt["vault"] = System.DateTime.UtcNow.Ticks; else state.UsageAt.Remove("vault");
            var expectedScore = state.OpenScore.GetValueOrDefault("vault");
            var expectedAt = state.UsageAt.GetValueOrDefault("vault");
            OpenTargetUsesRingPick();
            Assert.Equal(usageExists, state.Usage.ContainsKey("vault"));
            if (usageExists) Assert.Equal(7, state.Usage["vault"]);
            Assert.Equal(scoreExists, state.OpenScore.ContainsKey("vault"));
            Assert.Equal(atExists, state.UsageAt.ContainsKey("vault"));
            if (scoreExists) Assert.Equal(expectedScore, state.OpenScore["vault"]);
            if (atExists) Assert.Equal(expectedAt, state.UsageAt["vault"]);
        }
        finally
        {
            if (hadUsage) state.Usage["vault"] = oldUsage; else state.Usage.Remove("vault");
            if (hadScore) state.OpenScore["vault"] = oldScore; else state.OpenScore.Remove("vault");
            if (hadAt) state.UsageAt["vault"] = oldAt; else state.UsageAt.Remove("vault");
        }
    }
}
