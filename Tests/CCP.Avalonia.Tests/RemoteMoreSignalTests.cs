using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

// The relay and confirmation delay are process-wide while these tests await responses.
[Collection(RunsAloneCollection.Name)]
public sealed class RemoteMoreSignalTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        private readonly object _sync = new();
        private readonly List<JObject> _signals = new();
        private TaskCompletionSource<bool> _signalAdded = NewSignal();
        public IReadOnlyList<JObject> Signals { get { lock (_sync) return _signals.ToArray(); } }
        public TaskCompletionSource<bool>? NextSignalGate;
        public TimeSpan SignalDelay;

        private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task WaitForSignals(int count)
        {
            while (true)
            {
                Task wait;
                lock (_sync)
                {
                    if (_signals.Count >= count) return;
                    wait = _signalAdded.Task;
                }
                await wait.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v2/remote/emote")
            {
                if (SignalDelay > TimeSpan.Zero) await Task.Delay(SignalDelay, ct);
                var signal = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
                var gate = Interlocked.Exchange(ref NextSignalGate, null);
                TaskCompletionSource<bool> added;
                lock (_sync)
                {
                    _signals.Add(signal);
                    added = _signalAdded;
                    _signalAdded = NewSignal();
                }
                added.TrySetResult(true);
                if (gate != null) await gate.Task;
            }
            var body = path == "/v2/remote/start" ? "{\"code\":\"ABC123\"}"
                : path == "/v2/remote/poll" ? "{\"controller_connected\":true}" : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    // The relay answers on the thread pool; a fixed count of yields ran out on a loaded CI runner.
    private static async Task Until(Func<bool> done)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!done() && Environment.TickCount64 < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Delay(1); }
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task Confirmation_expires_restarts_and_cancels_on_both_surfaces() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldRelay = RemoteControlTabView.Relay;
        var oldDelay = RemoteControlTabView.MoreConfirmationDelay;
        var oldAttention = MainShellWindow.RemoteJoinAttention;
        var oldSink = global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        var pending = new List<(TimeSpan Duration, CancellationToken Token, TaskCompletionSource<bool> Completion)>();
        var confirmationAdded = new SemaphoreSlim(0);
        RemoteControlTabView.MoreConfirmationDelay = (duration, token) =>
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => completion.TrySetCanceled(token));
            pending.Add((duration, token, completion));
            confirmationAdded.Release();
            return completion.Task;
        };
        var fake = new FakeRelay();
        using var relay = new RemoteRelay(() => "token", () => "uid", "1", (_, _) => null, _ => { }, fake) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        MainShellWindow.RemoteJoinAttention = _ => { };
        global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        Window? host = null;
        MainShellWindow? shell = null;
        TaskCompletionSource<bool>? heldSignal = null;
        try
        {
            var view = new RemoteControlTabView();
            host = new Window { Content = view, Width = 1200, Height = 900 };
            shell = new MainShellWindow();
            host.Show();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await relay.StartAsync("light");
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            var tabButton = view.FindControl<Button>("BtnRemoteMore")!;
            var tabStatus = view.FindControl<TextBlock>("TxtRemoteMoreStatusTab")!;
            var emoteStatus = view.FindControl<TextBlock>("TxtEmoteStatus")!;
            emoteStatus.Text = "emote result";
            var shellButton = shell.Named<Button>("BtnRemoteMoreOverlay")!;
            var shellStatus = shell.Named<TextBlock>("TxtRemoteMoreStatus")!;
            var expected = string.Format(Loc.Get("remote_hud_sent"), Loc.Get("remote_hud_more"));

            async Task WaitForConfirmation(int count)
            {
                while (pending.Count < count)
                    Assert.True(await confirmationAdded.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(count, pending.Count);
            }
            async Task Click(Button button, int count)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => pending.Count >= count);
                Assert.Equal(count, pending.Count);
                Assert.Equal(TimeSpan.FromMilliseconds(1600), pending[count - 1].Duration);
            }
            async Task Clear(TextBlock status, int index)
            {
                pending[index].Completion.TrySetResult(true);
                await Until(() => status.Text == "");
                Assert.Equal("", status.Text);
            }

            await Click(tabButton, 1);
            Assert.Equal(expected, tabStatus.Text);
            var gate = heldSignal = fake.NextSignalGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.SignalDelay = TimeSpan.FromMilliseconds(300); // The request may arrive after many dispatcher turns.
            tabButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => fake.Signals.Count >= 2);
            Assert.Equal(2, fake.Signals.Count);
            fake.SignalDelay = TimeSpan.Zero;
            Assert.Equal(expected, tabStatus.Text); // the first confirmation stays until the next response
            Assert.False(pending[0].Token.IsCancellationRequested);
            gate.SetResult(true);
            await Until(() => pending.Count >= 2);
            Assert.Equal(2, pending.Count);
            Assert.Equal(TimeSpan.FromMilliseconds(1600), pending[1].Duration);
            Assert.True(pending[0].Token.IsCancellationRequested);
            Assert.Equal(expected, tabStatus.Text);
            await Clear(tabStatus, 1);
            Assert.Equal("emote result", emoteStatus.Text);
            await Click(tabButton, 3);
            host.Close();
            Assert.True(pending[2].Token.IsCancellationRequested);
            Assert.Equal("", tabStatus.Text);

            await Click(shellButton, 4);
            Assert.Equal(expected, shellStatus.Text);
            await Click(shellButton, 5);
            Assert.True(pending[3].Token.IsCancellationRequested);
            Assert.Equal(expected, shellStatus.Text);
            await Clear(shellStatus, 4);
            await Click(shellButton, 6);
            shell.Close();
            Assert.True(pending[5].Token.IsCancellationRequested);
            Assert.Equal("", shellStatus.Text);
        }
        finally
        {
            heldSignal?.TrySetResult(true);
            if (relay.IsActive) await relay.StopAsync();
            host?.Close();
            shell?.Close();
            RemoteControlTabView.Relay = oldRelay;
            RemoteControlTabView.MoreConfirmationDelay = oldDelay;
            MainShellWindow.RemoteJoinAttention = oldAttention;
            global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = oldSink;
        }
    });

    [Fact]
    public Task More_button_tracks_connection_and_sends_signal() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldRelay = RemoteControlTabView.Relay;
        var fake = new FakeRelay();
        using var relay = new RemoteRelay(() => "token", () => "uid", "1", (_, _) => null, _ => { }, fake) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        Window? host = null;
        try
        {
            var view = new RemoteControlTabView();
            host = new Window { Content = view, Width = 1200, Height = 900 };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var button = view.FindControl<Button>("BtnRemoteMore")!;
            Assert.False(button.IsVisible);
            await relay.StartAsync("light");
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsVisible);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => fake.Signals.Count > 0);
            Assert.Single(fake.Signals);
            Assert.Equal("more", (string?)fake.Signals[0]["text"]);
            Assert.Equal("signal", (string?)fake.Signals[0]["kind"]);
            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(button.IsVisible);
        }
        finally
        {
            if (relay.IsActive) await relay.StopAsync();
            host?.Close();
            RemoteControlTabView.Relay = oldRelay;
        }
    });
}
