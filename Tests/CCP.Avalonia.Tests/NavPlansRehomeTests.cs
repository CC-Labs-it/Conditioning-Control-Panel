using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
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
    public void PlansFollowTheTierCardAndPrecedeProviderCards() => AvaloniaTestDispatcher.Run(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var account = new AccountSettingsSection();
        var tier = account.FindControl<Border>("AccountTierCard")!;
        var plans = account.FindControl<ExclusivesTabView>("PlansView")!;
        var patreon = account.FindControl<Border>("PatreonLoginCard")!;
        var stack = Assert.IsType<StackPanel>(plans.Parent);
        Assert.Same(plans, stack.Children[stack.Children.IndexOf(tier) + 1]);
        Assert.Same(patreon, stack.Children[stack.Children.IndexOf(plans) + 1]);
        Assert.Equal(new Thickness(0, 0, 0, 12), plans.Margin);
        var host = new Window { Width = 900, Height = 1000, Content = new ScrollViewer { Content = account } };
        try
        {
            host.Show(); Dispatcher.UIThread.RunJobs(); host.UpdateLayout();
            Assert.True(tier.Bounds.Bottom <= plans.Bounds.Top);
            Assert.True(plans.Bounds.Bottom <= patreon.Bounds.Top);
            if (Environment.GetEnvironmentVariable("CCP_LAYOUT_PNG_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var bitmap = new RenderTargetBitmap(new PixelSize(900, 1000));
                bitmap.Render(host);
                bitmap.Save(Path.Combine(directory, "account-plans.png"));
            }
        }
        finally { host.Close(); }
    });

    [Fact]
    public void PremiumLinkUsesTheSecondaryThemeAndWpfGeometry() => AvaloniaTestDispatcher.Run(() =>
    {
        AmbientBeatLoopsTests.Setup();
        var plans = new ExclusivesTabView { PlansMode = true };
        var button = plans.FindControl<Button>("BtnSeePremium")!;
        Assert.Same(Application.Current!.FindResource("SecondaryButton"), button.Theme);
        Assert.IsType<ControlTheme>(button.Theme);
        Assert.Equal(HorizontalAlignment.Left, button.HorizontalAlignment);
        Assert.Equal(new Thickness(0, -8, 0, 18), button.Margin);
        Assert.Equal(new Thickness(18, 7), button.Padding);
        Assert.Equal(Loc.Get("plans_see_premium"), Assert.IsType<TextBlock>(button.Content).Text);
    });

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
