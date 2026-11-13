using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class LegacyCalibrationNoticeTests
{
    [Theory]
    [InlineData("none", false)]
    [InlineData("no-bounds", false)]
    [InlineData("named", false)]
    [InlineData("null-name", true)]
    [InlineData("empty-name", true)]
    public Task ShellOffersRecalibrationOnlyForLegacyBounds(string calibration, bool expected) =>
        CheckNotice(calibration, expected, "en", "Your calibration needs updating for multi-monitor support.", "Recalibrate");

    [Fact]
    public Task FrenchLegacyNoticeUsesLocalizedCopy() => CheckNotice("null-name", true, "fr",
        "Ton étalonnage doit être mis à jour pour prendre en charge plusieurs écrans.", "Réétalonner");

    [Fact]
    public Task DismissedLegacyNoticeStaysDismissed() => CheckNotice("null-name", false, "en",
        "Your calibration needs updating for multi-monitor support.", "Recalibrate", dismissedBeforeOpen: true);

    private static Task CheckNotice(string calibration, bool expected, string language, string message, string action, bool dismissedBeforeOpen = false) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        var oldSettingLanguage = CoreSettings.Current.Language;
        var tracker = WebcamTracker.Instance;
        var oldCal = tracker.Calibration;
        var notices = AvApp.Notifications;
        var oldRemove = notices.RemoveLater;
        var dismissed = CoreSettings.Current.DismissedNotificationKeys;
        var hadDismissal = dismissed.Remove(WebcamCalibrationWindow.LegacyNoticeKey);
        MainShellWindow? shell = null;
        try
        {
            if (dismissedBeforeOpen) dismissed.Add(WebcamCalibrationWindow.LegacyNoticeKey);
            CoreSettings.Current.Language = language;
            LocalizationManager.Instance.SetLanguage(language);
            tracker.Calibration = calibration switch
            {
                "none" => null,
                "no-bounds" => new WebcamCalibrationData(),
                _ => new WebcamCalibrationData { MonitorBounds = new MonitorBoundsRecord
                    { DeviceName = calibration == "named" ? "monitor" : calibration == "empty-name" ? "" : null } }
            };
            notices.RemoveLater = (remove, _) => remove();
            shell = new MainShellWindow();
            var host = shell.FindControl<Panel>("NotificationHost")!;
            notices.AttachHost(host);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var toast = host.Children.OfType<Border>().SingleOrDefault(b => b.Child is Grid g &&
                g.Children.OfType<TextBlock>().Any(t => t.Text == message));
            Assert.Equal(expected, toast != null);
            if (!expected) return;
            Assert.Equal(Color.Parse("#FFB347"), ((ISolidColorBrush)toast!.BorderBrush!).Color);
            var button = ((Grid)toast.Child!).Children.OfType<StackPanel>().Single().Children.OfType<Button>()
                .Single(b => b.Name == "ToastAction");
            Assert.Equal(action, ((TextBlock)button.Content!).Text);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 100 && !shell.OwnedWindows.OfType<WebcamCalibrationWindow>().Any(); i++)
            { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.Single(shell.OwnedWindows.OfType<WebcamCalibrationWindow>());
            Assert.DoesNotContain(WebcamCalibrationWindow.LegacyNoticeKey, dismissed);
        }
        finally
        {
            foreach (var window in shell?.OwnedWindows.ToArray() ?? Array.Empty<Window>()) window.Close();
            shell?.RequestExit();
            notices.Dismiss(WebcamCalibrationWindow.LegacyNoticeKey);
            notices.RemoveLater = oldRemove;
            notices.AttachHost(new StackPanel());
            dismissed.Remove(WebcamCalibrationWindow.LegacyNoticeKey);
            if (hadDismissal) dismissed.Add(WebcamCalibrationWindow.LegacyNoticeKey);
            tracker.Calibration = oldCal;
            CoreSettings.Current.Language = oldSettingLanguage;
            LocalizationManager.Instance.SetLanguage(oldLanguage);
            Dispatcher.UIThread.RunJobs();
        }
    });
}
