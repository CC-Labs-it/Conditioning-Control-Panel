using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class IntakeSpentOfferTests
{
    [Fact]
    public Task SpentPassToastOpensBasicIntakePriceCard() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().WithInterFont()
                .With(AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider,
            CoreAccount.HasPremiumAccessProvider);
        var settings = new SettingsService();
        var spent = (settings.Current.IntakePassSpentWeek, settings.Current.IntakePassSpentUtc);
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.HasPremiumAccessProvider = () => false;
        settings.Current.IntakePassSpentWeek = IntakePassService.CurrentWeekKey();
        settings.Current.IntakePassSpentUtc = DateTime.UtcNow;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var toasts = new StackPanel();
            AvApp.Notifications.AttachHost(toasts);
            Assert.Equal(IntakePassState.Spent, AvApp.IntakePass.State);
            shell.Named<GradedIntakeTabView>("GradedIntakeTab")!
                .BtnStartIntake_Click(null, new RoutedEventArgs());
            var toast = Assert.Single(toasts.Children.OfType<Border>());
            Assert.Equal(TimeSpan.FromSeconds(8), Assert.IsType<DispatcherTimer>(toast.Tag).Interval);
            var action = toast.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ToastAction");
            Assert.Equal(Loc.Get("intake_gate_spent_cta"), Assert.IsType<TextBlock>(action.Content).Text);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var card = VaultGateDialog.Open;
            Assert.NotNull(card);
            var texts = card!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains(Loc.GetF("vaultgate_title_vault", Loc.Get("tab_gradedintake")), texts);
            Assert.Contains(Loc.Get("vaultgate_basic_9"), texts);
            Assert.DoesNotContain(Loc.Get("vaultgate_chip_lab"), texts);
            Assert.NotEqual("appsettings", shell.CurrentTab);
        }
        finally
        {
            VaultGateDialog.Open?.Close();
            AvApp.Notifications.AttachHost(new StackPanel());
            shell.Close();
            (settings.Current.IntakePassSpentWeek, settings.Current.IntakePassSpentUtc) = spent;
            settings.SaveImmediate();
            (CoreSettings.ServiceProvider, CoreAccount.IsLoggedInProvider,
                CoreAccount.HasPremiumAccessProvider) = previous;
        }
        return Task.CompletedTask;
    });
}
