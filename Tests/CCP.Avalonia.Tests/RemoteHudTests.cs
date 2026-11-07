using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
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
        // A second monitor right of the first: WPF Place (:345) centres the pill on the main window's monitor.
        var screen = (WorkingArea: new PixelRect(1920, 40, 2560, 1400), Scaling: 1.5);
        using var hud = new RemoteHudWindow(relay, () => stops.Add("panic"), () => screen) { Clock = clock };
        Assert.False(hud.ShowActivated);
        hud.Refresh();
        Assert.False(hud.IsVisible);
        await relay.StartAsync("light");
        server.Poll = "{\"controller_connected\":true}";
        await relay.PollOnceAsync();
        hud.Refresh();
        Assert.True(hud.IsVisible);
        Assert.Equal(40 + 15, hud.Position.Y);
        var width = hud.Bounds.Width > 0 ? hud.Bounds.Width : 360;
        Assert.Equal((int)(1920 + (2560 - width * 1.5) / 2), hud.Position.X);
        Assert.InRange(hud.Position.X, 1920, 1920 + 1280);
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
        // WPF RemoteHudRules.ConfirmFor: the confirmation outlives the 1 s tick until 1.6 s have passed.
        Assert.Equal(Loc.Get("remote_hud_stopped"), hud.Detail.Text);
        clock.Now = clock.Now.AddMilliseconds(1599);
        hud.Refresh();
        Assert.Equal(Loc.Get("remote_hud_stopped"), hud.Detail.Text);
        clock.Now = clock.Now.AddMilliseconds(1);
        hud.Refresh();
        Assert.StartsWith("1:06", hud.Detail.Text);
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
        // WPF keeps LastActionLabel across a reconnect (RemoteControlService.V2.cs:288 clears it at session end only).
        Assert.Equal("0:00  ·  " + Loc.GetF("remote_hud_last", "Spiral"), hud.Detail.Text);
        // A window manager close (WPF Closed += Teardown, :224) ends the tick; a late Refresh cannot Show a closed window.
        hud.Close();
        hud.Refresh();
        Assert.False(hud.IsVisible);
        await relay.StopAsync();
        Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    public Task Pill_has_the_wpf_tooltips() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        using var relay = new RemoteRelay(() => "token", () => "uid", "1", (_, _) => null, _ => { }, new FakeRelay()) { AutoPoll = false };
        using var hud = new RemoteHudWindow(relay, () => { }, () => null);
        Assert.Equal(Loc.Get("remote_hud_more_tip"), ToolTip.GetTip(hud.MoreButton));
        Assert.Equal(Loc.Get("remote_hud_stop_tip"), ToolTip.GetTip(hud.StopButton));
        Assert.False(hud.StopButton.Focusable);
        return Task.CompletedTask;
    });

    /// <summary>Hard rule 6: the HUD Stop never reaches further than the panic key, so Lockdown refuses it
    /// even with the panic key on (LockdownDisablePanicKey off).</summary>
    [Fact]
    public void Stop_button_is_refused_under_lockdown() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var (enabled, key, strict) = (settings.PanicKeyEnabled, settings.PanicKey, settings.StrictLockEnabled);
        var old = PanicSurfaces.All;
        var oldLockdown = LockdownService.Current;
        var ld = new LockdownService();
        var stopped = 0;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stopped++) };
        var shell = new MainShellWindow();
        try
        {
            LockdownService.Current = ld;
            ld.Activate(TimeSpan.FromMinutes(30));
            (settings.PanicKeyEnabled, settings.PanicKey) = (true, "F8");
            Assert.True(MainShellWindow.LockdownActive);
            shell.StopFromRemoteHud();
            Assert.Equal(0, stopped);
            ld.Deactivate();   // restores the pre-Lockdown panic flag
            settings.PanicKeyEnabled = true;
            shell.StopFromRemoteHud();
            Assert.Equal(1, stopped);
        }
        finally
        {
            shell.Close();
            ld.Deactivate();
            LockdownService.Current = oldLockdown;
            PanicSurfaces.All = old;
            (settings.PanicKeyEnabled, settings.PanicKey, settings.StrictLockEnabled) = (enabled, key, strict);
        }
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
