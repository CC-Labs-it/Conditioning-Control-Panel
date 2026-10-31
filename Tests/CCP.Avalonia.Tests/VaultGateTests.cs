using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Vault;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Main fbe161de2 et al.: a padlock opens the vault gate card, not Settings · Account.</summary>
public sealed class VaultGateTests
{
    [Theory]
    [InlineData("quest_reroll_daily_none_free")]
    [InlineData("quest_reroll_weekly_none_free")]
    [InlineData("intake")]
    public Task SpentPassAndRerollToastsOpenPricedCards(string route) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var shell = new MainShellWindow();
        var host = new StackPanel();
        global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(host);
        var oldReconnect = TierGate.ReconnectIsTheAnswerProvider;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        var oldLogin = CoreAccount.IsLoggedInProvider;
        var oldLab = CoreAccount.HasLabAccessProvider;
        var oldAccountPremium = CoreAccount.HasPremiumAccessProvider;
        // IntakePassService reads CoreSettings.Service; with no provider CoreSettings.Current is a fallback it never sees.
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var settings = CoreSettings.Current;
        var spent = (settings.IntakePassSpentWeek, settings.IntakePassSpentUtc);
        var oldQuests = global::ConditioningControlPanel.Avalonia.App.Quests;
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-vaultgate-reroll-").FullName;
        try
        {
            TierGate.ReconnectIsTheAnswerProvider = () => false;
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreAccount.HasLabAccessProvider = () => false;
            CoreAccount.HasPremiumAccessProvider = () => false;
            var board = new QuestService(null, dir);
            board.Progress.DailyQuests = new System.Collections.Generic.List<ActiveQuest> { new("pop_parade_d"), new("flash_rush_d"), new("spiral_sink_d") };
            board.Progress.WeeklyQuest = new ActiveQuest("flash_monsoon_w");
            global::ConditioningControlPanel.Avalonia.App.Quests = board;
            var quests = new QuestsTabView();
            shell.Content = quests;
            shell.Show();
            shell.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            // The board was painted with a reroll left; the budget runs out before the click (WPF RerollDailySlot / BtnRerollWeekly_Click).
            board.Progress.DailyRerollsUsed = 99;
            board.Progress.WeeklyRerollsUsed = 99;
            var buttons = new[]
            {
                ("quest_reroll_daily_none_free", quests.FindControl<DailyQuestCard>("DailyCard0")!.FindControl<Button>("BtnReroll")!),
                ("quest_reroll_weekly_none_free", quests.FindControl<Button>("BtnRerollWeekly")!),
            };
            foreach (var (key, button) in buttons.Where(b => b.Item1 == route))
            {
                Assert.True(button.IsEnabled);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Contains(host.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get(key));
                Click(host, Loc.Get("quest_reroll_see_basic"));
                Assert.Contains(Loc.Get("vaultgate_chip_vault"), Texts(VaultGateDialog.Open!));
                VaultGateDialog.Open!.Close();
                host.Children.Clear();
            }
            if (route == "intake")
            {
                settings.IntakePassSpentWeek = IntakePassService.CurrentWeekKey();
                settings.IntakePassSpentUtc = DateTime.UtcNow;
                var intake = new GradedIntakeTabView();
                shell.Content = intake;
                shell.UpdateLayout();
                intake.BtnStartIntake_Click(null, new RoutedEventArgs());
                Click(host, Loc.Get("intake_gate_spent_cta"));
                // WPF MainWindow.Lab.cs:183: tier 1, because unlimited runs are HasPremiumAccess.
                Assert.Contains(Loc.Get("vaultgate_chip_vault"), Texts(VaultGateDialog.Open!));
            }
        }
        finally
        {
            TierGate.ReconnectIsTheAnswerProvider = oldReconnect;
            CoreEntitlement.HasPremiumProvider = oldPremium;
            CoreAccount.IsLoggedInProvider = oldLogin;
            CoreAccount.HasLabAccessProvider = oldLab;
            CoreAccount.HasPremiumAccessProvider = oldAccountPremium;
            (settings.IntakePassSpentWeek, settings.IntakePassSpentUtc) = spent;
            CoreSettings.ServiceProvider = oldProvider;
            global::ConditioningControlPanel.Avalonia.App.Quests = oldQuests;
            VaultGateDialog.Open?.Close();
            shell.Close();
            global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(new StackPanel());
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SpentMomentCardUsesRequestedTierAndReconnectsDeadGrants() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var shell = new MainShellWindow();
        var old = TierGate.ReconnectIsTheAnswerProvider;
        var oldLd = LockdownService.Current;
        var reconnects = 0;
        shell.ReconnectFromGateOverride = () => { reconnects++; return Task.CompletedTask; };
        try
        {
            TierGate.ReconnectIsTheAnswerProvider = () => false;
            shell.OpenPricedCard("gradedintake", 2);
            Assert.Contains(Loc.Get("vaultgate_chip_lab"), Texts(VaultGateDialog.Open!));
            VaultGateDialog.Open!.Close();
            shell.OpenPricedCard("quest_reroll", 1);
            Assert.Contains(Loc.Get("vaultgate_chip_vault"), Texts(VaultGateDialog.Open!));
            VaultGateDialog.Open!.Close();
            TierGate.ReconnectIsTheAnswerProvider = () => true;
            shell.OpenPricedCard("gradedintake", 1);
            shell.OpenPricedCard("quest_reroll", 1);
            Assert.Equal(2, reconnects);
            Assert.Null(VaultGateDialog.Open);
            using var lockdown = new LockdownService();
            LockdownService.Current = lockdown;
            lockdown.Activate(TimeSpan.FromMinutes(5));
            shell.OpenPricedCard("gradedintake", 1);
            Assert.Equal(2, reconnects);
            TierGate.ReconnectIsTheAnswerProvider = () => false;
            shell.OpenPricedCard("quest_reroll", 1);
            Assert.Null(VaultGateDialog.Open);
        }
        finally { LockdownService.Current = oldLd; TierGate.ReconnectIsTheAnswerProvider = old; VaultGateDialog.Open?.Close(); shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PadlockOpensTheCardAndItsLinksClick() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var remote = shell.Named<RemoteControlTabView>("RemoteControlTab")!;
            var unlock = remote.GetLogicalDescendants().OfType<Button>()
                .Single(b => b.Content is IconLabel { Key: "gate_unlock_with_patreon" });
            unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var card = VaultGateDialog.Open;
            Assert.NotNull(card);
            Assert.Contains(Loc.GetF("vaultgate_title_vault", Loc.Get("tab_remote_control")), Texts(card!));
            Assert.Contains(Loc.Get("vaultgate_basic_8"), Texts(card!));
            Assert.NotEqual("appsettings", shell.CurrentTab);   // the old jump to Settings - Account is gone

            // "Sign in" is the account door, as WPF's ShowAppInfoPopup.
            Click(card!, Loc.Get("vaultgate_signin"));
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Null(VaultGateDialog.Open);
            unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            card = VaultGateDialog.Open;

            // 99f48bc22: the links are buttons and click; Compare -> Yearly -> Back -> Close.
            Click(card!, Loc.Get("vaultgate_compare"));
            Assert.Contains(Loc.Get("vaultgate_compare_title"), Texts(card!));
            Assert.Contains(Loc.Get("vaultgate_prime_7"), Texts(card!));
            Click(card!, Loc.Get("vaultgate_billing_yearly"));
            Assert.Contains(Loc.Get("vaultgate_yearly_note"), Texts(card!));
            Click(card!, Loc.Get("vaultgate_back"));
            Assert.Contains(Loc.Get("vaultgate_signin"), Texts(card!));
            Click(card!, Loc.Get("vaultgate_close"));
            Assert.Null(VaultGateDialog.Open);
        }
        finally { VaultGateDialog.Open?.Close(); shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SaleStrikesTheMonthlyPriceOnTheCoveredTier() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        try
        {
            VaultGateDialog.Sale = new VaultSaleInfo(20, null, new[] { 1 });
            var card = VaultGateDialog.ShowOffer(null, null, 2, () => { });
            Dispatcher.UIThread.RunJobs();
            // Lab card: the sale covers Basic only, so no strike and the Prime chip.
            Assert.Contains(Loc.Get("vaultgate_chip_lab"), Texts(card));
            Assert.DoesNotContain(card.GetVisualDescendants().OfType<TextBlock>(), t => "struck".Equals(t.Tag));

            card = VaultGateDialog.ShowOffer(null, "lockdown", 1, () => { });
            Dispatcher.UIThread.RunJobs();
            var cur = VaultOffer.HeadlineCurrency();   // WPF 5265111bc: the plan list's headline currency
            var struck = card.GetVisualDescendants().OfType<TextBlock>().Single(t => "struck".Equals(t.Tag));
            Assert.Equal(VaultOffer.Money(VaultOffer.PriceFor(1, cur).MonthlyCents, cur), struck.Text);
            Assert.Contains(Loc.Get("vaultgate_sale_first"), Texts(card));
        }
        finally { VaultGateDialog.Sale = null; VaultGateDialog.Open?.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SeeTiersOpensTheCardAtTheDoorsTierAndReconnectReplacesItForADeadGrant() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var shell = new MainShellWindow();
        var s = CoreSettings.Current;
        var (oldId, oldLinked, oldProvider, oldPremium) = (s.UnifiedId, s.HasLinkedPatreon, TierGate.ReconnectIsTheAnswerProvider, CoreAccount.HasPremiumAccessProvider);
        try
        {
            shell.Show();
            var reconnects = 0;
            shell.ReconnectFromGateOverride = () => { reconnects++; return Task.CompletedTask; };
            TierGate.ReconnectIsTheAnswerProvider = MainShellWindow.ReconnectIsTheAnswerNow;   // as App.axaml.cs seeds it
            CoreAccount.HasPremiumAccessProvider = () => false;
            s.UnifiedId = null;
            s.HasLinkedPatreon = false;
            var verdict = new TierVerdict(false, "Down the Rabbit Hole", PatreonTier.Level2, "refused");

            var toasts = Toasts(out var host);
            shell.ShowTierDenied(verdict, toasts);
            Click(host, Loc.Get("tiergate_see_tiers"));
            Assert.Contains(Loc.Get("vaultgate_chip_lab"), Texts(VaultGateDialog.Open!));
            VaultGateDialog.Open!.Close();

            // Linked server-side, no grant on this PC, no premium: Core PatreonReconnectRule says Reconnect.
            s.UnifiedId = "u-1";
            s.HasLinkedPatreon = true;
            toasts = Toasts(out host);
            shell.ShowTierDenied(verdict, toasts);
            Assert.Contains(host.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("tiergate_denied_reconnect"));
            Click(host, Loc.Get("tiergate_reconnect_action"));
            Assert.Equal(1, reconnects);
            var unlock = shell.Named<RemoteControlTabView>("RemoteControlTab")!.GetLogicalDescendants().OfType<Button>()
                .Single(b => b.Content is IconLabel { Key: "gate_unlock_with_patreon" });
            unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, reconnects);
            Assert.Null(VaultGateDialog.Open);
        }
        finally
        {
            (s.UnifiedId, s.HasLinkedPatreon) = (oldId, oldLinked);
            (TierGate.ReconnectIsTheAnswerProvider, CoreAccount.HasPremiumAccessProvider) = (oldProvider, oldPremium);
            VaultGateDialog.Open?.Close();
            shell.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PadlockAndVaultCardsRefuseUnderLockdown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var shell = new MainShellWindow();
        var (oldLd, oldProvider) = (LockdownService.Current, TierGate.ReconnectIsTheAnswerProvider);
        try
        {
            shell.Show();
            var unlock = shell.Named<RemoteControlTabView>("RemoteControlTab")!.GetLogicalDescendants().OfType<Button>()
                .Single(b => b.Content is IconLabel { Key: "gate_unlock_with_patreon" });
            shell.ShowTab("achievements");
            var ld = LockdownService.Current = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(30));
            unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            shell.OpenExclusiveFeature("remotecontrol");
            shell.ShowVaultGate(null, 1);
            Assert.Null(VaultGateDialog.Open);
            // The padlock refuses before either branch: not even the Reconnect sign-in opens.
            var reconnects = 0;
            shell.ReconnectFromGateOverride = () => { reconnects++; return Task.CompletedTask; };
            TierGate.ReconnectIsTheAnswerProvider = () => true;
            unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, reconnects);
            TierGate.ReconnectIsTheAnswerProvider = oldProvider;
            Assert.Equal("achievements", shell.CurrentTab);

            ld.Deactivate();
            shell.OpenExclusiveFeature("remotecontrol");
            Assert.Equal("remotecontrol", shell.CurrentTab);
            unlock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(VaultGateDialog.Open);
        }
        finally { (LockdownService.Current, TierGate.ReconnectIsTheAnswerProvider) = (oldLd, oldProvider); VaultGateDialog.Open?.Close(); shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task InviteWeekLastDayCardOpensOncePerWeek() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var shell = new MainShellWindow();
        var s = CoreSettings.Current;
        var (oldUntil, oldSeen) = (s.InviteGrantUntil, s.SeenFeatureIntros.ToList());
        try
        {
            s.InviteGrantUntil = DateTime.UtcNow.AddHours(10);
            var key = VaultOffer.InviteEndingOwed(s.InviteGrantUntil, DateTime.UtcNow, true, null)!;
            s.SeenFeatureIntros.Remove(key);
            shell.MaybeShowInviteEnding();
            Dispatcher.UIThread.RunJobs();
            if (VaultGateDialog.Open == null)   // a quiet window parks it in the Inbox instead
                StartupLadder.Inbox.Items.Single(i => i.Key == "intro:" + key).Open!();
            Assert.Contains(Loc.Get("vaultgate_ending_title"), Texts(VaultGateDialog.Open!));
            Assert.Contains(key, s.SeenFeatureIntros);
            VaultGateDialog.Open!.Close();
            shell.MaybeShowInviteEnding();
            Assert.Null(VaultGateDialog.Open);
        }
        finally
        {
            s.InviteGrantUntil = oldUntil;
            s.SeenFeatureIntros.Clear();
            s.SeenFeatureIntros.AddRange(oldSeen);
            VaultGateDialog.Open?.Close();
            shell.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CardLinksShowAKeyboardFocusOutline() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var card = VaultGateDialog.ShowOffer(null, null, 1, () => { });
        try
        {
            Dispatcher.UIThread.RunJobs();
            var link = card.GetVisualDescendants().OfType<Button>()
                .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.Get("vaultgate_compare")));
            var presenter = link.GetVisualDescendants().OfType<global::Avalonia.Controls.Presenters.ContentPresenter>().First();
            Assert.True(link.Focusable);
            Assert.Equal(default, presenter.BorderThickness);
            link.Focus(global::Avalonia.Input.NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Thickness(1), presenter.BorderThickness);
        }
        finally { card.Close(); }
        return Task.CompletedTask;
    });

    /// <summary>WPF 750e76812: under every Patreon button a link opens the site's card / PayPal checkout for that plan.</summary>
    [Fact]
    public Task EveryPayButtonAlsoOffersTheCardCheckoutForItsPlan() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var opened = new System.Collections.Generic.List<string>();
        var previous = VaultGateDialog.Launch;
        VaultGateDialog.Launch = t => { opened.Add(t); return true; };   // never a real URL
        string Url(string plan) => string.Format(VaultGateDialog.CardUrlFormat, plan);
        try
        {
            var card = VaultGateDialog.ShowOffer(null, null, 2, () => { });   // Lab card -> prime
            Dispatcher.UIThread.RunJobs();
            Click(card, Loc.Get("vaultgate_or_card"));
            Assert.Null(VaultGateDialog.Open);

            card = VaultGateDialog.ShowOffer(null, null, 1, () => { });       // Vault card -> basic
            Dispatcher.UIThread.RunJobs();
            Click(card, Loc.Get("vaultgate_or_card"));

            card = VaultGateDialog.ShowOffer(null, null, 1, () => { });       // Compare: one link per tier column
            Dispatcher.UIThread.RunJobs();
            Click(card, Loc.Get("vaultgate_compare"));
            var links = card.GetVisualDescendants().OfType<Button>()
                .Where(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.Get("vaultgate_or_card"))).ToList();
            Assert.Equal(2, links.Count);
            Assert.All(links, l => Assert.True(l.Focusable));
            Assert.All(links, l => Assert.Equal(TextAlignment.Center, ((TextBlock)l.Content!).TextAlignment));   // WPF centres wrapped lines
            links[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            card = VaultGateDialog.ShowEnding(null, DateTime.UtcNow.AddDays(2), () => { });   // Keep Basic -> basic
            Dispatcher.UIThread.RunJobs();
            Click(card, Loc.Get("vaultgate_or_card"));

            Assert.Equal(new[] { Url("prime"), Url("basic"), Url("prime"), Url("basic") }, opened);
            Assert.Equal("https://app.cclabs.app/subscribe?plan=basic&from=panel", Url("basic"));
        }
        finally { VaultGateDialog.Launch = previous; VaultGateDialog.Open?.Close(); }
        return Task.CompletedTask;
    });

    private static ConditioningControlPanel.Avalonia.Helpers.NotificationService Toasts(out StackPanel host)
    {
        var toasts = new ConditioningControlPanel.Avalonia.Helpers.NotificationService();
        toasts.AttachHost(host = new StackPanel());
        return toasts;
    }

    private static void Click(Panel host, string text)
    {
        host.GetLogicalDescendants().OfType<Button>().First(b => b.Content is TextBlock t && t.Text == text)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void EnsureAvalonia()
    {
        if (global::Avalonia.Application.Current is not null) return;
        global::Avalonia.AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
            .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static string?[] Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();

    private static void Click(Window w, string text)
    {
        var button = w.GetVisualDescendants().OfType<Button>()
            .First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }
}
