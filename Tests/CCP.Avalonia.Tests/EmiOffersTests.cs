using System.Linq;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiOffersTests
{
    [Fact]
    public void AvailabilityRefusesUnknownAndUnavailableDoors()
    {
        var desk = EmiDeskService.Instance;
        Assert.False(desk.IsTargetAvailable("not-a-door"));
        Assert.True(desk.IsTargetAvailable("settings"));
        ConditioningControlPanel.Services.JustDrop.JustDropService.SetServerEnabledForTests(false);
        Assert.False(desk.IsTargetAvailable("justdrop"));
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
        var before = EmiSuggester.ScoreOf("vault");
        EmiDeskService.Instance.OpenTarget("vault");
        Assert.True(EmiSuggester.ScoreOf("vault") > before);
    });
}
