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
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RemoteMoreSignalTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<JObject> Signals = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v2/remote/emote") Signals.Add(JObject.Parse(await request.Content!.ReadAsStringAsync(ct)));
            var body = path == "/v2/remote/start" ? "{\"code\":\"ABC123\"}"
                : path == "/v2/remote/poll" ? "{\"controller_connected\":true}" : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

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
            for (int i = 0; i < 50 && fake.Signals.Count == 0; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
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
