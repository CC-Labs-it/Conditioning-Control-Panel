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
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class JustSubscribedTests
{
    private sealed class ProfileWire : HttpMessageHandler
    {
        public readonly TaskCompletionSource<HttpResponseMessage> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v2/user/profile", request.RequestUri!.AbsolutePath);
            Reads++;
            return Reply.Task;
        }
        public void Answer(string json, HttpStatusCode status = HttpStatusCode.OK) =>
            Reply.SetResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
    }

    [Fact]
    public Task BothVaultsCheckAndReportTheRealProfileRead() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var settings = new SettingsService();
        var s = settings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.MotionLevel = MotionLevel.Off;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s));
        var oldFactory = AccountSeed.NewV2;
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            shell.Show();
            shell.ShowTab("premium");
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var row = view.FindControl<Control>("JustSubscribedRow")!;
            var button = view.FindControl<Button>("BtnJustSubscribed")!;
            var text = view.FindControl<TextBlock>("TxtJustSubscribed")!;
            Assert.True(row.IsVisible);
            s.PatreonLabValidUntil = DateTime.UtcNow.AddDays(1);
            view.RefreshJustSubscribed();
            Assert.False(row.IsVisible);
            s.PatreonLabValidUntil = null;
            view.RefreshJustSubscribed();
            Assert.True(row.IsVisible);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Loc.Get("plans_just_subscribed_signin"), text.Text);
            Assert.True(button.IsEnabled);

            s.UnifiedId = "test-user"; s.AuthToken = "test-token";
            var wire = new ProfileWire();
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, wire.Reads);
            Assert.False(button.IsEnabled);
            Assert.Equal(Loc.Get("plans_just_subscribed_checking"), text.Text);
            wire.Answer("{\"user\":{\"effective_tier\":2}}");
            await Until(() => button.IsEnabled);
            Assert.Equal(Loc.Get("plans_just_subscribed_prime"), text.Text);
            Assert.True(CoreEntitlement.HasLab);
            Assert.True(row.IsVisible); // the answer stays readable until leaving the page
            shell.ShowTab("settings");
            shell.ShowTab("premium");
            Assert.False(row.IsVisible);

            // Account & Plans uses the same tier rule on entry, including when it changes between visits.
            s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
            shell.OpenAppSettingsSection("account");
            Dispatcher.UIThread.RunJobs();
            var plans = shell.PlansVaultView!;
            Assert.True(plans.FindControl<Control>("JustSubscribedRow")!.IsVisible);
            s.UnifiedId = null; s.AuthToken = null;
            plans.FindControl<Button>("BtnJustSubscribed")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Loc.Get("plans_just_subscribed_signin"), plans.FindControl<TextBlock>("TxtJustSubscribed")!.Text);
        }
        finally
        {
            AccountSeed.NewV2 = oldFactory;
            shell.Close();
            CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public Task ProfileReadPreservesGraceAndReportsFailuresAndNewAccess() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s));
        var oldFactory = AccountSeed.NewV2;
        try
        {
            s.UnifiedId = "test-user"; s.AuthToken = "test-token";
            var wire = new ProfileWire();
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            wire.Answer("{\"user\":{\"effective_tier\":1}}");
            var notices = 0;
            AccountSeed.Patreon!.TierChanged += (_, _) => notices++;
            CheckoutWatch.NoteOpened("https://cclabs.app/subscribe");
            Assert.Equal(JustSubscribedOutcome.BasicOn, await AccountSeed.CheckSubscriptionNowAsync());
            Assert.False(CheckoutWatch.IsArmed);
            Assert.True(CoreEntitlement.HasPremium);
            Assert.Equal(1, notices);
            Assert.True(s.PatreonPremiumValidUntil > DateTime.UtcNow.AddDays(13));

            wire = new ProfileWire();
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            wire.Answer("{\"user\":{\"effective_tier\":0}}");
            Assert.Equal(JustSubscribedOutcome.NotYet, await AccountSeed.CheckSubscriptionNowAsync());
            Assert.True(CoreEntitlement.HasPremium);
            Assert.Equal(1, s.PatreonTier);
            Assert.Equal(0, s.ServerTierSeen);

            wire = new ProfileWire();
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            wire.Answer("{}", HttpStatusCode.ServiceUnavailable);
            Assert.Equal(JustSubscribedOutcome.Unreachable, await AccountSeed.CheckSubscriptionNowAsync());
            s.OfflineMode = true;
            Assert.Equal(JustSubscribedOutcome.Unreachable, await AccountSeed.CheckSubscriptionNowAsync());
        }
        finally { AccountSeed.NewV2 = oldFactory; CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task LateProfileCannotGrantTheNextAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s));
        var oldFactory = AccountSeed.NewV2;
        try
        {
            s.UnifiedId = "first"; s.AuthToken = "first-token";
            var wire = new ProfileWire();
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            var check = AccountSeed.CheckSubscriptionNowAsync();
            Assert.Equal(JustSubscribedOutcome.Unreachable, await AccountSeed.CheckSubscriptionNowAsync());
            Assert.Equal(1, wire.Reads);
            s.UnifiedId = "second"; s.AuthToken = "second-token";
            wire.Answer("{\"user\":{\"effective_tier\":2}}");
            Assert.Equal(JustSubscribedOutcome.Unreachable, await check);
            Assert.Equal(0, s.PatreonTier);
            Assert.False(CoreEntitlement.HasLab);
        }
        finally { AccountSeed.NewV2 = oldFactory; CoreSettings.ServiceProvider = null; }
    });

    [Fact]
    public Task PatreonValidationIsForcedAndInviteWeekIsNotPaid() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var token = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = "test", RefreshToken = "test", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        var secrets = new Dictionary<string, string?> { ["patreon_auth"] = token };
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var handler = new ValidationWire();
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s, p == "patreon" ? handler : null));
        var oldFactory = AccountSeed.NewV2;
        try
        {
            s.UnifiedId = "test-user"; s.AuthToken = "test-token";
            AccountSeed.NewV2 = () => throw new InvalidOperationException("profile unavailable");
            s.InviteGrantUntil = DateTime.UtcNow.AddDays(1);
            Assert.True(CoreEntitlement.HasPremium);
            Assert.Equal(0, AccountSeed.EffectivePaidTier());
            Assert.Equal(JustSubscribedOutcome.BasicOn, await AccountSeed.CheckSubscriptionNowAsync());
            Assert.Equal(1, handler.Reads);
            Assert.Equal(1, AccountSeed.EffectivePaidTier());
        }
        finally { AccountSeed.NewV2 = oldFactory; CoreSettings.ServiceProvider = null; }
    });

    private sealed class ValidationWire : HttpMessageHandler
    {
        public int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("/patreon/validate", request.RequestUri!.AbsolutePath);
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"is_active\":true,\"tier\":1}") });
        }
    }

    [Fact]
    public Task FreshPatreonConfirmationRestampsGraceAndAppliesExtras() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService();
        var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s));
        var oldFactory = AccountSeed.NewV2;
        try
        {
            s.UnifiedId = "test-user"; s.AuthToken = "test-token";
            s.PatreonTier = 1; s.PatreonMember = false; // fresh active stamp reopens expired grace
            var wire = new ProfileWire();
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            var confirmed = DateTime.UtcNow.AddHours(-1);
            wire.Answer($"{{\"user\":{{\"effective_tier\":1,\"patreon_is_active\":true,\"patreon_tier\":1,\"patreon_confirmed_at\":\"{confirmed:o}\",\"patreon_member\":true}}}}");
            var notices = 0;
            AccountSeed.Patreon!.TierChanged += (_, _) => notices++;
            Assert.Equal(JustSubscribedOutcome.BasicOn, await AccountSeed.CheckSubscriptionNowAsync());
            Assert.True(s.PatreonPremiumValidUntil > DateTime.UtcNow.AddDays(13));
            Assert.True(s.PatreonMember);
            Assert.Equal(1, notices);
        }
        finally { AccountSeed.NewV2 = oldFactory; CoreSettings.ServiceProvider = null; }
    });

    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++) await Task.Delay(10);
        Assert.True(done());
    }
}
