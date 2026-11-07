using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.DeeperTab.cs:604-617,649-668: a failed "Open with CCP" names the
/// window that failed and the exception type, through the real dispatch and its catch.</summary>
public class FileOpenDispatchTests
{
    [Theory]
    [InlineData("play", "deeper_open_player_failed_title", "deeper_open_player_failed_fmt")]
    [InlineData("edit", "deeper_open_editor_failed_title", "deeper_open_editor_failed_fmt")]
    public Task OpenFailureIsShownToTheUser(string action, string titleKey, string fmtKey) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldFault = MainShellWindow.MediaOpenFault;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = true;
        settings.Current.HasAcceptedAgeVerification = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            MainShellWindow.MediaOpenFault = a => { if (a == action) throw new InvalidOperationException("media failure"); };
            shell.OpenMediaRequest(action, "/nonexistent/clip.mp4");
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get(titleKey), dialog.Title);
            Assert.Equal(Loc.GetF(fmtKey, nameof(InvalidOperationException), "media failure"),
                dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.Contains("InvalidOperationException: media failure", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
        }
        finally
        {
            MainShellWindow.MediaOpenFault = oldFault;
            foreach (var w in shell.OwnedWindows.ToList()) w.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.ServiceProvider = oldProvider;
        }
        return Task.CompletedTask;
    });
}
