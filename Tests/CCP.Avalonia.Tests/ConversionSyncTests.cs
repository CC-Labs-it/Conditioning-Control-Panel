using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Media;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup.Conversion;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ConversionSyncTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(new FontManagerOptions
                {
                    FontFamilyMappings = new Dictionary<string, FontFamily>
                    {
                        ["Segoe UI Black"] = new("DejaVu Sans"), ["Segoe UI"] = new("DejaVu Sans"),
                        ["Consolas"] = new("DejaVu Sans Mono"), ["Courier New"] = new("DejaVu Sans Mono"),
                    },
                }).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public Task BlinkDirectStartDemandsPremiumBeforeAnyTrackerWork() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreEntitlement.HasPremiumProvider = () => false;
        var denied = 0;
        CoreEntitlement.ShowDeniedHandler = _ => denied++;
        CoreSettings.Current.WebcamConsentGiven = false;
        var view = new BlinkTrainerTabView();
        typeof(BlinkTrainerTabView).GetMethod("BtnBlinkTrainerStartSession_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object?[] { null, new RoutedEventArgs() });
        Assert.Equal(1, denied);
        Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.BlinkTrainerSession.IsRunning);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("gradedintake")]
    [InlineData("quest_reroll")]
    public Task PricedRoutesReconnectBeforeSelling(string key) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        var reconnect = 0;
        shell.ReconnectFromGateOverride = () => { reconnect++; return Task.CompletedTask; };
        TierGate.ReconnectIsTheAnswerProvider = () => true;
        try
        {
            shell.OpenPricedCard(key, 1);
            Assert.Equal(1, reconnect);
            Assert.Null(VaultGateDialog.Open);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SpentActionsOfferBasicCard(bool weekly) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreEntitlement.HasPremiumProvider = () => false;
        CoreAccount.HasPremiumAccessProvider = () => false;
        CoreAccount.IsLoggedInProvider = () => true;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        TierGate.ReconnectIsTheAnswerProvider = () => false;
        var shell = new MainShellWindow();
        var host = new StackPanel();
        AvApp.Notifications.AttachHost(host);
        try
        {
            shell.Show();
            shell.Named<QuestsTabView>("QuestsTab")!.TellRerollsSpent(weekly);
            ClickToast(host, Loc.Get("quest_reroll_see_basic"));
            Assert.NotNull(VaultGateDialog.Open);
            VaultGateDialog.Open!.Close();
            CoreSettings.Current.IntakePassSpentWeek = IntakePassService.CurrentWeekKey();
            CoreSettings.Current.IntakePassSpentUtc = DateTime.UtcNow;
            shell.Named<GradedIntakeTabView>("GradedIntakeTab")!.BtnStartIntake_Click(null, new RoutedEventArgs());
            ClickToast(host, Loc.Get("intake_gate_spent_cta"));
            Assert.NotNull(VaultGateDialog.Open);
            Assert.Equal(1, typeof(VaultGateDialog).GetField("_tier", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(VaultGateDialog.Open));
        }
        finally { VaultGateDialog.Open?.Close(); shell.Close(); }
        return Task.CompletedTask;
    });

    private static void ClickToast(Control host, string label) => host.GetVisualDescendants().OfType<Button>()
        .Last(b => b.Content is TextBlock t && t.Text == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class ProfileReply : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"user\":{\"patreon_member\":true,\"paid_until\":null,\"post_grants\":{\"voice\":\"2099-01-01T00:00:00Z\"}}}") });
        }
    }

    [Fact]
    public Task FocusExtrasAreAdditiveAndHelloIsOnceEver() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        s.UnifiedId = "conversion-user"; s.AuthToken = "test-token"; s.OfflineMode = false;
        s.PatreonMember = false; s.InboxStamps.Clear();
        s.ServerPaidUntil = DateTime.UtcNow;
        CoreAccount.HasPremiumAccessProvider = () => false;
        StartupLadder.ResetForTests();
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var fake = new ProfileReply();
        MainShellWindow.ConversionClient = () => new V2AuthService(() => s, fake);
        AvApp.DailyFree = new DailyFreeService(fetchOverride: false);
        var shell = new MainShellWindow();
        try
        {
            await shell.RefreshConversionProfileAsync();
            await shell.RefreshConversionProfileAsync();
            Assert.Equal(1, fake.Calls);
            Assert.True(s.PatreonMember);
            Assert.Null(s.ServerPaidUntil);
            Assert.True(ProfileExtras.GrantOpen(s.PostGrants, "voice", DateTime.UtcNow));
            var row = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Equal("conv:free_member_hello", row.Key);
            Assert.Contains(Loc.Get("conv_member_hello_body"), MainShellWindow.MemberHelloBody(row.Summary!));
            Assert.NotEqual(row.Summary, MainShellWindow.MemberHelloBody(row.Summary!));
            shell.ApplyConversionExtras(s.UnifiedId, ProfileExtras.Parse(new JObject()));
            shell.ApplyConversionExtras("another-account", ProfileExtras.Parse(JObject.Parse("{\"patreon_member\":false}")));
            shell.MaybePostMemberHello();
            Assert.True(s.PatreonMember);
            Assert.Single(StartupLadder.Inbox.Items);
            CoreAccount.HasPremiumAccessProvider = () => true;
            s.InboxStamps.Clear();
            Assert.False(MainShellWindow.IsFreeMember);
            shell.MaybePostMemberHello();
            Assert.Empty(s.InboxStamps);
        }
        finally { shell.Close(); StartupLadder.ResetForTests(); MainShellWindow.ConversionClient = null; AvApp.DailyFree = null; }
    });

    [Fact]
    public Task MemberWelcomeIsNonModalAndHoldsTheNextPassiveSurfaceUntilDismissed() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        StartupLadder.ResetForTests();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreSettings.Current.PatreonMember = true;
            CoreSettings.Current.InboxStamps.Clear();
            shell.MaybePostMemberHello();
            var card = Assert.Single(shell.OwnedWindows.OfType<InboxNoticeCard>());
            Assert.True(card.IsVisible);
            Assert.True(shell.IsEnabled);
            Assert.IsAssignableFrom<ConditioningControlPanel.Services.Startup.IPassiveStartupSurface>(card);
            Assert.True(StartupLadder.PassiveWindowUp());
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("conv_member_hello_title"));
            var dismiss = Assert.Single(card.GetVisualDescendants().OfType<Button>());
            Assert.Equal(Loc.Get("conv_ok"), Assert.IsType<TextBlock>(dismiss.Content).Text);
            var nextOpened = false;
            StartupLadder.PresentOrInbox(new ConditioningControlPanel.Services.Startup.InboxItem
                { Key = "after-member", Open = () => nextOpened = true });
            var clock = Assert.IsType<StartupLadder.TestClock>(StartupLadder.Time);
            clock.Advance(TimeSpan.FromSeconds(5));
            Assert.False(nextOpened);
            dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(card.IsVisible);
            Assert.False(StartupLadder.PassiveWindowUp());
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.True(nextOpened);
        }
        finally { InboxNoticeCard.Open?.Close(); shell.Close(); StartupLadder.ResetForTests(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, false)]
    public Task RecapTasteOnlyAtFreeLiveCompletedEnd(bool completed, bool history, bool premium, bool expected)
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreAccount.HasPremiumAccessProvider = () => premium;
        var recap = new SessionCompleteWindow(new SessionLog { Completed = completed }, playSound: !history);
        try { Assert.Equal(expected, recap.FindControl<TextBlock>("TxtTasteLine")!.IsVisible); }
        finally { recap.Close(); }
        return Task.CompletedTask;
    });
}
