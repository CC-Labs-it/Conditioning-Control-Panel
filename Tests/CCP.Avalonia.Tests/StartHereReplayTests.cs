using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StartHereReplayTests
{
    [Fact]
    public Task HelpOpensCardWithFourRowsWithoutSpendingOffer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var language = LocalizationManager.Instance.CurrentLanguage;
        LocalizationManager.Instance.SetLanguage("en");
        var previous = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.StartHereShown = false;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            shell.SetTutorialOverlay(true);
            var replay = shell.FindControl<Button>("BtnHelpStartHere");
            Assert.NotNull(replay);
            replay!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, replay));
            var card = Assert.Single(shell.OwnedWindows.Where(w => w.Name == "StartHereCard"));
            Assert.True(card.IsVisible);
            card.UpdateLayout();
            Assert.Equal(4, card.GetVisualDescendants().OfType<Button>().Count(b => b.Tag is string));
            Assert.False(settings.Current.StartHereShown);
            card.Close();
        }
        finally { shell.Close(); CoreSettings.ServiceProvider = previous; LocalizationManager.Instance.SetLanguage(language); }
        return Task.CompletedTask;
    });
}
