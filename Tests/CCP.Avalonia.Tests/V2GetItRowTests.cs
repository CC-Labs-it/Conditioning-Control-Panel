using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
            var (id, token, offline, seam) = (s.UnifiedId, s.AuthToken, s.OfflineMode, PrizeOwnership.IsGranted);
            var shatter = s.FlashShatterEnabled;
            var unified = CoreAccount.UnifiedUserId;
            var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
            var secrets = new System.Collections.Generic.Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            Window? host = null;
            try
            {
                (s.UnifiedId, s.AuthToken, s.OfflineMode) = (null, null, false);
                PrizeOwnership.IsGranted = PrizeOwnership.Held;
                PrizeOwnership.ResetHeld();
                s.FlashShatterEnabled = true;
                var card = new FlashFeatureControl();
                host = new Window { Content = card };
                host.Show();
                var row = card.FindControl<V2GetItRow>("RowGetFlashesV2")!;
                var btn = row.FindControl<Button>("BtnGet")!;
                var status = row.FindControl<TextBlock>("TxtStatus")!;
                var exit = card.FindControl<ComboBox>("CmbExit")!;
                var note = card.FindControl<TextBlock>("TxtExitShatterNote")!;
                Assert.True(exit.IsEnabled);
                Assert.False(note.IsVisible);
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

                PrizeOwnership.ApplySnapshot("u-test", 1, new[] { PrizeOwnership.FlashDriftBounce });
                Dispatcher.UIThread.RunJobs();
                Assert.False(row.IsVisible);
                Assert.True(row.IsRowHidden);
                Assert.True(card.FindControl<Control>("RowMotion")!.IsVisible);
                // WPF FlashFeatureControl.xaml.cs:75,191-196 refreshes exit state on grants.
                Assert.False(exit.IsEnabled);
                Assert.True(note.IsVisible);
                PrizeOwnership.ApplySnapshot("u-test", 2, Array.Empty<string>());
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.IsVisible);
                Assert.False(card.FindControl<Control>("RowMotion")!.IsVisible);
                Assert.True(exit.IsEnabled);
                Assert.False(note.IsVisible);
                // The 7.2 base also gates Shatter on either motion grant, including Pendulum.
                PrizeOwnership.ApplySnapshot("u-test", 3, new[] { PrizeOwnership.FlashPendulum });
                Dispatcher.UIThread.RunJobs();
                Assert.True(card.FindControl<Control>("RowShatter")!.IsVisible);
                Assert.False(exit.IsEnabled);
                Assert.True(note.IsVisible);
                Assert.False(row.IsVisible);
                PrizeOwnership.ApplySnapshot("u-test", 4, Array.Empty<string>());
                Dispatcher.UIThread.RunJobs();
                Assert.False(card.FindControl<Control>("RowShatter")!.IsVisible);
                Assert.True(exit.IsEnabled);
                Assert.False(note.IsVisible);
                Assert.True(s.FlashShatterEnabled);
                Assert.True(row.IsVisible);
            }
            finally
            {
                host?.Close();
                PrizeOwnership.ResetHeld();
                CoreAccount.UnifiedUserId = unified;
                s.FlashShatterEnabled = shatter;
                (s.UnifiedId, s.AuthToken, s.OfflineMode, PrizeOwnership.IsGranted) = (id, token, offline, seam);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public Task OwnedBrainDrainToggleRemainsHiddenUntilEffectExists() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BubbleMotionPickerTests.Setup();
        var seam = PrizeOwnership.IsGranted;
        Window? host = null;
        try
        {
            PrizeOwnership.IsGranted = id => id == PrizeOwnership.BubbleRain;
            var panel = new BubblePopFeatureControl();
            host = new Window { Content = panel };
            host.Show();
            Assert.True(panel.FindControl<Control>("MotionRow")!.IsVisible);
            Assert.False(panel.FindControl<CheckBox>("ChkBrainDrainBubble")!.IsVisible);
        }
        finally
        {
            host?.Close();
            PrizeOwnership.IsGranted = seam;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BubbleOfferAndBrainDrainToggleAreUnavailableUntilEffectExists() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BubbleMotionPickerTests.Setup();
        var seam = PrizeOwnership.IsGranted;
        Window? host = null;
        try
        {
            PrizeOwnership.IsGranted = _ => false;
            var panel = new BubblePopFeatureControl();
            host = new Window { Content = panel };
            host.Show();
            var row = panel.FindControl<V2GetItRow>("RowGetBubblesV2")!;
            var brain = panel.FindControl<CheckBox>("ChkBrainDrainBubble")!;
            Assert.False(row.IsVisible);
            Assert.False(brain.IsVisible);
            var confirms = 0;
            row.Confirm = (_, _) => { confirms++; return Task.FromResult(true); };
            row.FindControl<Button>("BtnGet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, confirms);
            Assert.Null(row.LastBuy);

            PrizeOwnership.IsGranted = id => id == PrizeOwnership.BubbleRain;
            Task.Run(() => ConditioningControlPanel.Services.Prizes.PrizeGrants.RaiseGrantsChanged()).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.False(row.IsVisible);
            Assert.False(brain.IsVisible);
            Assert.True(panel.FindControl<Control>("MotionRow")!.IsVisible);
        }
        finally
        {
            host?.Close();
            PrizeOwnership.IsGranted = seam;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BubbleOfferIsVisibleAndCanStartPurchase() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        BubbleMotionPickerTests.Setup();
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted, V2PurchaseHead.Service);
        var secrets = new System.Collections.Generic.Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        var proxy = new FakeProxy();
        Window? host = null;
        try
        {
            (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints) = ("bubble-test", "test-token", false, 50);
            CoreAccount.UnifiedUserId = "bubble-test";
            PrizeOwnership.IsGranted = _ => false;
            var svc = V2PurchaseHead.Create(null, "http://127.0.0.1:9", new System.Net.Http.HttpClient(proxy))!;
            V2PurchaseHead.Service = svc;
            proxy.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"sp\":50,\"catalog\":[{\"id\":\"bubbles_v2\",\"priceSp\":30,\"sale\":\"on\"}]}");
            svc.EnsureState();
            await svc.LastBackgroundRead.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(V2PurchaseRowState.Offer, svc.RowFor(V2PurchaseRule.BubblesPrizeId).State);

            var panel = new BubblePopFeatureControl();
            host = new Window { Content = panel };
            host.Show();
            var row = panel.FindControl<V2GetItRow>("RowGetBubblesV2")!;
            Assert.False(row.IsVisible);
            var blockedConfirms = 0;
            row.Confirm = (_, _) => { blockedConfirms++; return Task.FromResult(true); };
            row.FindControl<Button>("BtnGet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, blockedConfirms);
            Assert.Null(row.LastBuy);
            Assert.Single(proxy.Seen);
            // Keep the purchase flow covered for when the effect becomes available.
            row.Configure(V2PurchaseRule.BubblesPrizeId, "v2_get_bubble_blurb", "label_bubbles_v2_box");
            Assert.True(row.IsVisible);
            Assert.True(panel.FindControl<Border>("V2Box")!.IsVisible);
            var confirms = 0;
            row.Confirm = (_, _) => { confirms++; return Task.FromResult(false); };
            row.FindControl<Button>("BtnGet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, confirms);
            Assert.Null(row.LastBuy);
            Assert.Single(proxy.Seen); // declining sends no buy
            row.Confirm = (_, _) => Task.FromResult(true);
            proxy.Reply("{\"ok\":true,\"sp\":20,\"prizes\":{\"revision\":7,\"grants\":[\"fx.bubble.rain\"]}}");
            proxy.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"catalog\":[]}");
            row.FindControl<Button>("BtnGet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await row.LastBuy!.WaitAsync(TimeSpan.FromSeconds(10)));

            PrizeOwnership.IsGranted = id => id == PrizeOwnership.BubbleRain;
            ConditioningControlPanel.Services.Prizes.PrizeGrants.RaiseGrantsChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.True(panel.FindControl<Border>("V2Box")!.IsVisible);
            Assert.True(panel.FindControl<Control>("MotionRow")!.IsVisible);
            Assert.False(row.IsVisible);
        }
        finally
        {
            host?.Close();
            PrizeOwnership.ResetHeld();
            s.AuthToken = null;
            (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId,
                CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted, V2PurchaseHead.Service) = old;
        }
    });

    [Fact]
    public Task VisibleOfferReadsCounterAfterSignInAndHiddenOfferWaitsUntilShown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        BubbleMotionPickerTests.Setup();
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted, V2PurchaseHead.Service);
        var secrets = new System.Collections.Generic.Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        var proxy = new FakeProxy();
        EventHandler? invalidate = null;
        Window? host = null;
        try
        {
            (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints) = (null, null, false, 50);
            CoreAccount.UnifiedUserId = null;
            PrizeOwnership.IsGranted = _ => false;
            var svc = V2PurchaseHead.Create(null, "http://127.0.0.1:9", new System.Net.Http.HttpClient(proxy))!;
            V2PurchaseHead.Service = svc;
            invalidate = (_, _) => svc.Invalidate(); // App.axaml.cs:739
            CoreAccount.UnifiedIdentityChanged += invalidate;
            const string offer = "{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"sp\":50,\"catalog\":[{\"id\":\"flashes_v2\",\"priceSp\":30,\"sale\":\"on\"}]}";
            proxy.Reply(offer);
            var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            svc.Changed += () => { if (proxy.Seen.Count >= 1) firstRead.TrySetResult(); };
            var panel = new FlashFeatureControl();
            host = new Window { Content = panel };
            host.Show();
            var row = panel.FindControl<V2GetItRow>("RowGetFlashesV2")!;
            var button = row.FindControl<Button>("BtnGet")!;
            Assert.True(row.IsEffectivelyVisible);
            Assert.Equal(V2PurchaseRowState.SignIn, row.Row.State);
            Assert.Empty(proxy.Seen);
            row.OpenLogin = () =>
            {
                (s.UnifiedId, s.AuthToken) = ("signed-in", "test-token");
                CoreAccount.UnifiedUserId = "signed-in";
                return Task.CompletedTask;
            };
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await firstRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await svc.LastBackgroundRead.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(proxy.Seen);
            Assert.Equal(V2PurchaseRowState.Offer, row.Row.State);
            Assert.Equal("30", row.FindControl<TextBlock>("TxtPrice")!.Text);
            Assert.True(button.IsVisible && button.IsEnabled);

            panel.IsVisible = false;
            (s.UnifiedId, s.AuthToken) = (null, null);
            CoreAccount.UnifiedUserId = null;
            Dispatcher.UIThread.RunJobs();
            proxy.Reply(offer);
            var secondRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            svc.Changed += () => { if (proxy.Seen.Count >= 2) secondRead.TrySetResult(); };
            (s.UnifiedId, s.AuthToken) = ("another-user", "test-token");
            CoreAccount.UnifiedUserId = "another-user";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(proxy.Seen); // no state read for a hidden panel
            panel.IsVisible = true;
            await secondRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await svc.LastBackgroundRead.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, proxy.Seen.Count);
            Assert.Equal(V2PurchaseRowState.Offer, row.Row.State);
        }
        finally
        {
            if (invalidate != null) CoreAccount.UnifiedIdentityChanged -= invalidate;
            host?.Close();
            s.AuthToken = null;
            (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId,
                CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted, V2PurchaseHead.Service) = old;
        }
    });

    [Fact]
    public Task SigningBackInToTheSameAccountReadsTheCounter() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        BubbleMotionPickerTests.Setup();
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted, V2PurchaseHead.Service);
        var secrets = new System.Collections.Generic.Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        var proxy = new FakeProxy();
        Window? host = null;
        try
        {
            // The id was restored but the token is absent: the row asks to sign in.
            (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints) = ("same-user", null, false, 50);
            CoreAccount.UnifiedUserId = "same-user";
            PrizeOwnership.IsGranted = _ => false;
            var svc = V2PurchaseHead.Create(null, "http://127.0.0.1:9", new System.Net.Http.HttpClient(proxy))!;
            V2PurchaseHead.Service = svc;
            proxy.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"sp\":50,\"catalog\":[{\"id\":\"flashes_v2\",\"priceSp\":30,\"sale\":\"on\"}]}");
            var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            svc.Changed += () => { if (proxy.Seen.Count >= 1) read.TrySetResult(); };
            var panel = new FlashFeatureControl();
            host = new Window { Content = panel };
            host.Show();
            var row = panel.FindControl<V2GetItRow>("RowGetFlashesV2")!;
            Assert.Equal(V2PurchaseRowState.SignIn, row.Row.State);
            // Same id, so no UnifiedIdentityChanged: only the token comes back.
            row.OpenLogin = () => { s.AuthToken = "test-token"; return Task.CompletedTask; };
            row.FindControl<Button>("BtnGet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await read.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await svc.LastBackgroundRead.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(proxy.Seen);
            Assert.Equal(V2PurchaseRowState.Offer, row.Row.State);
            Assert.Equal("30", row.FindControl<TextBlock>("TxtPrice")!.Text);
        }
        finally
        {
            host?.Close();
            s.AuthToken = null;
            (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId,
                CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted, V2PurchaseHead.Service) = old;
        }
    });

    [Fact]
    public Task VisibleOfferReadsNewAccountAfterAnOlderReadFinishes() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        BubbleMotionPickerTests.Setup();
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted, V2PurchaseHead.Service);
        var secrets = new System.Collections.Generic.Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        var proxy = new FakeProxy();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? invalidate = null;
        Window? host = null;
        try
        {
            (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints) = ("first-user", "test-token", false, 100);
            CoreAccount.UnifiedUserId = "first-user";
            PrizeOwnership.IsGranted = _ => false;
            var svc = V2PurchaseHead.Create(null, "http://127.0.0.1:9", new System.Net.Http.HttpClient(proxy))!;
            V2PurchaseHead.Service = svc;
            invalidate = (_, _) => svc.Invalidate();
            CoreAccount.UnifiedIdentityChanged += invalidate;
            proxy.PauseNext = release;
            proxy.Requested += count => { if (count == 1) first.TrySetResult(); };
            proxy.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"catalog\":[{\"id\":\"flashes_v2\",\"priceSp\":30,\"sale\":\"on\"}]}");
            proxy.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":5,\"catalog\":[{\"id\":\"flashes_v2\",\"priceSp\":45,\"sale\":\"on\"}]}");
            svc.Changed += () => { if (proxy.Seen.Count >= 2) second.TrySetResult(); };
            var panel = new FlashFeatureControl();
            host = new Window { Content = panel };
            host.Show();
            await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(proxy.Seen);
            (s.UnifiedId, s.AuthToken) = ("second-user", "test-token");
            CoreAccount.UnifiedUserId = "second-user";
            Dispatcher.UIThread.RunJobs();
            Assert.Single(proxy.Seen); // the first account's request is still in flight
            release.TrySetResult();
            await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await svc.LastBackgroundRead.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            var row = panel.FindControl<V2GetItRow>("RowGetFlashesV2")!;
            Assert.Equal(2, proxy.Seen.Count);
            Assert.Equal(V2PurchaseRowState.Offer, row.Row.State);
            Assert.Equal("45", row.FindControl<TextBlock>("TxtPrice")!.Text);
        }
        finally
        {
            release.TrySetResult();
            if (invalidate != null) CoreAccount.UnifiedIdentityChanged -= invalidate;
            host?.Close();
            s.AuthToken = null;
            (s.UnifiedId, s.OfflineMode, s.SkillPoints, CoreAccount.UnifiedUserId,
                CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, PrizeOwnership.IsGranted, V2PurchaseHead.Service) = old;
        }
    });

    [Fact]
    public Task BubbleCardOffersSignInAndSwitchesToTheOwnedPicker() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        BubbleMotionPickerTests.Setup();
        var settings = CoreSettings.Current;
        var old = (settings.UnifiedId, settings.OfflineMode, CoreAccount.UnifiedUserId,
            CoreSecrets.RetrieveProvider, PrizeOwnership.IsGranted);
        Window? host = null;
        try
        {
            settings.UnifiedId = null;
            settings.OfflineMode = false;
            CoreAccount.UnifiedUserId = null;
            CoreSecrets.RetrieveProvider = _ => null;
            PrizeOwnership.IsGranted = _ => false;
            var panel = new BubblePopFeatureControl();
            host = new Window { Content = panel };
            host.Show();
            var row = panel.FindControl<V2GetItRow>("RowGetBubblesV2")!;
            Assert.False(row.IsVisible);
            // Exercise the sign-in flow under the future enabled configuration.
            row.Configure(V2PurchaseRule.BubblesPrizeId, "v2_get_bubble_blurb", "label_bubbles_v2_box");
            var box = panel.FindControl<Border>("V2Box")!;
            var motion = panel.FindControl<Control>("MotionRow")!;
            Assert.True(box.IsVisible);
            Assert.True(row.IsVisible);
            Assert.False(motion.IsVisible);
            Assert.Equal(Loc.Get("v2_get_bubble_blurb"), row.FindControl<TextBlock>("TxtBlurb")!.Text);
            Assert.Equal(V2PurchaseRowState.SignIn, row.Row.State);
            var opened = 0;
            row.OpenLogin = () => { opened++; return Task.CompletedTask; };
            row.FindControl<Button>("BtnGet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);

            PrizeOwnership.IsGranted = id => id == PrizeOwnership.BubbleRain;
            ConditioningControlPanel.Services.Prizes.PrizeGrants.RaiseGrantsChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.False(row.IsVisible);
            Assert.True(box.IsVisible);
            Assert.True(motion.IsVisible);
            Assert.Contains(panel.FindControl<ComboBox>("CmbMotion")!.Items.OfType<ComboBoxItem>(),
                item => item.Tag is ConditioningControlPanel.Models.BubbleMotionStyle.Rain);
        }
        finally
        {
            host?.Close();
            (settings.UnifiedId, settings.OfflineMode, CoreAccount.UnifiedUserId,
                CoreSecrets.RetrieveProvider, PrizeOwnership.IsGranted) = old;
        }
        return Task.CompletedTask;
    });

    /// <summary>Row feat-backroom-counter-buy: the startup-built service over a fake proxy. The row reads
    /// the counter when shown, Get it confirms at the shown price, buys, toasts, adopts the receipt's
    /// SP and hides once the reply's grants land; a refusal shows WPF's key. Never reaches a server.</summary>
    [Fact]
    public async Task Get_it_reads_the_counter_confirms_buys_toasts_and_hides()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var unified = CoreAccount.UnifiedUserId;
            var (id, token, offline, sp, seam) = (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints, PrizeOwnership.IsGranted);
            var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
            var secrets = new System.Collections.Generic.Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            var h = new FakeProxy();
            Window? host = null;
            try
            {
                (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints) = ("u-buy", "t-buy", false, 50);
                CoreAccount.UnifiedUserId = "u-buy";
                PrizeOwnership.IsGranted = g => PrizeOwnership.Held(g);
                PrizeOwnership.ResetHeld();
                var svc = V2PurchaseHead.Create(null, "http://127.0.0.1:9", new System.Net.Http.HttpClient(h))!;
                V2PurchaseHead.Service = svc;
                h.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"sp\":50,\"catalog\":[{\"id\":\"flashes_v2\",\"priceSp\":30,\"sale\":\"on\"}]}");
                var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // Posted, so it queues behind the row's own repaint (the row subscribes on attach, below).
                svc.Changed += () => Dispatcher.UIThread.Post(() => read.TrySetResult());
                var toasts = new StackPanel();
                global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(toasts);

                var card = new FlashFeatureControl();
                host = new Window { Content = card };
                host.Show();
                await read.Task.WaitAsync(TimeSpan.FromSeconds(10));   // the read the shown row started
                Dispatcher.UIThread.RunJobs();
                var row = card.FindControl<V2GetItRow>("RowGetFlashesV2")!;
                var btn = row.FindControl<Button>("BtnGet")!;
                Assert.Equal(V2PurchaseRowState.Offer, row.Row.State);
                Assert.True(row.FindControl<StackPanel>("PricePlate")!.IsVisible);
                Assert.Equal(30.ToString(System.Globalization.CultureInfo.CurrentCulture), row.FindControl<TextBlock>("TxtPrice")!.Text);
                Assert.Equal(Loc.Get("v2_get_button"), row.FindControl<TextBlock>("TxtGet")!.Text);
                Assert.True(btn.IsVisible && btn.IsEnabled);

                // A refusal: insufficient -> WPF's key, the row stays and offers again.
                string? asked = null;
                row.Confirm = (t, b) => { asked = t + "|" + b; return Task.FromResult(true); };
                h.Reply("{\"ok\":false,\"reason\":\"insufficient\"}", System.Net.HttpStatusCode.PaymentRequired);
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await row.LastBuy!.WaitAsync(TimeSpan.FromSeconds(10)) == false);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("v2_get_confirm_title") + "|" + Loc.GetF("v2_get_confirm_body", 30, Loc.Get("section_flash_v2")), asked);
                Assert.Equal(Loc.Get("v2_get_error_sp"), row.FindControl<TextBlock>("TxtStatus")!.Text);
                Assert.True(btn.IsEnabled);

                // Declining the confirm sends nothing.
                row.Confirm = (_, _) => Task.FromResult(false);
                var sent = h.Seen.Count;
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(sent, h.Seen.Count);

                row.Confirm = (_, _) => Task.FromResult(true);
                h.Reply("{\"ok\":true,\"sp\":20,\"prizes\":{\"revision\":7,\"grants\":[\"fx.flash.drift_bounce\"]}}");
                h.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"catalog\":[]}");   // the post-buy refresh
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await row.LastBuy!.WaitAsync(TimeSpan.FromSeconds(10)));
                Dispatcher.UIThread.RunJobs();
                Assert.False(row.IsVisible);
                // WPF rebuilds the motion picker on GrantsChanged: the bought Drift & Bounce shows at once.
                Assert.True(card.FindControl<Border>("BoxFlashV2")!.IsVisible);
                Assert.True(card.FindControl<Control>("RowMotion")!.IsVisible);
                Assert.Contains(card.FindControl<ComboBox>("CmbMotion")!.Items.OfType<ComboBoxItem>(),
                    i => i.Tag is ConditioningControlPanel.Models.FlashMotionStyle.DriftBounce);
                Assert.Equal(20, s.SkillPoints);
                Assert.Contains(toasts.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.Text == Loc.GetF("v2_get_toast_done", Loc.Get("section_flash_v2")));
            }
            finally
            {
                host?.Close();
                V2PurchaseHead.Service = null;
                CoreAccount.UnifiedUserId = unified;
                PrizeOwnership.ResetHeld();
                (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints, PrizeOwnership.IsGranted) = (id, token, offline, sp, seam);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
            }
        });
    }

    [Fact]
    public async Task Snapshot_repaints_without_a_counter_and_detach_unsubscribes()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (id, token, offline, seam) = (s.UnifiedId, s.AuthToken, s.OfflineMode, PrizeOwnership.IsGranted);
            var unified = CoreAccount.UnifiedUserId;
            var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
            var secrets = new System.Collections.Generic.Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            Window? host = null;
            try
            {
                (s.UnifiedId, s.AuthToken, s.OfflineMode) = (null, null, false);
                PrizeOwnership.IsGranted = PrizeOwnership.Held;
                PrizeOwnership.ResetHeld();
                var row = new V2GetItRow();
                row.Configure(V2PurchaseRule.BubblesPrizeId, "v2_get_bubble_blurb", "label_bubbles_v2_box");
                host = new Window { Content = row };
                host.Show();
                Assert.True(row.IsVisible);
                Assert.Equal(Loc.Get("v2_get_bubble_blurb"), row.FindControl<TextBlock>("TxtBlurb")!.Text);
                Assert.Equal(V2PurchaseRowState.SignIn, row.Row.State);
                int opened = 0;
                row.OpenLogin = () => { opened++; return Task.CompletedTask; };
                row.FindControl<Button>("BtnGet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, opened);

                (s.UnifiedId, s.AuthToken) = ("u-bubbles", "t-bubbles");
                CoreAccount.UnifiedUserId = "u-bubbles";   // ownership binds to the session account (WPF App.UnifiedUserId)
                row.Apply();
                Assert.Equal(V2PurchaseRowState.Unavailable, row.Row.State);
                PrizeOwnership.ApplySnapshot("u-bubbles", 1, new[] { PrizeOwnership.BubbleRain });
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.IsRowHidden);
                Assert.False(row.IsVisible);
                // A revoke restores the offer, without a purchase service or direct Apply.
                PrizeOwnership.ApplySnapshot("u-bubbles", 2, Array.Empty<string>());
                Dispatcher.UIThread.RunJobs();
                Assert.False(row.IsRowHidden);
                Assert.True(row.IsVisible);
                host.Content = null;
                var state = row.Row;
                PrizeOwnership.ApplySnapshot("u-bubbles", 3, new[] { PrizeOwnership.BubbleRain });
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(state, row.Row); // detached rows no longer listen
            }
            finally
            {
                host?.Close();
                CoreAccount.UnifiedUserId = unified;
                PrizeOwnership.ResetHeld();
                (s.UnifiedId, s.AuthToken, s.OfflineMode, PrizeOwnership.IsGranted) = (id, token, offline, seam);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>Scripted proxy: one reply per request, in order; never a socket.</summary>
    private sealed class FakeProxy : System.Net.Http.HttpMessageHandler
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Seen = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(string Json, System.Net.HttpStatusCode Code)> _replies = new();
        public TaskCompletionSource? PauseNext;
        public event Action<int>? Requested;
        public void Reply(string json, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.OK) => _replies.Enqueue((json, code));
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage r, System.Threading.CancellationToken ct)
        {
            Seen.Enqueue(r.RequestUri!.AbsolutePath);
            if (!_replies.TryDequeue(out var next)) throw new System.Net.Http.HttpRequestException("no scripted reply");
            var pause = PauseNext;
            PauseNext = null;
            Requested?.Invoke(Seen.Count);
            if (pause != null) await pause.Task.WaitAsync(ct);
            return new System.Net.Http.HttpResponseMessage(next.Code)
                { Content = new System.Net.Http.StringContent(next.Json, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
