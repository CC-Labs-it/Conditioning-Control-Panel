using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF AccountSettingsSection PlansView (PlansMode) + AppSettingsTabView.CheckPill -> OnPlansVisibilityChanged:
/// Settings > Account &amp; Plans carries the vault header and a link to Premium; its motion follows the lit pill.</summary>
public sealed class AccountPlansVaultTests
{
    [Fact]
    public void PlansCopyShowsTheHeaderAndLinkAndItsMotionFollowsTheAccountPill()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var old = (s.MotionLevel, s.PerformanceMode);
            MainShellWindow? shell = null;
            try
            {
                (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
                shell = new MainShellWindow { Width = 1400, Height = 900 };
                shell.Show();
                shell.ShowTab("appsettings");
                Dispatcher.UIThread.RunJobs();
                var page = shell.Named<AppSettingsTabView>("AppSettingsTab")!;
                var plans = page.FindControl<AccountSettingsSection>("SectionAccount")!.FindControl<ExclusivesTabView>("PlansView")!;

                Assert.True(plans.PlansMode);
                Assert.False(plans.FindControl<ItemsControl>("ExclusivesShelf")!.IsVisible);
                Assert.False(plans.FindControl<Border>("SpotlightCard")!.IsVisible);
                Assert.Equal(Loc.Get("plans_header_title"), plans.FindControl<TextBlock>("TxtVaultTitle")!.Text);
                Assert.Equal(Loc.Get("plans_header_sub"), plans.FindControl<TextBlock>("TxtVaultSub")!.Text);

                page.FocusSection("data");
                Dispatcher.UIThread.RunJobs();
                Assert.False(plans.MotionRunning);   // parked off Account
                page.FocusSection("account");
                Dispatcher.UIThread.RunJobs();
                Assert.True(plans.MotionRunning);    // runs while Account is lit
                page.FocusSection("general");
                Dispatcher.UIThread.RunJobs();
                Assert.False(plans.MotionRunning);

                var see = plans.FindControl<Button>("BtnSeePremium")!;
                Assert.True(see.IsVisible);
                see.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("premium", shell.CurrentTab);
                Assert.False(shell.Named<ExclusivesTabView>("ExclusivesTab")!.PlansMode);
            }
            finally
            {
                shell?.Close();
                (s.MotionLevel, s.PerformanceMode) = old;
            }
        });
    }
}
