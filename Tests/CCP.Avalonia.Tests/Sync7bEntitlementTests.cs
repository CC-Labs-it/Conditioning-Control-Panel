using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public class Sync7bEntitlementTests
{
    [Fact]
    public void ReadingRenewsAndRejectsLateLogout()
    {
        var secrets = new System.Collections.Generic.Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var s = CoreSettings.Current;
        s.OfflineMode = false; s.UnifiedId = "sync7b"; s.AuthToken = "test-token";
        s.PatreonTier = 1; s.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(-1);
        CoreAccount.HasPremiumAccessProvider = () => s.PatreonPremiumValidUntil > DateTime.UtcNow;
        CoreAccount.HasLabAccessProvider = () => false;
        var now = DateTime.UtcNow.AddHours(-1);
        var user = new V2AuthService.V2User { EffectiveTierRaw = new JValue(1), PatreonTier = 1,
            PatreonIsActive = true, PatreonConfirmedAtRaw = new JValue(now) };
        CheckoutWatch.NoteOpened("https://patreon.com/checkout");
        Assert.True(EntitlementRefresh.Apply(s, "sync7b", user));
        Assert.Equal(now.AddDays(14), s.PatreonPremiumValidUntil);
        Assert.False(CheckoutWatch.TakeCheck(DateTime.UtcNow));
        Assert.True(EntitlementRefresh.ServerAnsweredThisRun);
        s.AuthToken = null;
        s.PatreonPremiumValidUntil = null;
        Assert.False(EntitlementRefresh.Apply(s, "sync7b", user));
        Assert.Null(s.PatreonPremiumValidUntil);
    }

    [Fact]
    public void ButtonShowsFeedbackAndHidesForPrime()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            CoreSettings.Current.UnifiedId = null; CoreSettings.Current.AuthToken = null;
            CoreAccount.HasPremiumAccessProvider = () => false; CoreAccount.HasLabAccessProvider = () => false;
            var view = new ExclusivesTabView();
            Assert.True(view.FindControl<StackPanel>("JustSubscribedRow")!.IsVisible);
            var button = view.FindControl<Button>("BtnJustSubscribed")!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(button.IsEnabled);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("plans_just_subscribed_signin"),
                view.FindControl<TextBlock>("TxtJustSubscribed")!.Text);
            CoreAccount.HasLabAccessProvider = () => true;
            view.RefreshVault();
            Assert.False(view.FindControl<StackPanel>("JustSubscribedRow")!.IsVisible);
        });
    }
    [Fact]
    public Task CheckoutFocusReadsOnceWithoutProgressionPush() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var s = CoreSettings.Current;
        s.OfflineMode = false; s.UnifiedId = "checkout"; s.AuthToken = "test-token";
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var handler = new ProfileAnswer();
        AccountSeed.NewV2 = () => new V2AuthService(() => s, handler);
        CheckoutWatch.Disarm();
        CheckoutWatch.NoteOpened("https://app.cclabs.app/subscribe");
        EntitlementRefresh.OnActivated(shell: false);
        EntitlementRefresh.OnActivated(shell: false);
        Assert.Equal(1, handler.Reads);
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
        Assert.True(EntitlementRefresh.ServerAnsweredThisRun);
        s.OfflineMode = true;
        CheckoutWatch.Disarm(); CheckoutWatch.NoteOpened("https://patreon.com/checkout");
        EntitlementRefresh.OnActivated(shell: false);
        Assert.Equal(1, handler.Reads);
    });

    private sealed class ProfileAnswer : HttpMessageHandler
    {
        internal int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"user\":{\"effective_tier\":0}}") });
        }
    }

}
