using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
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
            Assert.True(Application.Current!.TryFindResource("SecondaryButton", out var theme)); Assert.Same(theme, button.Theme);
            Assert.Equal(new Thickness(14, 5), button.Padding); Assert.IsType<TextBlock>(button.Content);
            var row = view.FindControl<StackPanel>("JustSubscribedRow")!;
            Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Left, row.HorizontalAlignment);
            Assert.Equal(new Thickness(0, -8, 0, 18), row.Margin);
            var status = view.FindControl<TextBlock>("TxtJustSubscribed")!;
            Assert.Equal(11.5, status.FontSize);
            Assert.True(Application.Current!.TryFindResource("TextSecondaryBrush", out var muted)); Assert.Same(muted, status.Foreground);
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
        var secrets = new Dictionary<string, string?>(); CoreSecrets.RetrieveProvider = key => { lock (secrets) return secrets.GetValueOrDefault(key); };
        CoreSecrets.StoreProvider = (key, value) => { lock (secrets) secrets[key] = value; };
        var s = CoreSettings.Current; s.OfflineMode = false; s.UnifiedId = "checkout"; s.AuthToken = "test-token";
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var handler = new ProfileAnswer(); AccountSeed.NewV2 = () => new V2AuthService(() => s, handler);
        using var patreon = new ProviderSubscription("patreon", () => s, handler); patreon.StoreTokens("test-token", "test-refresh", DateTime.UtcNow.AddHours(1));
        var slot = typeof(AccountSeed).GetProperty("Patreon", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = slot.GetValue(null); slot.SetValue(null, patreon); Task activation = Task.CompletedTask;
        var grant = new TaskCompletionSource<PatreonTier>(TaskCreationOptions.RunContinuationsAsynchronously); patreon.TierChanged += (_, tier) => grant.TrySetResult(tier);
        try {
            CheckoutWatch.Disarm(); CheckoutWatch.NoteOpened("https://app.cclabs.app/subscribe");
            activation = EntitlementRefresh.OnActivated(shell: false); await EntitlementRefresh.OnActivated(shell: false);
            Assert.Equal(1, handler.Reads); Assert.Equal(PatreonTier.Level2, await grant.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => !patreon.IsVerifying, TimeSpan.FromSeconds(5)));
            Assert.False(handler.Profile.Task.IsCompleted); Assert.True(ProviderSubscription.HasLabAccess(patreon, null, s));
        } finally {
            handler.Profile.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"user\":{\"effective_tier\":0}}") });
            try { await activation.WaitAsync(TimeSpan.FromSeconds(10)); await grant.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(SpinWait.SpinUntil(() => !patreon.IsVerifying, TimeSpan.FromSeconds(5))); } finally { slot.SetValue(null, previous); CheckoutWatch.Disarm(); }
        }
        Assert.True(EntitlementRefresh.ServerAnsweredThisRun); s.OfflineMode = true;
        CheckoutWatch.Disarm(); CheckoutWatch.NoteOpened("https://patreon.com/checkout");
        await EntitlementRefresh.OnActivated(shell: false); Assert.Equal(1, handler.Reads); CheckoutWatch.Disarm();
    });

    [Fact]
    public void TierNoticeDoesNotLetPresenterFailureEscape() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
            .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        StartupLadder.ResetForTests();
        var shell = new MainShellWindow();
        CoreAccount.HasLabAccessProvider = () => true;
        var s = CoreSettings.Current;
        s.LastKnownEffectiveTier = 0; s.SeenFeatureIntros.Add(TierCelebration.KeyT2);
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(1));
        void Fail() => throw new InvalidOperationException("presenter probe");
        StartupLadder.Inbox.Changed += Fail;
        try
        {
            Assert.Null(Record.Exception(shell.TierOnNoticePass));
            Assert.Contains(StartupLadder.Inbox.Items, row => row.Key == "tier-on:2");
        }
        finally { StartupLadder.Inbox.Changed -= Fail; shell.Close(); StartupLadder.ResetForTests(); }
    });

    [Fact]
    public void HideShowKeepsOneActivationCallback() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        using var hook = EntitlementRefresh.Install();
        var window = new Window { ShowActivated = false }; var previous = EntitlementRefresh.Activation; int calls = 0;
        try
        {
            EntitlementRefresh.Activation = shell => { calls++; previous(shell); };
            window.Show(); window.Hide(); window.Show(); window.Hide(); window.Show();
            // Headless Activate is a no-op; deliver the native activation notification.
            var notify = typeof(WindowBase).GetMethod("HandleActivated", BindingFlags.Instance | BindingFlags.NonPublic)!;
            calls = 0; notify.Invoke(window, null);
            Assert.Equal(1, calls);
        }
        finally { window.Close(); EntitlementRefresh.Activation = previous; }
    });

    private sealed class ProfileAnswer : HttpMessageHandler
    {
        internal int Reads; internal readonly TaskCompletionSource<HttpResponseMessage> Profile = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Get, r.Method); if (r.RequestUri!.AbsolutePath == "/patreon/validate")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"is_active\":true,\"tier\":2}") });
            Reads++; return Profile.Task;
        }
    }

}
