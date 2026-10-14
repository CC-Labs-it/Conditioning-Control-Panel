using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// UsernamePickerDialog against WPF Dialogs/UsernamePickerDialog.xaml.cs: the local name rules,
/// Confirm gating, Cancel and the debounced availability lifecycle. Valid names use a fake HTTP
/// handler: no test contacts the hardcoded production endpoint.
/// </summary>
public sealed class UsernamePickerDialogTests
{
    private static void SetupHeadless()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
    }

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task LocalRules_GateConfirm_WithWpfTextAndColours() =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetupHeadless();
            var dlg = new UsernamePickerDialog();
            dlg.ConfigureForNewUser();
            dlg.Show();
            try
            {
                Assert.False(dlg.FindControl<Border>("OgWelcomePanel")!.IsVisible);
                Assert.False(dlg.FindControl<StackPanel>("SuggestionPanel")!.IsVisible);

                var box = dlg.FindControl<TextBox>("TxtUsername")!;
                var status = dlg.FindControl<TextBlock>("TxtAvailability")!;
                var confirm = dlg.FindControl<Button>("BtnConfirm")!;

                void Expect(string text, string key, IBrush colour)
                {
                    box.Text = text;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(Loc.Get(key), status.Text);
                    Assert.Same(colour, status.Foreground);
                    Assert.False(confirm.IsEnabled);
                }

                Expect("ab", "login_name_min_3_chars", Brushes.Orange);
                Expect(new string('x', 31), "login_name_max_30_chars", Brushes.Orange);
                Expect("   ", "login_enter_unique_display_name", Brushes.Gray);

                // A disabled Confirm is a no-op, as WPF's _isAvailable guard.
                Click(confirm);
                Assert.True(dlg.IsVisible);
                Assert.Null(dlg.ChosenDisplayName);
            }
            finally { dlg.Close(); }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Cancel_ClosesFalse_WithNoName() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            SetupHeadless();
            var owner = new Window();
            owner.Show();
            try
            {
                var dlg = new UsernamePickerDialog();
                var shown = dlg.ShowDialog<bool?>(owner);
                dlg.FindControl<TextBox>("TxtUsername")!.Text = "ab";
                Click(dlg.FindControl<Button>("BtnCancel")!);
                Assert.False(await shown);
                Assert.Null(dlg.ChosenDisplayName);
            }
            finally { owner.Close(); }
        });
    private sealed class AvailabilityTransport : HttpMessageHandler
    {
        internal readonly List<(Uri Uri, TaskCompletionSource<HttpResponseMessage> Response)> Requests = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((request.RequestUri!, reply));
            return reply.Task; // deliberately ignores cancellation, to exercise the UI's stale-reply guard
        }
        internal void Reply(int index, string json, HttpStatusCode code = HttpStatusCode.OK) =>
            Requests[index].Response.SetResult(new HttpResponseMessage(code) { Content = new StringContent(json) });
    }

    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public Task DebounceChecksOnlyTheLatestTrimmedName_AndConfirmClosesTrue() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            SetupHeadless();
            using var transport = new AvailabilityTransport();
            using var client = new HttpClient(transport);
            var owner = new Window(); owner.Show();
            var dlg = new UsernamePickerDialog(client);
            var shown = dlg.ShowDialog<bool?>(owner);
            try
            {
                var box = dlg.FindControl<TextBox>("TxtUsername")!;
                var status = dlg.FindControl<TextBlock>("TxtAvailability")!;
                var confirm = dlg.FindControl<Button>("BtnConfirm")!;
                box.Text = "old name";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("login_checking_availability"), status.Text);
                Assert.Same(Brushes.Gray, status.Foreground);
                Assert.False(confirm.IsEnabled);
                Assert.Empty(transport.Requests); // still inside the 500 ms WPF debounce
                box.Text = "  New Name+  ";
                Dispatcher.UIThread.RunJobs();
                await Until(() => transport.Requests.Count == 1);
                Assert.Equal("https://codebambi-proxy.vercel.app/user/check-display-name?display_name=New%20Name%2B", transport.Requests[0].Uri.AbsoluteUri);
                transport.Reply(0, "{\"available\":true}");
                await Until(() => confirm.IsEnabled);
                Assert.Equal(Loc.GetF("login_name_available", "New Name+"), status.Text);
                Assert.Same(Brushes.LightGreen, status.Foreground);
                Click(confirm);
                Assert.True(await shown);
                Assert.Equal("New Name+", dlg.ChosenDisplayName);
            }
            finally { dlg.Close(); owner.Close(); }
        });

    [Theory]
    [InlineData("{\"available\":false}", HttpStatusCode.OK)]
    [InlineData("{\"available\":true}", HttpStatusCode.ServiceUnavailable)]
    [InlineData("not json", HttpStatusCode.OK)]
    public Task TakenOrFailedChecks_KeepConfirmDisabled(string body, HttpStatusCode code) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            SetupHeadless();
            using var transport = new AvailabilityTransport();
            using var client = new HttpClient(transport);
            var dlg = new UsernamePickerDialog(client); dlg.Show();
            try
            {
                dlg.FindControl<TextBox>("TxtUsername")!.Text = "SomeName";
                await Until(() => transport.Requests.Count == 1);
                transport.Reply(0, body, code);
                var status = dlg.FindControl<TextBlock>("TxtAvailability")!;
                await Until(() => status.Text == Loc.GetF("login_name_already_taken", "SomeName"));
                Assert.Same(Brushes.Orange, status.Foreground);
                var confirm = dlg.FindControl<Button>("BtnConfirm")!;
                Assert.False(confirm.IsEnabled);
                Click(confirm);
                Assert.True(dlg.IsVisible);
                Assert.Null(dlg.ChosenDisplayName);
            }
            finally { dlg.Close(); }
        });

    [Fact]
    public Task OldRepliesAndRepliesAfterClose_CannotEnableConfirm() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            SetupHeadless();
            using var transport = new AvailabilityTransport();
            using var client = new HttpClient(transport);
            var dlg = new UsernamePickerDialog(client); dlg.Show();
            var box = dlg.FindControl<TextBox>("TxtUsername")!;
            var status = dlg.FindControl<TextBlock>("TxtAvailability")!;
            var confirm = dlg.FindControl<Button>("BtnConfirm")!;
            try
            {
                box.Text = "PreviousName";
                await Until(() => transport.Requests.Count == 1);
                box.Text = "ab";
                Dispatcher.UIThread.RunJobs();
                transport.Reply(0, "{\"available\":true}");
                await Task.Delay(100);
                Assert.Equal(Loc.Get("login_name_min_3_chars"), status.Text);
                Assert.False(confirm.IsEnabled);
                box.Text = "ClosingName";
                await Until(() => transport.Requests.Count == 2);
                var beforeClose = status.Text;
                dlg.Close();
                transport.Reply(1, "{\"available\":true}");
                await Task.Delay(100);
                Assert.False(dlg.IsVisible);
                Assert.False(confirm.IsEnabled);
                Assert.Equal(beforeClose, status.Text);
                Assert.Null(dlg.ChosenDisplayName);
            }
            finally { dlg.Close(); }
        });

}
