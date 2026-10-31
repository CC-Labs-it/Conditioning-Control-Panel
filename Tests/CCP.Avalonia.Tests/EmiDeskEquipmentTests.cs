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

public sealed partial class AccountSeedTests
{
    [Fact]
    public Task FreshLaunchOwnershipEnablesDeskEquipmentWithoutAShopDoor() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var identity = (s.UnifiedId, s.AuthToken, s.OfflineMode);
        var sink = ConditioningControlPanel.Services.ProviderSubscription.PrizesSink;
        var hat = CoreSettings.Current.EmiDeskHat;
        var toy = CoreSettings.Current.EmiDeskToy;
        var stamp = CoreSettings.Current.EmiDeskEquipUpdatedMs;
        var owed = CoreSettings.Current.EmiDeskEquipPushOwed;
        try
        {
            PrizeOwnership.ResetHeld();
            s.UnifiedId = "equipment-owner"; s.AuthToken = "test"; s.OfflineMode = false;
            Assert.True(AccountSeed.Seed(Make)); AccountSeed.RestoreSession();
            using var desk = new DeskCleanup(new EmiDeskWindow());
            using var options = new OptionsCleanup(new EmiOptionsWindow(desk.Window));
            var hats = options.Window.FindControl<WrapPanel>("DeskHatPicks")!.Children.Cast<RadioButton>().ToArray();
            Assert.Equal(3,hats.Length); Assert.False(hats[1].IsEnabled); Assert.False(hats[2].IsEnabled);
            var sync = new ConditioningControlPanel.Services.SyncPush(() => null, () => false, new EquipmentWire());
            sync.MarkLoaded(null); Assert.True(await sync.PushAsync("fresh launch"));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            hats = options.Window.FindControl<WrapPanel>("DeskHatPicks")!.Children.Cast<RadioButton>().ToArray();
            Assert.False(hats[1].IsEnabled); Assert.True(hats[2].IsEnabled);
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
            PrizeOwnership.ResetHeld(); ConditioningControlPanel.Services.ProviderSubscription.PrizesSink = sink;
            (s.UnifiedId, s.AuthToken, s.OfflineMode) = identity;
            CoreSettings.Current.EmiDeskHat = hat; CoreSettings.Current.EmiDeskToy = toy;
            CoreSettings.Current.EmiDeskEquipUpdatedMs = stamp; CoreSettings.Current.EmiDeskEquipPushOwed = owed;
        }
    });
    private sealed class DeskCleanup(EmiDeskWindow window) : System.IDisposable
    { public EmiDeskWindow Window => window; public void Dispose() => window.Close(); }
    private sealed class OptionsCleanup(EmiOptionsWindow window) : System.IDisposable
    { public EmiOptionsWindow Window => window; public void Dispose() => window.Kill(); }
    private sealed class EquipmentWire : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage r, System.Threading.CancellationToken ct)
            => Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent(
                "{\"success\":true,\"prizes\":{\"revision\":1,\"grants\":[\"cos.desk.hat.party\"]}}") });
    }
}
