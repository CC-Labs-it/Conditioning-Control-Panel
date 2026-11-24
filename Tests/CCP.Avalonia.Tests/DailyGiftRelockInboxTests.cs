using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup.Conversion;
using Xunit;

namespace CCP.Avalonia.Tests;

public class DailyGiftRelockInboxTests
{
    [Fact]
    public void FocusAndLapseRememberGiftAndFileOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        var shell = new MainShellWindow();
        var old = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.DailyFreeKeyProvider,
            CoreSettings.ServiceProvider, MainShellWindow.GiftSeededKeyFor, MainShellWindow.GiftIsFreeToday, MainShellWindow.GiftToday);
        var service = new ConditioningControlPanel.Services.SettingsService();
        var today = new DateTime(2026, 10, 7);
        var free = "takeover";
        try
        {
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.DailyFreeKeyProvider = () => free;
            CoreEntitlement.IsFreeTodayProvider = key => key == free;
            MainShellWindow.GiftSeededKeyFor = _ => "takeover";
            MainShellWindow.GiftIsFreeToday = key => key == free;
            MainShellWindow.GiftToday = () => today;
            StartupLadder.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromDays(2));
            service.Current.AutonomyModeEnabled = true;
            shell.DailyGiftFocus();
            Assert.Equal(ConversionRules.GiftUse(today, "takeover"), service.Current.InboxStamps[ConversionRules.KeyGiftUsed]);
            // Simulate a launch that never got yesterday's focus: only the pre-lapse hook can recover it.
            service.Current.InboxStamps.Clear();
            today = today.AddDays(1);
            free = "remote";
            shell.RefreshEntitlementVeils(false);
            Assert.False(service.Current.AutonomyModeEnabled);
            var row = Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "conv:daily_gift_relock"));
            Assert.False(string.IsNullOrEmpty(row.Summary));
            Assert.True(ConditioningControlPanel.Avalonia.Controls.IconMap.TryGet(row.Glyph, out var icon));
            Assert.Equal(FluentIcons.Common.Symbol.DoorArrowLeft, icon.Kind);
            shell.DailyGiftFocus();
            Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == row.Key));
            row.Open!();
            var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
            Assert.Equal(Loc.Get("conv_get_basic"), ((TextBlock)card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "NoticePrimary").Content!).Text);
            card.Close();
        }
        finally
        {
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.DailyFreeKeyProvider,
                CoreSettings.ServiceProvider, MainShellWindow.GiftSeededKeyFor, MainShellWindow.GiftIsFreeToday, MainShellWindow.GiftToday) = old;
            StartupLadder.ResetForTests();
            shell.Close();
        }
    });

    [Fact]
    public void PremiumOrFreeAgainDoesNotFile() => AvaloniaTestDispatcher.Run(() =>
    {
        var shell = new MainShellWindow();
        var old = (CoreEntitlement.HasPremiumProvider, MainShellWindow.GiftIsFreeToday, MainShellWindow.GiftToday);
        var s = new ConditioningControlPanel.Models.AppSettings();
        var today = new DateTime(2026, 10, 8);
        try
        {
            s.InboxStamps[ConversionRules.KeyGiftUsed] = ConversionRules.GiftUse(today.AddDays(-1), "takeover");
            MainShellWindow.GiftToday = () => today;
            MainShellWindow.GiftIsFreeToday = _ => false;
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.False(shell.MaybePostRelockRow(s));
            CoreEntitlement.HasPremiumProvider = () => false;
            MainShellWindow.GiftIsFreeToday = _ => true;
            Assert.False(shell.MaybePostRelockRow(s));
        }
        finally
        {
            (CoreEntitlement.HasPremiumProvider, MainShellWindow.GiftIsFreeToday, MainShellWindow.GiftToday) = old;
            shell.Close();
        }
    });

    [Fact]
    public void A_free_member_gets_the_hello_row_once_ever_and_a_paid_one_never() => AvaloniaTestDispatcher.Run(() =>
    {
        var shell = new MainShellWindow();
        var old = (CoreAccount.HasPremiumAccessProvider, CoreSettings.ServiceProvider);
        var service = new ConditioningControlPanel.Services.SettingsService();
        try
        {
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            StartupLadder.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromDays(2));
            CoreAccount.HasPremiumAccessProvider = () => true;
            s.PatreonMember = true;
            Assert.False(shell.MaybePostMemberHello(s));   // a paying patron is not a free member
            Assert.Empty(s.InboxStamps);
            CoreAccount.HasPremiumAccessProvider = () => false;
            s.PatreonMember = false;
            Assert.False(shell.MaybePostMemberHello(s));
            s.PatreonMember = true;
            shell.DailyGiftFocus();
            var row = Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "conv:" + ConversionRules.KeyFreeMemberHello));
            Assert.Equal("Gift", row.IconKey);
            Assert.Equal(Loc.Get("conv_member_hello_title"), row.Title);
            Assert.StartsWith(Loc.Get("conv_member_hello_body"), MainShellWindow.MemberHelloBody(row.Summary!));
            shell.DailyGiftFocus();
            Assert.False(shell.MaybePostMemberHello(s));   // once ever
            Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == row.Key));
            row.Open!();
            var card = Assert.IsType<InboxNoticeCard>(InboxNoticeCard.Open);
            Assert.True(shell.IsEnabled);   // non-modal
            Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("conv_ok"));
            card.Close();
        }
        finally
        {
            (CoreAccount.HasPremiumAccessProvider, CoreSettings.ServiceProvider) = old;
            StartupLadder.ResetForTests();
            shell.Close();
        }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MemberHelloFollowsLapseAndReadingWithoutActivation(bool reading) => AvaloniaTestDispatcher.Run(() =>
    {
        var evt = typeof(AccountSeed).GetField("TierReadingApplied", BindingFlags.Static | BindingFlags.NonPublic)!;
        var listeners = evt.GetValue(null);
        evt.SetValue(null, null);
        var old = (CoreAccount.HasPremiumAccessProvider, CoreEntitlement.HasPremiumProvider, CoreSettings.ServiceProvider);
        var service = new ConditioningControlPanel.Services.SettingsService();
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.ServiceProvider = () => service;
            var premium = true;
            CoreAccount.HasPremiumAccessProvider = () => premium;
            CoreEntitlement.HasPremiumProvider = () => premium;
            service.Current.PatreonMember = true;
            StartupLadder.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromDays(2));
            shell = new MainShellWindow();
            void Trigger()
            {
                if (reading) ((Action?)evt.GetValue(null))?.Invoke();
                else shell.RefreshEntitlementVeils(persist: true);
            }
            Trigger();
            Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key == "conv:free_member_hello");
            premium = false;
            Trigger();
            Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "conv:free_member_hello"));
            shell.DailyGiftFocus();
            ((Action?)evt.GetValue(null))?.Invoke();
            Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "conv:free_member_hello"));
        }
        finally
        {
            shell?.Close();
            evt.SetValue(null, listeners);
            (CoreAccount.HasPremiumAccessProvider, CoreEntitlement.HasPremiumProvider, CoreSettings.ServiceProvider) = old;
            StartupLadder.ResetForTests();
        }
    });

}
