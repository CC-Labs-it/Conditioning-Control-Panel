using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class RequiredDisplayNameTests
{
    private sealed class CheckWire(string body, Action? beforeReply = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            beforeReply?.Invoke();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    [Theory]
    [InlineData("{\"available\":false}", 0, false)]
    [InlineData("{\"available\":true}", 1, false)]
    [InlineData("{\"available\":true}", 0, true)]
    [InlineData("{}", 1, false)]
    public Task Taken_name_never_spends_rename_cooldown_but_unknown_check_still_renames(string body, int expected, bool switchAccount) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldV2 = AccountSeed.NewV2;
        var oldRename = CoreAccount.ChangeDisplayNameProvider;
        var service = new SettingsService();
        service.Current.UnifiedId = "test-account";
        CoreSettings.ServiceProvider = () => service;
        AccountSeed.NewV2 = () => new V2AuthService(handler: new CheckWire(body, () => { if (switchAccount) service.Current.UnifiedId = "replacement-account"; }));
        var calls = 0;
        CoreAccount.ChangeDisplayNameProvider = _ => { calls++; return Task.FromResult((false, (string?)"Server refused", (string?)null)); };
        var shell = new MainShellWindow();
        try
        {
            Assert.NotNull(await shell.SaveRequiredDisplayNameAsync("Valid Name"));
            Assert.Equal(expected, calls);
        }
        finally
        {
            shell.Close();
            CoreSettings.ServiceProvider = oldSettings;
            AccountSeed.NewV2 = oldV2;
            CoreAccount.ChangeDisplayNameProvider = oldRename;
        }
    });

    [Fact]
    public Task Required_mode_validates_and_keeps_a_failed_save_open_then_retries() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var pending = new TaskCompletionSource<string?>();
        var calls = 0;
        var d = new DisplayNameDialog("ab", _ => { calls++; return pending.Task; });
        try
        {
            d.Show();
            var input = d.FindControl<TextBox>("TxtDisplayName")!;
            var confirm = d.FindControl<Button>("BtnConfirm")!;
            Assert.False(d.FindControl<Button>("BtnCancel")!.IsVisible);
            Assert.True(d.FindControl<Button>("BtnClose")!.IsVisible);
            Assert.False(confirm.IsEnabled);
            input.Text = "bad@name";
            Dispatcher.UIThread.RunJobs();
            Assert.False(confirm.IsEnabled);
            Assert.True(d.FindControl<TextBlock>("TxtStatus")!.IsVisible);
            input.Text = "Valid Name";
            Dispatcher.UIThread.RunJobs();
            Assert.True(confirm.IsEnabled);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(input.IsEnabled);
            Assert.False(confirm.IsEnabled);
            pending.SetResult("Taken");
            for (int i = 0; i < 100 && !input.IsEnabled; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.True(d.IsVisible);
            Assert.True(input.IsEnabled);
            Assert.Equal("Taken", d.FindControl<TextBlock>("TxtStatus")!.Text);
            pending = new TaskCompletionSource<string?>();
            pending.SetResult(null);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(d.IsVisible);
            Assert.Equal("Valid Name", d.DisplayName);
            Assert.Equal(2, calls);
        }
        finally { d.Close(); }
    });

    [Fact]
    public Task Panic_closes_required_mode_and_late_save_does_not_touch_closed_window() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var pending = new TaskCompletionSource<string?>();
        var d = new DisplayNameDialog("Valid Name", _ => pending.Task);
        try
        {
            d.Show();
            d.FindControl<Button>("BtnConfirm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var surface in PanicSurfaces.All)
                if (surface.Id == "display-name") surface.Stop(null);
            Assert.False(d.IsVisible);
            pending.SetResult(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", d.DisplayName);
        }
        finally { d.Close(); }
        return Task.CompletedTask;
    });
}
