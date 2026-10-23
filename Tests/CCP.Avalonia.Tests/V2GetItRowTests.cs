using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Get it row (WPF Controls/V2GetItRow) on the Flashes card: Core V2PurchaseRule over this head's
/// inputs. Owned hides it, signed out offers the login, signed in reads "not available" (no counter here).</summary>
public sealed class V2GetItRowTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void Decide_maps_ownership_and_sign_in_to_the_rule_states()
    {
        Assert.Equal(V2PurchaseRowState.Hidden,
            V2GetItRow.Decide(V2PurchaseRule.FlashesPrizeId, true, id => id == "fx.flash.pendulum").State);
        Assert.Equal(V2PurchaseRowState.Hidden,
            V2GetItRow.Decide(V2PurchaseRule.BubblesPrizeId, false, id => id == "fx.bubble.spiral_in").State);
        // Jackpot Remix is its own prize: owning it keeps the offer up (V2PurchaseRule.OwnsPrize).
        var signIn = V2GetItRow.Decide(V2PurchaseRule.FlashesPrizeId, false, id => id == "fx.flash.jackpot_remix");
        Assert.Equal(V2PurchaseRowState.SignIn, signIn.State);
        Assert.True(signIn.ShowsBuyButton);
        var signedIn = V2GetItRow.Decide(V2PurchaseRule.FlashesPrizeId, true, _ => false);
        Assert.Equal(V2PurchaseRowState.Unavailable, signedIn.State);
        Assert.False(signedIn.ShowsBuyButton || signedIn.ShowsPrice || signedIn.BuyEnabled);
    }

    [Fact]
    public async Task Flash_card_row_offers_sign_in_then_unavailable_and_hides_when_owned()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (id, offline, seam) = (s.UnifiedId, s.OfflineMode, PrizeOwnership.IsGranted);
            var unified = CoreAccount.UnifiedUserId;
            var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
            var secrets = new System.Collections.Generic.Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            Window? host = null;
            try
            {
                (s.UnifiedId, s.AuthToken, s.OfflineMode) = (null, null, false);
                PrizeOwnership.IsGranted = _ => false;
                var card = new FlashFeatureControl();
                host = new Window { Content = card };
                host.Show();
                var row = card.FindControl<V2GetItRow>("RowGetFlashesV2")!;
                var btn = row.FindControl<Button>("BtnGet")!;
                var status = row.FindControl<TextBlock>("TxtStatus")!;
                Assert.True(card.FindControl<Border>("BoxFlashV2")!.IsVisible);
                Assert.True(row.IsVisible);
                Assert.Equal(Loc.Get("v2_get_flash_blurb"), row.FindControl<TextBlock>("TxtBlurb")!.Text);
                Assert.Equal(Loc.Get("v2_get_signin"), status.Text);
                Assert.Equal(Loc.Get("v2_get_signin_button"), row.FindControl<TextBlock>("TxtGet")!.Text);
                Assert.True(btn.IsVisible && btn.IsEnabled);
                Assert.False(row.FindControl<StackPanel>("PricePlate")!.IsVisible);

                int opened = 0;
                row.OpenLogin = () => { opened++; return Task.CompletedTask; };
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, opened);

                // The live trigger on this head: a sign-in moves CoreAccount.UnifiedUserId (no direct Apply).
                (s.UnifiedId, s.AuthToken) = ("u-test", "t-test");
                Assert.Equal(Loc.Get("v2_get_signin"), status.Text);
                CoreAccount.UnifiedUserId = "u-test";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("v2_get_unavailable"), status.Text);
                Assert.False(btn.IsVisible);
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, opened);

                PrizeOwnership.IsGranted = g => g == PrizeOwnership.FlashDriftBounce;
                row.Apply();
                Assert.False(row.IsVisible);
                Assert.True(row.IsRowHidden);
            }
            finally
            {
                host?.Close();
                CoreAccount.UnifiedUserId = unified;
                (s.UnifiedId, s.OfflineMode, PrizeOwnership.IsGranted) = (id, offline, seam);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
            }
            return Task.CompletedTask;
        });
    }
}
