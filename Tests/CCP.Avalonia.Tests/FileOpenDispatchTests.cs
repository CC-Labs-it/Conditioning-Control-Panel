using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class FileOpenDispatchTests
{
    [Fact]
    public Task OpenFailureIsShownToTheUser() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowMediaOpenError(new InvalidOperationException("media failure"));
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal("media failure", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            dialog.Close();
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
