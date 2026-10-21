using System;
using System.Collections.Generic;
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

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    /// <summary>Stepped clock whose timers fire only on <see cref="Advance"/> (as LoginDeviceCodeTests).</summary>
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        private readonly List<T> _timers = new();
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
        {
            var t = new T(this, () => cb(state));
            _timers.Add(t);
            t.Change(due, period);
            return t;
        }
        public void Advance(TimeSpan by)
        {
            var end = _now + by;
            for (T? next; (next = _timers.Find(t => t.Due <= end)) != null;)
            {
                _now = next.Due!.Value;
                next.Due = null;
                next.Fire();
            }
            _now = end;
        }
        private sealed class T(Clock clock, Action fire) : ITimer
        {
            public DateTimeOffset? Due;
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) { Due = due == Timeout.InfiniteTimeSpan ? null : clock._now + due; return true; }
            public void Dispose() => Due = null;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

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

    /// <summary>Runs dispatcher work until <paramref name="done"/>; bounded, no wall-clock waits.</summary>
    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        Assert.True(done());
    }

    [Fact]
    public Task DebounceChecksOnlyTheLatestTrimmedName_AndConfirmClosesTrue() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            SetupHeadless();
            using var transport = new AvailabilityTransport();
            using var client = new HttpClient(transport);
            var clock = new Clock();
            var owner = new Window(); owner.Show();
            var dlg = new UsernamePickerDialog(client, clock);
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
                var stale = dlg.Check!;
                box.Text = "  New Name+  ";
                await stale; // the superseded debounce was cancelled and sent nothing
                clock.Advance(Debounce - TimeSpan.FromMilliseconds(1));
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(transport.Requests); // still inside the 500 ms WPF debounce
                clock.Advance(TimeSpan.FromMilliseconds(1));
                await Until(() => transport.Requests.Count == 1);
                Assert.Equal("https://codebambi-proxy.vercel.app/user/check-display-name?display_name=New%20Name%2B", transport.Requests[0].Uri.AbsoluteUri);
                transport.Reply(0, "{\"available\":true}");
                await dlg.Check!;
                Assert.True(confirm.IsEnabled);
                Assert.Equal(Loc.GetF("login_name_available", "New Name+"), status.Text);
                Assert.Same(Brushes.LightGreen, status.Foreground);
                Click(confirm);
                Assert.True(await shown);
                Assert.Equal("New Name+", dlg.ChosenDisplayName);
                Assert.Single(transport.Requests);
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
            var clock = new Clock();
            var dlg = new UsernamePickerDialog(client, clock); dlg.Show();
            try
            {
                dlg.FindControl<TextBox>("TxtUsername")!.Text = "SomeName";
                Dispatcher.UIThread.RunJobs(); // TextChanged is posted, so the debounce timer starts here
                clock.Advance(Debounce);
                await Until(() => transport.Requests.Count == 1);
                transport.Reply(0, body, code);
                await dlg.Check!;
                var status = dlg.FindControl<TextBlock>("TxtAvailability")!;
                Assert.Equal(Loc.GetF("login_name_already_taken", "SomeName"), status.Text);
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
            var clock = new Clock();
            var dlg = new UsernamePickerDialog(client, clock); dlg.Show();
            var box = dlg.FindControl<TextBox>("TxtUsername")!;
            var status = dlg.FindControl<TextBlock>("TxtAvailability")!;
            var confirm = dlg.FindControl<Button>("BtnConfirm")!;
            try
            {
                box.Text = "PreviousName";
                Dispatcher.UIThread.RunJobs();
                clock.Advance(Debounce);
                await Until(() => transport.Requests.Count == 1);
                var previous = dlg.Check!;
                box.Text = "ab";
                Dispatcher.UIThread.RunJobs();
                transport.Reply(0, "{\"available\":true}");
                await previous; // the stale reply has been fully handled
                Assert.Equal(Loc.Get("login_name_min_3_chars"), status.Text);
                Assert.False(confirm.IsEnabled);
                box.Text = "ClosingName";
                Dispatcher.UIThread.RunJobs();
                clock.Advance(Debounce);
                await Until(() => transport.Requests.Count == 2);
                var closing = dlg.Check!;
                var beforeClose = status.Text;
                dlg.Close();
                transport.Reply(1, "{\"available\":true}");
                await closing; // the post-close reply has been fully handled
                Assert.False(dlg.IsVisible);
                Assert.False(confirm.IsEnabled);
                Assert.Equal(beforeClose, status.Text);
                Assert.Null(dlg.ChosenDisplayName);
            }
            finally { dlg.Close(); }
        });
}
