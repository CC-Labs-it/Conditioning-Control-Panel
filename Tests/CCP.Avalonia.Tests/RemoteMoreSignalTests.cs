using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
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

public sealed class RemoteMoreSignalTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly ConcurrentQueue<JObject> Signals = new();
        private readonly SemaphoreSlim _arrived = new(0);
        public TaskCompletionSource<bool>? NextSignalGate;
        public async Task WaitForSignals(int count)
        {
            while (Signals.Count < count)
                Assert.True(await _arrived.WaitAsync(TimeSpan.FromSeconds(10)), "Relay request did not arrive");
            Assert.Equal(count, Signals.Count);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v2/remote/emote")
            {
                var gate = Interlocked.Exchange(ref NextSignalGate, null);
                if (Signals.Count == 1) await Task.Delay(250, ct); // the delayed arrival that exposed the CI race
                Signals.Enqueue(JObject.Parse(await request.Content!.ReadAsStringAsync(ct)));
                _arrived.Release();
                if (gate != null) await gate.Task;
            }
            var body = path == "/v2/remote/start" ? "{\"code\":\"ABC123\"}"
                : path == "/v2/remote/poll" ? "{\"controller_connected\":true}" : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static async Task WaitUntil(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public Task Confirmation_expires_restarts_and_cancels_on_both_surfaces() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldRelay = RemoteControlTabView.Relay;
        var oldDelay = RemoteControlTabView.MoreConfirmationDelay;
        var oldAttention = MainShellWindow.RemoteJoinAttention;
        var oldSink = global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        var pending = new List<(TimeSpan Duration, CancellationToken Token, TaskCompletionSource<bool> Completion)>();
        RemoteControlTabView.MoreConfirmationDelay = (duration, token) =>
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            token.Register(() => completion.TrySetCanceled(token));
            pending.Add((duration, token, completion));
            return completion.Task;
        };
        var fake = new FakeRelay();
        using var relay = new RemoteRelay(() => "token", () => "uid", "1", (_, _) => null, _ => { }, fake) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        MainShellWindow.RemoteJoinAttention = _ => { };
        global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        Window? host = null;
        MainShellWindow? shell = null;
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

            async Task Click(Button button, int count)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(() => pending.Count >= count);
                Assert.Equal(count, pending.Count);
                Assert.Equal(TimeSpan.FromMilliseconds(1600), pending[count - 1].Duration);
            }
            async Task Clear(TextBlock status, int index)
            {
                pending[index].Completion.TrySetResult(true);
                await WaitUntil(() => status.Text == "");
                Assert.Equal("", status.Text);
            }

            await Click(tabButton, 1);
            Assert.Equal(expected, tabStatus.Text);
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref fake.NextSignalGate, gate);
            tabButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await fake.WaitForSignals(2);
            Assert.Equal(expected, tabStatus.Text); // the first confirmation stays until the next response
            Assert.False(pending[0].Token.IsCancellationRequested);
            gate.SetResult(true);
            await WaitUntil(() => pending.Count >= 2);
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
            await fake.WaitForSignals(1);
            Assert.Single(fake.Signals);
            var signal = fake.Signals.Single();
            Assert.Equal("more", (string?)signal["text"]);
            Assert.Equal("signal", (string?)signal["kind"]);
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
