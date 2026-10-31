using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;
namespace CCP.Avalonia.Tests;

public sealed class EmiDeskEquipmentTests
{
    [Fact]
    public Task PickersKeepLockedItemsDisabledAndNeverOfferAShopDoor() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var before = PrizeOwnership.IsGranted;
        var hat = CoreSettings.Current.EmiDeskHat;
        var toy = CoreSettings.Current.EmiDeskToy;
        var stamp = CoreSettings.Current.EmiDeskEquipUpdatedMs;
        var owed = CoreSettings.Current.EmiDeskEquipPushOwed;
        try
        {
            PrizeOwnership.IsGranted = id => id == "cos.desk.hat.party";
            using var desk = new DeskCleanup(new EmiDeskWindow());
            using var options = new OptionsCleanup(new EmiOptionsWindow(desk.Window));
            var hats = options.Window.FindControl<WrapPanel>("DeskHatPicks")!.Children.Cast<RadioButton>().ToArray();
            Assert.Equal(3,hats.Length); Assert.False(hats[1].IsEnabled); Assert.True(hats[2].IsEnabled);
            hats[2].IsChecked = true;
            Assert.Equal("desk.hat.party", CoreSettings.Current.EmiDeskHat);
            Assert.Equal("desk.hat.party", desk.Window.DeskItems.Hat);
            Assert.True(CoreSettings.Current.EmiDeskEquipPushOwed);
            hats[0].IsChecked = true; Assert.Null(CoreSettings.Current.EmiDeskHat);
            Assert.Null(options.Window.FindControl<Button>("BtnDeskCounter"));
            Assert.Null(desk.Window.FindControl<Image>("DeskHatOverImage")!.Source);
        }
        finally
        {
            PrizeOwnership.IsGranted = before;
            CoreSettings.Current.EmiDeskHat = hat; CoreSettings.Current.EmiDeskToy = toy;
            CoreSettings.Current.EmiDeskEquipUpdatedMs = stamp; CoreSettings.Current.EmiDeskEquipPushOwed = owed;
        }
        return Task.CompletedTask;
    });
    private sealed class DeskCleanup(EmiDeskWindow window) : System.IDisposable
    { public EmiDeskWindow Window => window; public void Dispose() => window.Close(); }
    private sealed class OptionsCleanup(EmiOptionsWindow window) : System.IDisposable
    { public EmiOptionsWindow Window => window; public void Dispose() => window.Kill(); }
}
