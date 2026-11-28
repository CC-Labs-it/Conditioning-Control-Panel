using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiSpiralOfferTests
{
    [Fact]
    public Task SpiralOfferStartsAndPanicStopsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        AmbientBeatLoopsTests.Setup();
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var desk = EmiDeskService.Instance;
        try
        {
            PanicAftermath.EndQuiet();
            CoreSettings.Current.EmiDeskEnabled = true;
            CoreSettings.Current.EmiDeskMuteAvatar = false;
            CoreSettings.Current.GentleVisuals = true;
            SpiralOverlay.SkipPlatformChecksForTests = true;
            await desk.Summon();
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(desk.Window);
            Assert.True(EmiOffers.EffectFeasible(" SPIRAL "));
            EmiOffers.Run("spiral", true);
            Assert.True(SpiralOverlay.HasTimedPayload);
            if (SpiralOverlay.Decoding is { } decode) await decode;
            Dispatcher.UIThread.RunJobs();
            var surface = Assert.Single(SpiralOverlay.Shown);
            SpiralOverlay.Stop();
            Assert.False(surface.IsVisible);
            Assert.False(SpiralOverlay.HasTimedPayload);
            EmiOffers.Run("spiral", true);
            Assert.True(SpiralOverlay.HasTimedPayload);
            PanicSurfaces.StopAll("test");
            Assert.False(SpiralOverlay.HasTimedPayload);
            EmiOffers.Run("spiral", true);
            Assert.False(SpiralOverlay.HasTimedPayload);
        }
        finally
        {
            SpiralOverlay.CloseAll();
            SpiralOverlay.SkipPlatformChecksForTests = false;
            desk.Window?.ShutDown();
            PanicAftermath.EndQuiet();
            CoreSettings.ServiceProvider = old;
        }
    });
}
