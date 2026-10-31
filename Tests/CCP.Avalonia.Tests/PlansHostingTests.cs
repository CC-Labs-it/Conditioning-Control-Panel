using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PlansHostingTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    [Fact]
    public Task PlansKeepsVaultChromeAndInvitesWithoutShelfOrNestedScroller() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var account = new AccountSettingsSection();
        var plans = account.PlansView;
        Assert.True(plans.PlansMode);
        Assert.False(plans.FindControl<ItemsControl>("ExclusivesShelf")!.IsVisible);
        Assert.Empty(plans.FindControl<ItemsControl>("ExclusivesShelf")!.ItemsSource!.Cast<object>());
        Assert.Empty(plans.GetVisualDescendants().OfType<ScrollViewer>());
        foreach (var name in new[] { "TierPlate1", "TierPlate2" })
            Assert.True(plans.FindControl<Control>(name)!.IsVisible);
        Assert.False(plans.FindControl<Control>("SpotlightCard")!.IsVisible);
        Assert.Equal(Loc.Get("plans_header_title"), plans.FindControl<TextBlock>("TxtVaultTitle")!.Text);
        Assert.Equal(Loc.Get("plans_header_sub"), plans.FindControl<TextBlock>("TxtVaultSub")!.Text);
        Assert.True(plans.FindControl<Button>("BtnSeePremium")!.IsVisible);
        Assert.Equal(Loc.Get("plans_see_premium"), ((TextBlock)plans.FindControl<Button>("BtnSeePremium")!.Content!).Text);
        Assert.True(plans.FindControl<Control>("PlansInvitesHost")!.IsVisible);
        Assert.Null(plans.FindControl<ScrollViewer>("ContentScroll")!.Parent);
        // The separate Premium page retains its full collection and scroller.
        var full = new ExclusivesTabView();
        Assert.False(full.PlansMode);
        Assert.True(full.FindControl<ItemsControl>("ExclusivesShelf")!.IsVisible);
        Assert.NotNull(full.FindControl<ScrollViewer>("ContentScroll"));
        Assert.True(full.FindControl<Control>("SpotlightCard")!.IsVisible);
        Assert.False(full.FindControl<Button>("BtnSeePremium")!.IsVisible);
        Assert.False(full.FindControl<Control>("PlansInvitesHost")!.IsVisible);
        // An invite response can reveal the card, but never its Premium-page host.
        full.FindControl<Control>("InvitesHost")!.IsVisible = true;
        Assert.False(full.FindControl<Control>("PlansInvitesHost")!.IsVisible);
        Assert.Equal(Loc.Get("premium_page_title"), full.FindControl<TextBlock>("TxtVaultTitle")!.Text);
        Assert.Equal(Loc.Get("premium_page_sub"), full.FindControl<TextBlock>("TxtVaultSub")!.Text);
        var language = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            Assert.Equal(Loc.Get("plans_header_title"), plans.FindControl<TextBlock>("TxtVaultTitle")!.Text);
            Assert.Equal(Loc.Get("plans_header_sub"), plans.FindControl<TextBlock>("TxtVaultSub")!.Text);
            Assert.Equal(Loc.Get("premium_page_title"), full.FindControl<TextBlock>("TxtVaultTitle")!.Text);
            Assert.Equal(Loc.Get("premium_page_sub"), full.FindControl<TextBlock>("TxtVaultSub")!.Text);
        }
        finally { LocalizationManager.Instance.SetLanguage(language); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VaultAndInviteTicketOpenAccountAndPlansAndMotionParksOnLeave() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            shell.Show();
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            shell.Named<SettingsTabView>("SettingsTab")!.FindControl<FeatureCard>("CardVault")!
                .RaiseEvent(new RoutedEventArgs(FeatureCard.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var page = shell.Named<AppSettingsTabView>("AppSettingsTab")!;
            var plans = shell.PlansVaultView!;
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Equal("account", page.CheckedSectionKey);
            Assert.True(plans.MotionRunning);
            shell.OpenAppSettingsSection("data");
            Dispatcher.UIThread.RunJobs();
            Assert.False(plans.MotionRunning);
            Assert.False(plans.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!.IsRunning);
            shell.ShowTab("settings");
            shell.BtnInviteTicket_Click(null, new RoutedEventArgs());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("appsettings", shell.CurrentTab);
            Assert.Equal("account", page.CheckedSectionKey);
            Assert.True(plans.MotionRunning);
            shell.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.False(plans.MotionRunning);
            Assert.False(plans.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!.IsRunning);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(plans.MotionRunning);
            shell.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.False(plans.MotionRunning);
            shell.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(plans.MotionRunning);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.False(plans.MotionRunning);
            Assert.False(plans.FindControl<AmbientFxCanvas>("ExclusivesAmbientFx")!.IsRunning);
            shell.OpenAppSettingsSection("account");
            Dispatcher.UIThread.RunJobs();
            var premiumLink = plans.FindControl<Button>("BtnSeePremium");
            Assert.NotNull(premiumLink);
            premiumLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("premium", shell.CurrentTab);
            Assert.False(plans.MotionRunning);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
