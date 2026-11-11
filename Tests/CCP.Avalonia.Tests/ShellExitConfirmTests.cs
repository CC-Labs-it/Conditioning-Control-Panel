using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ShellExitConfirmTests
{
    private static bool ExitRequested(MainShellWindow shell) => (bool)typeof(MainShellWindow)
        .GetField("_exitRequested", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RunningEngineAsksBeforeExit(bool yes) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            CoreEngine.Start();
            Assert.True(CoreEngine.IsRunning);
            shell.BtnExit_Click(null, new RoutedEventArgs());
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("title_confirm_exit"), dialog.Title);
            Assert.Equal(Loc.Get("msg_engine_is_running_stop_and_exit"), dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.True(CoreEngine.IsRunning);
            Assert.False(ExitRequested(shell));
            dialog.FindControl<Button>(yes ? "BtnOk" : "BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
            Assert.Equal(!yes, CoreEngine.IsRunning);
            Assert.Equal(yes, ExitRequested(shell));
        }
        finally { CoreEngine.Stop(); shell.Close(); }
    });

    [Fact]
    public Task IdleExitDoesNotAsk() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = new MainShellWindow();
        shell.Show();
        shell.BtnExit_Click(null, new RoutedEventArgs());
        Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
        Assert.True(ExitRequested(shell));
        return Task.CompletedTask;
    });

    [Fact]
    public Task LockdownRefusesBeforeAskingAboutTheEngine() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var lockdown = new LockdownService();
        LockdownService.Current = lockdown;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            CoreEngine.Start();
            lockdown.Activate(TimeSpan.FromMinutes(30));
            shell.BtnExit_Click(null, new RoutedEventArgs());
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("title_lockdown"), dialog.Title);
            Assert.True(CoreEngine.IsRunning);
            Assert.False(ExitRequested(shell));
            dialog.Close();
        }
        finally { LockdownService.Current = null; CoreEngine.Stop(); shell.Close(); }
        return Task.CompletedTask;
    });
}
