using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RequiredDisplayNameTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public async Task Required_name_validates_retries_inline_and_saves_before_closing()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var pending = new TaskCompletionSource<string?>();
            var calls = 0;
            var d = new DisplayNameDialog("ab", _ => { calls++; return pending.Task; });
            var input = d.FindControl<TextBox>("TxtDisplayName")!;
            var confirm = d.FindControl<Button>("BtnConfirm")!;
            var status = d.FindControl<TextBlock>("TxtStatus")!;
            var closed = false;
            d.Closed += (_, _) => closed = true;
            d.Show();
            try
            {
                Assert.False(d.FindControl<Button>("BtnCancel")!.IsVisible);
                Assert.True(d.FindControl<Button>("BtnClose")!.IsVisible);
                Assert.False(confirm.IsEnabled);
                input.Text = "bad@name";
                Dispatcher.UIThread.RunJobs();
                Assert.False(confirm.IsEnabled);
                Assert.True(status.IsVisible);
                input.Text = "Good name";
                Dispatcher.UIThread.RunJobs();
                Assert.True(confirm.IsEnabled);
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(input.IsEnabled);
                Assert.False(closed);
                pending.SetResult("Name taken");
                Dispatcher.UIThread.RunJobs();
                Assert.False(closed);
                Assert.Equal("Name taken", status.Text);
                Assert.True(input.IsEnabled);
                pending = new TaskCompletionSource<string?>();
                confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pending.SetResult(null);
                Dispatcher.UIThread.RunJobs();
                Assert.True(closed);
                Assert.Equal("Good name", d.DisplayName);
                Assert.Equal(2, calls);
            }
            finally { d.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Closing_the_name_ask_does_not_repeat_it_for_the_same_account()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var shell = new MainShellWindow();
            var opened = new List<DisplayNameDialog>();
            using var hook = Window.WindowOpenedEvent.AddClassHandler<DisplayNameDialog>((d, _) =>
            {
                opened.Add(d);
                Dispatcher.UIThread.Post(() => d.Close(false));
            });
            try
            {
                shell.Show();
                CoreAccount.IsLoggedInProvider = () => true;
                CoreAccount.UnifiedUserId = "name-one";
                var result = new LoginDialog.LoginResult { DisplayName = "", ServerNeedsDisplayName = true };
                await shell.AskDisplayNameAfterSignIn(shell, result);
                await shell.AskDisplayNameAfterSignIn(shell, result);
                Assert.Single(opened);
                CoreAccount.UnifiedUserId = "name-two";
                await shell.AskDisplayNameAfterSignIn(shell, result);
                Assert.Equal(2, opened.Count);
            }
            finally { foreach (var d in opened) d.Close(); shell.Close(); }
        });
    }

    [Fact]
    public async Task Replay_switch_loads_and_saves_the_setting()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            CoreSettings.Current.MandatoryVideoReplayAfterFailedCheck = false;
            var view = new VideoFeatureControl();
            var toggle = view.FindControl<CheckBox>("ChkReplayFailedCheck")!;
            Assert.False(toggle.IsChecked);
            toggle.IsChecked = true;
            Assert.True(CoreSettings.Current.MandatoryVideoReplayAfterFailedCheck);
            return Task.CompletedTask;
        });
    }
}
