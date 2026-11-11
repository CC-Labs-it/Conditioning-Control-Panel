using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LapseInboxTests
{
    private static Task Run(Func<MainShellWindow, StartupLadder.TestClock, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        var oldReconnect = TierGate.ReconnectIsTheAnswerProvider;
        var oldLaunch = VaultGateDialog.Launch;
        var oldAnswer = AccountSeed.TierAnswerAccount;
        var oldFocus = AccountSeed.LastFocusUtc;
        var oldCheckout = AccountSeed.CheckoutUntilUtc;
        var oldCheckoutCheck = AccountSeed.LastCheckoutUtc;
        var patreonProperty = typeof(AccountSeed).GetProperty("Patreon", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var substarProperty = typeof(AccountSeed).GetProperty("SubscribeStar", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldPatreon = AccountSeed.Patreon;
        var oldSubstar = AccountSeed.SubscribeStar;
        var oldLifetime = Application.Current!.ApplicationLifetime;
        var lifetimeField = typeof(Application).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => typeof(IApplicationLifetime).IsAssignableFrom(f.FieldType));
        MainShellWindow? shell = null;
        var secrets = new Dictionary<string, string?>();
        try
        {
            CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            var service = new SettingsService();
            var s = service.Current;
            s.Welcomed = s.HasAcceptedAgeVerification = true;
            s.WhatMovedCardShown = 1;
            s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            CoreSettings.ServiceProvider = () => service;
            s.UnifiedId = "lapse-test"; s.AuthToken = "token"; s.OfflineMode = true;
            s.PatreonTier = 2; s.ServerTierSeen = 0;
            AccountSeed.TierAnswerAccount = null;
            AccountSeed.LastFocusUtc = DateTime.MinValue;
            AccountSeed.CheckoutUntilUtc = AccountSeed.LastCheckoutUtc = DateTime.MinValue;
            patreonProperty.SetValue(null, new ProviderSubscription("patreon", () => s));
            substarProperty.SetValue(null, new ProviderSubscription("substar", () => s));
            TierGate.ReconnectIsTheAnswerProvider = () => false;
            StartupLadder.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromDays(2));
            var clock = (StartupLadder.TestClock)StartupLadder.Time;
            s.PatreonLabValidUntil = clock.GetUtcNow().UtcDateTime.AddDays(2);
            var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            lifetimeField.SetValue(Application.Current, lifetime);
            shell = new MainShellWindow();
            lifetime.MainWindow = shell;
            shell.Show();
            await body(shell, clock);
        }
        finally
        {
            InboxNoticeCard.Open?.Close();
            shell?.Close();
            lifetimeField.SetValue(Application.Current, oldLifetime);
            StartupLadder.ResetForTests();
            patreonProperty.SetValue(null, oldPatreon);
            substarProperty.SetValue(null, oldSubstar);
            AccountSeed.TierAnswerAccount = oldAnswer;
            AccountSeed.LastFocusUtc = oldFocus;
            AccountSeed.CheckoutUntilUtc = oldCheckout;
            AccountSeed.LastCheckoutUtc = oldCheckoutCheck;
            TierGate.ReconnectIsTheAnswerProvider = oldReconnect;
            VaultGateDialog.Launch = oldLaunch;
            CoreSettings.ServiceProvider = oldSettings;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
        }
    });

    [Fact]
    public Task CachedReadingDoesNotPost_ThenWarningIsStampedAndOpensPrimeCheckout() => Run((shell, clock) =>
    {
        shell.MaybePostLapseRow();
        Assert.Empty(StartupLadder.Inbox.Items);
        Assert.Empty(CoreSettings.Current.InboxStamps);
        AccountSeed.ApplyTier("lapse-test", "token", 1);
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal("conv:lapse_warn_3", row.Key);
        Assert.Equal(Loc.GetF("conv_lapse_warn_prime", 2), row.Title);
        Assert.Equal(clock.GetUtcNow().AddDays(2).ToString("yyyy-MM-dd"), CoreSettings.Current.InboxStamps["lapse_warn_3"]);
        shell.MaybePostLapseRow();
        Assert.Single(StartupLadder.Inbox.Items);
        string? url = null;
        VaultGateDialog.Launch = value => { Assert.Null(InboxNoticeCard.Open); url = value; return true; };
        StartupLadder.Inbox.Open(row);
        var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
        var button = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Primary");
        Assert.Equal(Loc.Get("conv_keep_prime"), button.Content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=27681505", url);
        shell.MaybePostLapseRow();
        Assert.Empty(StartupLadder.Inbox.Items);
        return Task.CompletedTask;
    });

    [Fact]
    public Task LiveTierSuppressesAndClosedRowAppearsAfterCoverageEnds() => Run((shell, clock) =>
    {
        AccountSeed.Patreon!.SetWhitelistStatus(true);
        AccountSeed.ApplyTier("lapse-test", "token", 0);
        Assert.Empty(StartupLadder.Inbox.Items);
        Assert.Empty(CoreSettings.Current.InboxStamps);
        typeof(AccountSeed).GetProperty("Patreon", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(null, new ProviderSubscription("patreon", () => CoreSettings.Current));
        CoreSettings.Current.PatreonLabValidUntil = clock.GetUtcNow().UtcDateTime.AddHours(-1);
        shell.MaybePostLapseRow();
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal("conv:lapse_closed", row.Key);
        Assert.Equal(Loc.Get("conv_lapse_closed_prime_today"), row.Title);
        Assert.Equal(Loc.Get("conv_lapse_closed_body"), row.Summary);
        string? url = null;
        VaultGateDialog.Launch = value => { url = value; return true; };
        StartupLadder.Inbox.Open(row);
        var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
        var button = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Primary");
        Assert.Equal(Loc.Get("conv_reopen_prime"), button.Content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("https://www.patreon.com/checkout/CodeBambi?rid=27681505", url);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReconnectReplacesLapseAndIsStampedDaily() => Run((shell, clock) =>
    {
        var calls = 0;
        shell.ReconnectFromGateOverride = () => { calls++; return Task.CompletedTask; };
        TierGate.ReconnectIsTheAnswerProvider = () => true;
        AccountSeed.ApplyTier("lapse-test", "token", 0);
        var row = Assert.Single(StartupLadder.Inbox.Items);
        Assert.Equal("conv:lapse_reconnect", row.Key);
        Assert.Equal(clock.GetLocalNow().ToString("yyyy-MM-dd"), CoreSettings.Current.InboxStamps["lapse_reconnect"]);
        Assert.False(CoreSettings.Current.InboxStamps.ContainsKey("lapse_warn_3"));
        StartupLadder.Inbox.Open(row);
        var button = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open).GetVisualDescendants().OfType<Button>().Single(b => b.Name == "Primary");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, calls);
        shell.MaybePostLapseRow();
        Assert.Empty(StartupLadder.Inbox.Items);
        clock.Advance(TimeSpan.FromDays(1));
        shell.MaybePostLapseRow();
        Assert.Single(StartupLadder.Inbox.Items);
        return Task.CompletedTask;
    });
    [Fact]
    public Task SubscribeStarCoversBasicButNotPrime() => Run((shell, clock) =>
    {
        var s = CoreSettings.Current;
        s.PatreonTier = 1;
        s.PatreonPremiumValidUntil = clock.GetUtcNow().UtcDateTime.AddDays(2);
        AccountSeed.SubscribeStar!.SetWhitelistStatus(true);
        AccountSeed.ApplyTier("lapse-test", "token", 0);
        Assert.Empty(StartupLadder.Inbox.Items);
        Assert.Empty(s.InboxStamps);
        s.PatreonTier = 2;
        shell.MaybePostLapseRow();
        Assert.Equal("conv:lapse_warn_3", Assert.Single(StartupLadder.Inbox.Items).Key);
        return Task.CompletedTask;
    });

    [Fact]
    public Task RiseClearsLowerReadingAndNotifiesProvider() => Run((shell, clock) =>
    {
        var s = CoreSettings.Current;
        s.PatreonTier = 1;
        var calls = 0;
        AccountSeed.Patreon!.TierChanged += Changed;
        void Changed(object? sender, ConditioningControlPanel.Models.PatreonTier tier) { if (tier == ConditioningControlPanel.Models.PatreonTier.Level2) calls++; }
        try
        {
            AccountSeed.ApplyTier("lapse-test", "token", 2);
            Assert.Equal(2, s.PatreonTier);
            Assert.Null(s.ServerTierSeen);
            Assert.Equal(1, calls);
            Assert.Empty(StartupLadder.Inbox.Items);
        }
        finally { AccountSeed.Patreon!.TierChanged -= Changed; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FocusRefreshPostsAfterServerAnswer() => Run(async (shell, clock) =>
    {
        var oldClient = AccountSeed.NewV2;
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Applied() => answered.TrySetResult();
        AccountSeed.TierReadingApplied += Applied;
        try
        {
            var s = CoreSettings.Current;
            s.OfflineMode = false;
            AccountSeed.NewV2 = () => new V2AuthService(() => s, new TierWire());
            AccountSeed.OnFocus();
            await answered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("conv:lapse_warn_3", Assert.Single(StartupLadder.Inbox.Items).Key);
            Assert.Equal(1, s.ServerTierSeen);
        }
        finally { AccountSeed.TierReadingApplied -= Applied; AccountSeed.NewV2 = oldClient; }
    });

    [Fact]
    public Task CheckoutReturnRefreshesEvenInsideTheOrdinaryFocusGap() => Run(async (shell, clock) =>
    {
        var oldClient = AccountSeed.NewV2;
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Applied() => answered.TrySetResult();
        AccountSeed.TierReadingApplied += Applied;
        var otherWindow = new Window();
        try
        {
            var s = CoreSettings.Current;
            AccountSeed.NewV2 = () => new V2AuthService(() => s, new TierWire());
            otherWindow.Show();
            shell.Activate();
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();
            s.OfflineMode = false;
            AccountSeed.LastFocusUtc = DateTime.UtcNow;
            AccountSeed.NoteCheckout();
            otherWindow.Activate();
            await answered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("conv:lapse_warn_3", Assert.Single(StartupLadder.Inbox.Items).Key);
        }
        finally { otherWindow.Close(); AccountSeed.TierReadingApplied -= Applied; AccountSeed.NewV2 = oldClient; }
    });

    [Fact]
    public Task HeartbeatResponsePostsThroughTheShell() => Run(async (shell, clock) =>
    {
        var oldLoggedIn = CoreAccount.IsLoggedInProvider;
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Applied() => answered.TrySetResult();
        AccountSeed.TierReadingApplied += Applied;
        try
        {
            CoreSettings.Current.OfflineMode = false;
            CoreAccount.IsLoggedInProvider = () => true;
            var sync = new SyncPush(() => null, () => false, new TierWire()) { HeartbeatTier = AccountSeed.OfferTier };
            Assert.True(await sync.HeartbeatAsync());
            await answered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("conv:lapse_warn_3", Assert.Single(StartupLadder.Inbox.Items).Key);
        }
        finally { AccountSeed.TierReadingApplied -= Applied; CoreAccount.IsLoggedInProvider = oldLoggedIn; }
    });

    private sealed class TierWire : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Method == HttpMethod.Get ? "{\"user\":{\"effective_tier\":1}}" : "{\"effective_tier\":1}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

}
