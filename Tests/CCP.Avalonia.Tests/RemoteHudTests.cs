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
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;
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

    private static async Task<RemoteRelay> Connected(FakeRelay server, Action<bool> stopEffects)
    {
        var relay = new RemoteRelay(() => "token", () => "uid", "1", (_, _) => null, stopEffects, server) { AutoPoll = false };
        await relay.StartAsync("light");
        server.Poll = "{\"controller_connected\":true}";
        await relay.PollOnceAsync();
        return relay;
    }

    /// <summary>WPF RemoteHudRules.ConfirmFor and StopDebounce on the pill: a confirmation outlives the 1 s tick
    /// until 1.6 s have passed, a second Stop inside 2.5 s is swallowed, and a window manager close (WPF
    /// Closed += Teardown, :224) ends the tick so a late Refresh never shows a closed window.</summary>
    [Fact]
    public Task Confirmation_holds_stop_debounces_and_close_ends_the_tick() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var oldEnabled = settings.PanicKeyEnabled;
        settings.PanicKeyEnabled = true;   // the relay forces only when the panic key would run
        var server = new FakeRelay();
        var stops = new List<string>();
        using var relay = await Connected(server, force => { if (force) stops.Add("relay"); });
        var clock = new Clock();
        var hud = new RemoteHudWindow(() => relay, () => { stops.Add("panic"); return true; }, () => null) { Clock = clock };
        try
        {
            Assert.False(hud.ShowActivated);
            hud.Refresh();
            Assert.True(hud.IsVisible);
            await hud.OnSignalAsync("more", "remote_hud_more");
            var sent = Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_more"));
            Assert.Equal(sent, hud.Subline);
            clock.Now = clock.Now.AddMilliseconds(1599);
            hud.Refresh();
            Assert.Equal(sent, hud.Subline);
            clock.Now = clock.Now.AddMilliseconds(1);
            hud.Refresh();
            Assert.NotEqual(sent, hud.Subline);
            await hud.OnSignalAsync("stop", "remote_hud_stop");
            clock.Now = clock.Now.AddMilliseconds(2499);
            await hud.OnSignalAsync("stop", "remote_hud_stop");
            Assert.Equal(new[] { "more", "stop" }, server.Signals);
            Assert.Equal(new[] { "panic", "relay" }, stops);
            clock.Now = clock.Now.AddMilliseconds(1);
            await hud.OnSignalAsync("stop", "remote_hud_stop");
            Assert.Equal(3, server.Signals.Count);
            hud.Close();
            hud.Refresh();
            Assert.False(hud.IsVisible);
        }
        finally { hud.Dispose(); await relay.StopAsync(); Dispatcher.UIThread.RunJobs(); settings.PanicKeyEnabled = oldEnabled; }
    });

    [Fact]
    public Task Hud_uses_the_anchor_monitor_and_does_not_activate() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var server = new FakeRelay();
        using var relay = await Connected(server, _ => { });
        var anchor = new Window();
        var hud = new RemoteHudWindow(() => relay, () => true, () => anchor);
        try
        {
            anchor.Show();
            var primary = anchor.Screens.Primary!;
            var secondary = (Screen)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(primary, null)!;
            var area = new PixelRect(1920, 40, 2560, 1400);
            typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(secondary, area);
            typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(secondary, 1.5);
            Window? requested = null;
            hud.ScreenForAnchor = window => { requested = window; return secondary; };
            hud.Refresh();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(anchor, requested);
            Assert.False(hud.ShowActivated);
            Assert.Equal(area.X + (int)((area.Width - hud.Bounds.Width * secondary.Scaling) / 2), hud.Position.X);
            Assert.Equal(area.Y + (int)((10 - 12) * secondary.Scaling), hud.Position.Y);
        }
        finally { hud.Dispose(); anchor.Close(); await relay.StopAsync(); }
    });

    /// <summary>One HUD Stop click through the production wiring (shell.StopFromRemoteHud), on a fresh pill so
    /// the 2.5 s debounce never hides a press. Returns the relay's forced stops and the pill's confirmation.</summary>
    private static async Task<(int Forced, List<string> Signals, string? Detail)> HudStop(MainShellWindow shell)
    {
        var server = new FakeRelay();
        var forced = 0;
        using var relay = await Connected(server, force => { if (force) forced++; });
        var hud = new RemoteHudWindow(() => relay, shell.StopFromRemoteHud, () => null);
        hud.Refresh();
        await hud.OnSignalAsync("stop", "remote_hud_stop");
        var detail = hud.Subline;
        hud.Dispose();
        await relay.StopAsync();
        Dispatcher.UIThread.RunJobs();
        return (forced, server.Signals, detail);
    }

    /// <summary>Hard rule 6: the HUD Stop never reaches further than the panic key. Under Lockdown (panic key on,
    /// LockdownDisablePanicKey off) neither the local panic nor the relay's forced stop (session, engine, Chaster
    /// hold: RemoteCommands.StopEffects(force: true)) runs; the controller is still told. The same holds with the
    /// panic key off, where the HUD gate and the relay's Core gate (VoicePanicReach.PanicKeyRuns) must agree.</summary>
    [Fact]
    public Task Stop_button_is_refused_under_lockdown() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var (enabled, key, strict, ldPanic, ldStrict) = (settings.PanicKeyEnabled, settings.PanicKey, settings.StrictLockEnabled,
            settings.LockdownDisablePanicKey, settings.LockdownForceStrictLock);
        var old = PanicSurfaces.All;
        var oldLockdown = LockdownService.Current;
        var ld = new LockdownService();
        var stopped = 0;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stopped++) };
        var shell = new MainShellWindow();
        try
        {
            (settings.LockdownDisablePanicKey, settings.LockdownForceStrictLock) = (false, false);
            LockdownService.Current = ld;
            ld.Activate(TimeSpan.FromMinutes(30));
            (settings.PanicKeyEnabled, settings.PanicKey) = (true, "F8");
            Assert.True(MainShellWindow.LockdownActive);
            var (forced, signals, detail) = await HudStop(shell);
            Assert.Equal(0, stopped);
            Assert.Equal(0, forced);
            Assert.Equal(new[] { "stop" }, signals);
            Assert.Equal(Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_stop")), detail);
            // Lockdown with the panic key off: the HUD's local half and the relay's Core gate both refuse.
            settings.PanicKeyEnabled = false;
            (forced, signals, detail) = await HudStop(shell);
            Assert.Equal(0, stopped);
            Assert.Equal(0, forced);
            Assert.Equal(new[] { "stop" }, signals);
            Assert.Equal(Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_stop")), detail);
            Assert.True(ld.IsActive);
            ld.Deactivate();   // restores the pre-Lockdown panic flag
            settings.PanicKeyEnabled = true;
            (forced, signals, detail) = await HudStop(shell);
            Assert.Equal(1, stopped);
            Assert.Equal(1, forced);
            Assert.Equal(new[] { "stop" }, signals);
            Assert.Equal(Loc.Get("remote_hud_stopped"), detail);
        }
        finally
        {
            shell.Close();
            ld.Deactivate();
            LockdownService.Current = oldLockdown;
            PanicSurfaces.All = old;
            (settings.PanicKeyEnabled, settings.PanicKey, settings.StrictLockEnabled, settings.LockdownDisablePanicKey, settings.LockdownForceStrictLock)
                = (enabled, key, strict, ldPanic, ldStrict);
        }
    });

    /// <summary>Panic key off: no local panic and no forced relay stop; on a leash only the leash part runs
    /// (WPF LeashPanicKeyWhilePanicOff), as the key itself does.</summary>
    [Fact]
    public Task Stop_button_uses_local_panic_only_when_enabled() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = CoreSettings.Current;
        var (enabled, key) = (settings.PanicKeyEnabled, settings.PanicKey);
        var old = PanicSurfaces.All;
        var (oldService, oldRunner) = (LeashHead.Service, LeashTaskHost.Runner);
        var stopped = 0;
        PanicSurfaces.All = new[] { new PanicSurfaces.Surface("probe", _ => stopped++) };
        var shell = new MainShellWindow();
        try
        {
            settings.PanicKeyEnabled = false;
            var (forced, signals, detail) = await HudStop(shell);
            Assert.Equal(0, stopped);
            Assert.Equal(0, forced);
            Assert.Equal(new[] { "stop" }, signals);
            Assert.Equal(Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_stop")), detail);

            var leash = new LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
            leash.ApplyBlock(LeashHeadTests.BlockWithPunishment());
            Assert.NotNull(leash.Snapshot.Me);
            var runner = new PanicReachHeadTests.FakeRunner();
            (LeashHead.Service, LeashTaskHost.Runner) = (leash, runner);
            (forced, _, _) = await HudStop(shell);
            Assert.Equal(new[] { "park+session" }, runner.Calls);   // the leash-only stop, nothing more
            Assert.Equal(0, stopped);
            Assert.Equal(0, forced);
            (LeashHead.Service, LeashTaskHost.Runner) = (oldService, oldRunner);

            settings.PanicKeyEnabled = true;
            settings.PanicKey = "F8";
            (forced, _, _) = await HudStop(shell);
            Assert.Equal(1, stopped);
            Assert.Equal(1, forced);
        }
        finally
        {
            shell.Close();
            (LeashHead.Service, LeashTaskHost.Runner) = (oldService, oldRunner);
            PanicSurfaces.All = old;
            (settings.PanicKeyEnabled, settings.PanicKey) = (enabled, key);
        }
    });

}
