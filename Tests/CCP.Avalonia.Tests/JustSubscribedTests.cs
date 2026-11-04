using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Linq;
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
    [Theory, InlineData(false), InlineData(true)] public Task BothVaultsCheckAndReportTheRealProfileRead(bool intakeOpen) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var settings = new SettingsService(); var s = settings.Current;
        s.Welcomed = s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        foreach (var key in FeatureIntros.All.Keys) if (!s.SeenFeatureIntros.Contains(key)) s.SeenFeatureIntros.Add(key);
        s.SeenFeatureIntros.Add(TierCelebration.LegacyKey);
        s.SeenFeatureIntros.Remove(TierCelebration.KeyT2);
        StartupLadder.ResetForTests(); FeatureIntroPopup.ResetForTests();
        s.MotionLevel = MotionLevel.Off;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key); CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s));
        var oldFactory = AccountSeed.NewV2;
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            shell.Show(); shell.ShowTab("premium");
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
            var wire = new ProfileWire(); bool sparkBurst = false; AccountSeed.Patreon!.TierChanged += (_, _) => sparkBurst = shell.Named<global::ConditioningControlPanel.Avalonia.Controls.PremiumSpark>("HeaderPremiumSpark")!.Field.Alive.Any(m => m.Kind == global::ConditioningControlPanel.Controls.Header.MoteKind.Burst);
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, wire.Reads); Assert.False(button.IsEnabled);
            Assert.Equal(Loc.Get("plans_just_subscribed_checking"), text.Text);
            s.PerformanceMode = false; s.AutoPerformanceMode = false; s.GentleVisuals = false; s.MotionLevel = MotionLevel.Full;
            shell.Activate();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(1));
            wire.Answer("{\"user\":{\"effective_tier\":2}}");
            await Until(() => button.IsEnabled);
            await Until(() => s.SeenFeatureIntros.Contains(TierCelebration.KeyT2));
            Assert.Equal(1, shell._profileBubbleBurst.Count);
            Assert.True(sparkBurst);
            Assert.Equal(2, s.LastKnownEffectiveTier);
            var ring = shell.Named<global::Avalonia.Controls.Shapes.Ellipse>("ProfileBubbleGlowRing")!;
            Assert.Equal("#ff5ee6ff", ((global::Avalonia.Media.ISolidColorBrush)ring.Stroke!).Color.ToString().ToLowerInvariant());
            s.MotionLevel = MotionLevel.Off;
            Assert.Equal(Loc.Get("plans_just_subscribed_prime"), text.Text);
            Assert.True(CoreEntitlement.HasLab);
            Assert.True(row.IsVisible); // the answer stays readable until leaving the page
            shell.ShowTab("settings");
            shell.ShowTab("premium");
            Assert.False(row.IsVisible);
            foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
            StartupLadder.ResetForTests(); s.SeenFeatureIntros.Remove(TierCelebration.KeyT2); s.SeenFeatureIntros.Remove(TierCelebration.LegacyKey); s.PatreonTier = 0; s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = null;
            Window game = intakeOpen ? new IntakeHostWindow() : new global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow(); game.Show();
            wire = new ProfileWire(); var gameCheck = AccountSeed.CheckSubscriptionNowAsync(); wire.Answer("{\"user\":{\"effective_tier\":2}}"); await gameCheck; await Until(() => StartupLadder.Inbox.Items.Any(item => item.Key == "intro:" + TierCelebration.KeyT2));
            Assert.Contains(StartupLadder.Inbox.Items, item => item.Key == "intro:" + TierCelebration.KeyT2);
            Assert.DoesNotContain(TierCelebration.KeyT2, s.SeenFeatureIntros);
            s.SeenFeatureIntros.Add(TierCelebration.KeyT2); s.LastKnownEffectiveTier = 0;
            shell.MaybeShowPremiumCelebration(); Assert.Equal(2, s.LastKnownEffectiveTier);
            var notice = Assert.Single(StartupLadder.Inbox.Items, item => item.Key == "tier-on:2"); game.Close(); StartupLadder.Inbox.Open(notice); var dialog = Assert.Single(shell.OwnedWindows.OfType<global::ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>(), d => d.Title == Loc.Get("tier_on_prime")); var clock = (StartupLadder.TestClock)StartupLadder.Time; clock.Advance(TimeSpan.FromSeconds(4)); bool nextOpened = false;
            StartupLadder.PresentOrInbox(new ConditioningControlPanel.Services.Startup.InboxItem { Key = "after-notice", Open = () => nextOpened = true });
            clock.Advance(TimeSpan.FromSeconds(4)); Assert.False(nextOpened);
            dialog.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.False(dialog.IsVisible);
            clock.Advance(TimeSpan.FromSeconds(1)); Assert.True(nextOpened);
            // The Settings page refreshes the inline row even while another section is selected.
            shell.OpenAppSettingsSection("general");
            shell.ShowTab("settings");
            s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
            shell.ShowTab("appsettings");
            Assert.Equal("general", shell.Named<AppSettingsTabView>("AppSettingsTab")!.CheckedSectionKey);
            Assert.True(shell.PlansVaultView!.FindControl<Control>("JustSubscribedRow")!.IsVisible);
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
            foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
            global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow.CloseAllForPanic(); shell.Close();
            CoreSettings.ServiceProvider = null;
        }
    });
    [Theory, InlineData("intake", true), InlineData("fyp", false), InlineData("loom", false)]
    public Task GameParkingDistinguishesIntakeFromFeedAndLoom(string id, bool parked) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        StartupLadder.ResetForTests();
        Window host = id == "intake" ? new IntakeHostWindow() : new global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow(global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow.Games[id]);
        try { host.Show(); Assert.Equal(parked, StartupLadder.World(DateTime.UtcNow).GameHostUp); }
        finally { host.Close(); StartupLadder.ResetForTests(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task ProfileReadPreservesGraceAndReportsFailuresAndNewAccess() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key); CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
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
            wire = new ProfileWire(); AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            wire.Answer("{\"user\":{\"effective_tier\":0}}");
            Assert.Equal(JustSubscribedOutcome.NotYet, await AccountSeed.CheckSubscriptionNowAsync());
            Assert.True(CoreEntitlement.HasPremium);
            Assert.Equal(1, s.PatreonTier);
            Assert.Equal(0, s.ServerTierSeen);
            wire = new ProfileWire(); AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            wire.Answer("{}", HttpStatusCode.ServiceUnavailable);
            Assert.Equal(JustSubscribedOutcome.Unreachable, await AccountSeed.CheckSubscriptionNowAsync());
            s.OfflineMode = true;
            Assert.Equal(JustSubscribedOutcome.Unreachable, await AccountSeed.CheckSubscriptionNowAsync());
        }
        finally { AccountSeed.NewV2 = oldFactory; CoreSettings.ServiceProvider = null; }
    });
    [Fact]
    public Task LapseReadingIsPublishedWhenStoredAndCleared() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); var s = settings.Current;
        s.PatreonTier = 1; s.ServerTierSeen = null; s.OfflineMode = false;
        s.UnifiedId = "test-user";
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key); CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        s.AuthToken = "test-token";
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s));
        var oldFactory = AccountSeed.NewV2;
        var readings = new List<int?>();
        void OnReading() => readings.Add(s.ServerTierSeen);
        AccountSeed.SubscriptionReadingApplied += OnReading;
        try
        {
            foreach (var tier in new[] { 0, 0, 1 })
            {
                var wire = new ProfileWire(); AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
                wire.Answer("{\"user\":{\"effective_tier\":" + tier + "}}");
                await AccountSeed.CheckSubscriptionNowAsync();
            }
            Assert.Equal(new int?[] { 0, null }, readings);
        }
        finally { AccountSeed.SubscriptionReadingApplied -= OnReading; AccountSeed.NewV2 = oldFactory; CoreSettings.ServiceProvider = null; }
    });
    [Fact]
    public Task LateProfileCannotGrantTheNextAccount() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key); CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
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
        var settings = new SettingsService(); var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var token = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = "test", RefreshToken = "test", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        var secrets = new Dictionary<string, string?> { ["patreon_auth"] = token };
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key); CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
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
            s.AuthToken = "before-repair";
            Assert.Equal(JustSubscribedOutcome.NotYet, await AccountSeed.CheckSubscriptionNowAsync());
            Assert.Equal("repaired", s.AuthToken);
        }
        finally { AccountSeed.NewV2 = oldFactory; CoreSettings.ServiceProvider = null; }
    });
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public Task PendingPatreonReplyCannotRestoreAChangedSession(bool logout, bool expired, bool newGrant) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = null;
        s.UnifiedId = "first"; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?> { ["patreon_auth"] = JsonConvert.SerializeObject(new PatreonTokenData
            { AccessToken = "a", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(expired ? -1 : 1) }) };
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key); CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var wire = new PendingValidation();
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s, p == "patreon" ? wire : null));
        var old = AccountSeed.NewV2;
        try
        {
            s.AuthToken = "first";
            AccountSeed.NewV2 = () => throw new InvalidOperationException();
            var check = AccountSeed.CheckSubscriptionNowAsync();
            Assert.Equal(1, wire.Reads);
            if (logout) AccountSeed.Patreon!.Logout();
            else if (newGrant) AccountSeed.Patreon!.StoreTokens("new", "new", DateTime.UtcNow.AddDays(1));
            else { s.UnifiedId = "second"; s.AuthToken = "second"; }
            wire.Reply.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(expired ? "{\"access_token\":\"old\",\"refresh_token\":\"old\",\"expires_in\":3600}" : "{\"is_active\":true,\"tier\":2,\"auth_token\":\"old\"}") });
            await check;
            Assert.Equal(PatreonTier.None, AccountSeed.Patreon!.CurrentTier);
            Assert.Null(s.PatreonPremiumValidUntil); Assert.Null(s.PatreonLabValidUntil);
            Assert.Null(AccountSeed.Patreon.RetrieveCachedState());
            if (logout) Assert.False(AccountSeed.Patreon.IsAuthenticated);
            if (newGrant) Assert.Equal("new", AccountSeed.Patreon.GetAccessToken());
            Assert.Equal(logout || newGrant ? "first" : "second", s.AuthToken);
        }
        finally { AccountSeed.NewV2 = old; CoreSettings.ServiceProvider = null; }
    });
    private sealed class PendingValidation : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<HttpResponseMessage> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Reads++; return Reply.Task; }
    }
    private sealed class ValidationWire : HttpMessageHandler
    {
        public int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("/patreon/validate", request.RequestUri!.AbsolutePath);
            Reads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"is_active\":true,\"tier\":1,\"auth_token\":\"repaired\"}") });
        }
    }
    [Fact]
    public Task FreshPatreonConfirmationRestampsGraceAndAppliesExtras() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); var s = settings.Current;
        s.PatreonTier = 0; s.PatreonPremiumValidUntil = null; s.PatreonLabValidUntil = null;
        s.InviteGrantUntil = null; s.OfflineMode = false;
        CoreSettings.ServiceProvider = () => settings;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key); CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
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
