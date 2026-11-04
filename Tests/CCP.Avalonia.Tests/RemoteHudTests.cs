using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public class RemoteHudTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public string Poll = "{}";
        public readonly List<string> Signals = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
            if (path == "/v2/remote/emote") Signals.Add((string)body["text"]!);
            var result = path == "/v2/remote/start" ? "{\"code\":\"ABC123\"}" : path == "/v2/remote/poll" ? Poll : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result) };
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public Task Hud_lifetime_signals_elapsed_and_stop_debounce() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var server = new FakeRelay();
        var stops = new List<string>();
        using var relay = new RemoteRelay(() => "token", () => "uid", "1", (_, _) => null,
            force => { if (force) stops.Add("relay"); }, server) { AutoPoll = false };
        var clock = new Clock();
        using var hud = new RemoteHudWindow(relay, () => stops.Add("panic")) { Clock = clock };
        hud.Refresh();
        Assert.False(hud.IsVisible);
        await relay.StartAsync("light");
        server.Poll = "{\"controller_connected\":true}";
        await relay.PollOnceAsync();
        hud.Refresh();
        Assert.True(hud.IsVisible);
        clock.Now = clock.Now.AddSeconds(65);
        hud.RecordAction("show_spiral");
        Assert.Contains("1:05", hud.Detail.Text);
        Assert.Contains(Loc.GetF("remote_hud_last", "Spiral"), hud.Detail.Text);
        hud.MoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for (var i = 0; i < 200 && server.Signals.Count == 0; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.Equal(new[] { "more" }, server.Signals);
        await hud.SignalAsync("stop");
        await hud.SignalAsync("stop");
        Assert.Equal(new[] { "more", "stop" }, server.Signals);
        Assert.Equal(new[] { "panic", "relay" }, stops);
        clock.Now = clock.Now.AddMilliseconds(2500);
        await hud.SignalAsync("stop");
        Assert.Equal(3, server.Signals.Count);
        server.Poll = "{\"controller_connected\":false}";
        await relay.PollOnceAsync();
        hud.Refresh();
        Assert.False(hud.IsVisible);
        server.Poll = "{\"controller_connected\":true}";
        await relay.PollOnceAsync();
        hud.Refresh();
        Assert.True(hud.IsVisible);
        Assert.Equal("0:00", hud.Detail.Text);
        await relay.StopAsync();
        hud.Refresh();
        Assert.False(hud.IsVisible);
        Dispatcher.UIThread.RunJobs();
    });
    [Fact]
    public void Stop_button_uses_local_panic_only_when_enabled() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var (enabled, key) = (settings.PanicKeyEnabled, settings.PanicKey);
        var old = PanicSurfaces.All;
        var stopped = 0;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stopped++) };
        var shell = new MainShellWindow();
        try
        {
            settings.PanicKeyEnabled = false;
            shell.StopFromRemoteHud();
            Assert.Equal(0, stopped);
            settings.PanicKeyEnabled = true;
            settings.PanicKey = "F8";
            shell.StopFromRemoteHud();
            Assert.Equal(1, stopped);
        }
        finally
        {
            shell.Close();
            PanicSurfaces.All = old;
            (settings.PanicKeyEnabled, settings.PanicKey) = (enabled, key);
        }
    });

}
