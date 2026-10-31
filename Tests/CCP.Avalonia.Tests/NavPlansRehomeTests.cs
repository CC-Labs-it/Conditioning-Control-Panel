using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NavPlansRehomeTests
{
    [Fact]
    public void AccountHostsCompactPlansWithoutAnInnerScroller() => AvaloniaTestDispatcher.Run(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var account = new AccountSettingsSection();
        var plans = account.FindControl<ExclusivesTabView>("PlansView")!;
        Assert.True(plans.PlansMode);
        Assert.False(plans.FindControl<ItemsControl>("ExclusivesShelf")!.IsVisible);
        Assert.False(plans.FindControl<Border>("SpotlightCard")!.IsVisible);
        Assert.True(plans.FindControl<Button>("BtnSeePremium")!.IsVisible);
        Assert.True(plans.FindControl<Border>("InvitesWrapper")!.IsVisible);
        Assert.False(plans.FindControl<InvitePanel>("InvitesHost")!.IsVisible);
        Assert.NotNull(plans.FindControl<Control>("TierPlate1"));
        Assert.NotNull(plans.FindControl<Control>("TierPlate2"));
        var scroll = plans.FindControl<ScrollViewer>("ContentScroll")!;
        Assert.Null(scroll.Content);
        Assert.Null(scroll.Parent);
        Assert.Equal(Loc.Get("plans_header_title"), plans.FindControl<TextBlock>("TxtVaultTitle")!.Text);
        var full = new ExclusivesTabView();
        Assert.False(full.PlansMode);
        Assert.True(full.FindControl<ItemsControl>("ExclusivesShelf")!.IsVisible);
        Assert.False(full.FindControl<Border>("InvitesWrapper")!.IsVisible);
        Assert.False(full.FindControl<InvitePanel>("InvitesHost")!.IsVisible);
    });

    [Fact]
    public void PlansMotionFollowsAccountAndInviteTicketRehomesThere() => AvaloniaTestDispatcher.Run(() =>
    {
        AmbientBeatLoopsTests.Setup();
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.OpenAppSettingsSection("account");
            Dispatcher.UIThread.RunJobs();
            var plans = shell.PlansVaultView!;
            Assert.NotNull(plans);
            var settings = shell.Named<AppSettingsTabView>("AppSettingsTab")!;
            settings.FocusSection("account");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("account", settings.CheckedSectionKey);
            Assert.True(plans.MotionRunning);
            settings.FocusSection("general");
            Assert.False(plans.MotionRunning);
            settings.FocusSection("account");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("account", settings.CheckedSectionKey);
            Assert.True(plans.MotionRunning);
            shell.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.False(plans.MotionRunning);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(plans.MotionRunning);
            shell.ShowTab("achievements");
            Assert.False(plans.MotionRunning);
            shell.OpenInvitesCard();
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Equal("account", shell.Named<AppSettingsTabView>("AppSettingsTab")!.CheckedSectionKey);
            plans.FindControl<Button>("BtnSeePremium")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("premium", shell.CurrentTab);
            Assert.False(plans.MotionRunning);
        }
        finally { shell.Close(); }
    });
}
