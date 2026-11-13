using System;
using System.Linq;
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
}
