using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel.Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RequiredNameTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private sealed class Wire : HttpMessageHandler
    {
        public int Checks;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Checks++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"available":false}""") });
        }
    }

    // WPF MainWindow.DisplayNameAsk.cs:85-90. No window or font is needed for this transaction.
    [Fact]
    public async Task SuccessfulOwnNameNeverChecksAvailabilityButRefusalDoes()
    {
        var old = CoreAccount.ChangeDisplayNameProvider;
        var factory = AccountSeed.NewV2;
        var wire = new Wire();
        try
        {
            AccountSeed.NewV2 = () => new V2AuthService(() => null, wire);
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((true, (string?)null, (string?)"Own Name"));
            var saved = await MainShellWindow.ChangeRequiredDisplayNameAsync("Own Name");
            Assert.Null(saved.Error);
            Assert.Equal("Own Name", saved.Name);
            Assert.Equal(0, wire.Checks);
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((false, (string?)"refused", (string?)null));
            var refused = await MainShellWindow.ChangeRequiredDisplayNameAsync("Other");
            Assert.Equal(Loc.GetF("login_name_already_taken", "Other"), refused.Error);
            Assert.Null(refused.Name);
            Assert.Equal(1, wire.Checks);
        }
        finally { CoreAccount.ChangeDisplayNameProvider = old; AccountSeed.NewV2 = factory; }
    }

    // WPF DisplayNameDialog.xaml.cs:44-62,105-166,201-205.
    [Fact]
    public Task RequiredModeValidatesRetriesAndIgnoresAnAnswerAfterClose() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var owner = new Window();
        owner.Show();
        var answer = new TaskCompletionSource<string?>();
        int calls = 0;
        var d = new DisplayNameDialog(null, _ => { calls++; return answer.Task; });
        var shown = d.ShowDialog<bool>(owner);
        var input = d.FindControl<TextBox>("TxtDisplayName")!;
        var confirm = d.FindControl<Button>("BtnConfirm")!;
        void Click() => confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(d.FindControl<Button>("BtnCancel")!.IsVisible);
        Assert.True(d.FindControl<Button>("BtnClose")!.IsVisible);
        input.Text = "ab";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(confirm.IsEnabled);
        input.Text = "a@b";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(confirm.IsEnabled);
        Assert.Equal(Loc.Get("name_ask_bad_chars"), d.FindControl<TextBlock>("TxtStatus")!.Text);
        input.Text = "Own Name";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Click();
        Click();
        Assert.Equal(1, calls);
        Assert.False(input.IsEnabled);
        answer.SetResult("taken");
        await answer.Task;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(input.IsEnabled);
        Assert.True(confirm.IsEnabled);
        Assert.Equal("taken", d.FindControl<TextBlock>("TxtStatus")!.Text);
        answer = new TaskCompletionSource<string?>();
        Click();
        d.Close(false);
        answer.SetResult(null);
        await answer.Task;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(await shown);
        Assert.Equal("", d.DisplayName);
        owner.Close();
    });

    // WPF MainWindow.DisplayNameAsk.cs:25-30,58-59,78-108.
    [Fact]
    public Task SignInGateAndOwnNameSaveChangeBeforeAnyAvailabilityCheck() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.UserDisplayName);
        var change = CoreAccount.ChangeDisplayNameProvider;
        var factory = AccountSeed.NewV2;
        var wire = new Wire();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            AccountSeed.NewV2 = () => new V2AuthService(() => s, wire);
            s.UnifiedId = "u1";
            s.UserDisplayName = null;
            await shell.AskDisplayNameAfterSignIn(shell, null, false);
            Assert.Empty(shell.OwnedWindows);
            var ask = shell.AskDisplayNameAfterSignIn(shell, null, true);
            var d = Assert.Single(shell.OwnedWindows.OfType<DisplayNameDialog>());
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((true, (string?)null, (string?)"Own Name"));
            d.FindControl<TextBox>("TxtDisplayName")!.Text = "Own Name";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            d.FindControl<Button>("BtnConfirm")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Own Name", s.UserDisplayName);
            Assert.Equal(0, wire.Checks);
            CoreAccount.ChangeDisplayNameProvider = _ => Task.FromResult((false, (string?)"refused", (string?)null));
            Assert.Equal(Loc.GetF("login_name_already_taken", "Other"), await shell.SaveRequiredDisplayNameAsync("Other"));
            Assert.Equal(1, wire.Checks);
            Assert.Equal("Own Name", s.UserDisplayName);
        }
        finally
        {
            foreach (var d in shell.OwnedWindows.ToArray()) d.Close();
            shell.Close();
            (s.UnifiedId, s.UserDisplayName) = old;
            CoreAccount.ChangeDisplayNameProvider = change;
            AccountSeed.NewV2 = factory;
        }
    });
}
