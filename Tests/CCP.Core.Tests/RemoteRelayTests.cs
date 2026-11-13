using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core RemoteRelay against a fake relay: no network. Rules from WPF RemoteControlService.</summary>
[Collection(SessionStatics.Name)]
public sealed class RemoteRelayTests
{
    // WPF RemoteControlService.cs:1476/:1488 dispatch; :980 panic/session end stop and re-arm on the saved
    // switch; :1159 a leave only cancels pulses; :1020/:1179 every card closes; :964/:1154 over the wearer's own
    // run neither touches autonomy nor the cards; :921 the status lists a running autonomy.
    [Fact]
    public async Task Remote_autonomy_and_lock_card_route_to_the_head_and_each_teardown_does_what_WPF_does()
    {
        var s = CoreSettings.Current;
        var saved = (RemoteCommands.AutonomyCommand, RemoteCommands.CancelAutonomyPulses, CoreLockCard.ShowHandler,
            CoreLockCard.CloseAllHandler, s.AutonomyModeEnabled, s.StrictLockEnabled, s.PanicKeyEnabled,
            RemoteCommands.AutonomyRunning, s.AudioOnlySession, s.TotalSessions, s.FlashEnabled, s.LockCardEnabled);
        var log = new List<string>();
        try
        {
            (RemoteCommands.AutonomyCommand, CoreLockCard.ShowHandler) = (null, null);
            foreach (var a in new[] { "start_autonomy", "stop_autonomy", "trigger_lock_card" })
                Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute(a, null));

            RemoteCommands.AutonomyCommand = on => { log.Add(on ? "start" : "stop"); return true; };
            RemoteCommands.CancelAutonomyPulses = () => log.Add("pulses");
            CoreLockCard.ShowHandler = test => log.Add(test ? "test card" : "card");
            CoreLockCard.CloseAllHandler = () => log.Add("close");
            Assert.Null(RemoteCommands.Execute("start_autonomy", null));
            Assert.Null(RemoteCommands.Execute("stop_autonomy", null));
            Assert.Null(RemoteCommands.Execute("trigger_lock_card", null));
            Assert.Equal(new[] { "start", "stop", "card" }, log);

            log.Clear();
            RemoteCommands.StopEffects(false);   // the controller left: autonomy keeps running
            Assert.Equal(new[] { "pulses", "close" }, log);

            s.AutonomyModeEnabled = true;
            log.Clear();
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.Equal(new[] { "pulses", "stop", "start", "close" }, log);
            s.AutonomyModeEnabled = false;
            log.Clear();
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.Equal(new[] { "pulses", "stop", "close" }, log);

            s.AutonomyModeEnabled = true;
            var f = new FakeRelay();
            using var r = Relay(f);
            await r.StartAsync("full");
            foreach (var on in new[] { true, false })
            {
                RemoteCommands.AutonomyRunning = () => on;
                await r.PushStatusNowAsync();
                var services = f.Seen.Last(x => x.Path == "/v2/remote/status").Body["active_services"]!.ToObject<string[]>()!;
                Assert.Equal(on, services.Contains("autonomy"));
            }
            log.Clear();
            f.Answer = _ => Json(HttpStatusCode.NotFound, "{}");
            await r.PollOnceAsync();   // the session ended
            Assert.False(r.IsActive);
            Assert.Equal(new[] { "pulses", "stop", "start" }, log);

            // The wearer's own run (WPF LeavesLocalRun, ReleaseRemoteHoldings :1125): a leave and a session end
            // leave its services, autonomy and cards alone. Flash off: armed, but never ticks.
            (s.AudioOnlySession, s.FlashEnabled, s.LockCardEnabled) = (true, false, true);
            CoreEngine.Start();
            CoreFlash.Start();
            LockCardScheduler.Instance.Start();
            log.Clear();
            RemoteCommands.StopEffects(false);
            var f2 = new FakeRelay();
            using var r2 = Relay(f2);
            await r2.StartAsync("full");
            f2.Answer = _ => Json(HttpStatusCode.NotFound, "{}");
            await r2.PollOnceAsync();
            Assert.False(r2.IsActive);
            Assert.Empty(log);
            Assert.True(CoreFlash.IsRunning);
            Assert.True(LockCardScheduler.Instance.IsRunning);
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));   // panic still tears it all down
            Assert.Equal(new[] { "pulses", "stop", "start", "close" }, log);

            RemoteCommands.AutonomyCommand = _ => false;   // the shell refused: switch, consent or entitlement
            Assert.Equal(RemoteCommands.AutonomyOff, RemoteCommands.Execute("start_autonomy", null));
        }
        finally
        {
            CoreEngine.Stop();
            CoreFlash.Stop();
            LockCardScheduler.Instance.Stop();
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (RemoteCommands.AutonomyCommand, RemoteCommands.CancelAutonomyPulses, CoreLockCard.ShowHandler,
                CoreLockCard.CloseAllHandler, s.AutonomyModeEnabled, s.StrictLockEnabled, s.PanicKeyEnabled,
                RemoteCommands.AutonomyRunning, s.AudioOnlySession, s.TotalSessions, s.FlashEnabled, s.LockCardEnabled) = saved;
        }
    }

    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<(string Path, JObject Body)> Seen = new();
        public Func<string, HttpResponseMessage> Answer = path => path switch
        {
            "/v2/remote/start" => Json(HttpStatusCode.OK, "{\"code\":\"ABC123\"}"),
            _ => Json(HttpStatusCode.OK, "{}"),
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.RequestUri!.AbsolutePath, JObject.Parse(await r.Content!.ReadAsStringAsync(ct))));
            return Answer(r.RequestUri.AbsolutePath);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode s, string body) => new(s) { Content = new StringContent(body) };

    private readonly List<string> _ran = new();
    private readonly List<bool> _stops = new();

    // RemoteWearer is one static session per app: a test that started a relay and never ended it must not
    // hand a stale snapshot to the next one.
    public RemoteRelayTests() => ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();

    // WPF RemoteEndRule.cs:26-27, EndSessionNow :344: a leash cut over the wearer's own run still cancels the
    // pulses, closes every card (:1020) and stops autonomy, re-armed on the saved switch (:980-985).
    [Fact]
    public async Task Leash_cut_over_a_local_run_closes_cards_and_resets_autonomy()
    {
        var s = CoreSettings.Current;
        var saved = (RemoteCommands.AutonomyCommand, RemoteCommands.CancelAutonomyPulses, CoreLockCard.CloseAllHandler,
            s.AutonomyModeEnabled, s.StrictLockEnabled, s.PanicKeyEnabled, s.AudioOnlySession, s.TotalSessions);
        var log = new List<string>();
        try
        {
            RemoteCommands.AutonomyCommand = on => { log.Add(on ? "start" : "stop"); return true; };
            RemoteCommands.CancelAutonomyPulses = () => log.Add("pulses");
            CoreLockCard.CloseAllHandler = () => log.Add("close");
            (s.AutonomyModeEnabled, s.AudioOnlySession) = (true, true);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, f) { AutoPoll = false };
            await r.StartAsync("full");
            CoreEngine.Start();
            log.Clear();
            r.EndSessionNow();
            Assert.False(r.IsActive);
            Assert.True(CoreEngine.IsRunning);   // the wearer's engine stays, as WPF
            Assert.Equal(new[] { "pulses", "close", "pulses", "stop", "start" }, log);
        }
        finally
        {
            CoreEngine.Stop();
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (RemoteCommands.AutonomyCommand, RemoteCommands.CancelAutonomyPulses, CoreLockCard.CloseAllHandler,
                s.AutonomyModeEnabled, s.StrictLockEnabled, s.PanicKeyEnabled, s.AudioOnlySession, s.TotalSessions) = saved;
        }
    }

    private RemoteRelay Relay(FakeRelay f) => new(() => "tok", () => "uid-1", "9.9.9",
        (a, _) => { _ran.Add(a); return null; }, force => _stops.Add(force), f) { AutoPoll = false };

    private static void Poll(FakeRelay f, string json) =>
        f.Answer = path => path == "/v2/remote/poll" ? Json(HttpStatusCode.OK, json) : Json(HttpStatusCode.OK, "{}");

    [Fact]
    public async Task StatusReportsActiveOverlaysAndMindWipe()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldMindWipe = CoreMindWipe.IsRunningProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var mindWipeRunning = true;
        CoreMindWipe.IsRunningProvider = () => mindWipeRunning;
        settings.Current.PinkFilterEnabled = true;
        settings.Current.SpiralEnabled = true;
        var fake = new FakeRelay();
        using var relay = Relay(fake);
        try
        {
            await relay.StartAsync("free");
            Poll(fake, "{\"controller_connected\":true}");
            await relay.PollOnceAsync();
            var active = fake.Seen.Last(x => x.Path == "/v2/remote/status").Body["active_services"]!.Values<string>().ToArray();
            Assert.Contains("pink_filter", active);
            Assert.Contains("spiral", active);
            Assert.Contains("mind_wipe", active);
            settings.Current.PinkFilterEnabled = false;
            settings.Current.SpiralEnabled = false;
            mindWipeRunning = false;
            var later = DateTime.UtcNow.AddSeconds(16);
            relay.Now = () => later;
            await relay.PollOnceAsync();
            active = fake.Seen.Last(x => x.Path == "/v2/remote/status").Body["active_services"]!.Values<string>().ToArray();
            Assert.DoesNotContain("pink_filter", active);
            Assert.DoesNotContain("spiral", active);
            Assert.DoesNotContain("mind_wipe", active);
        }
        finally
        {
            await relay.StopAsync();
            CoreSettings.ServiceProvider = oldSettings;
            CoreMindWipe.IsRunningProvider = oldMindWipe;
        }
    }

    [Theory]
    [InlineData("show_pink_filter", true, true)]
    [InlineData("stop_pink_filter", true, false)]
    [InlineData("show_spiral", false, true)]
    [InlineData("stop_spiral", false, false)]
    public void Overlay_commands_set_the_saved_switch(string verb, bool pink, bool enabled)
    {
        var old = CoreSettings.ServiceProvider;
        var oldRefresh = RemoteCommands.RefreshOverlays;
        RemoteCommands.RefreshOverlays = () => { };
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            settings.Current.PinkFilterEnabled = settings.Current.SpiralEnabled = !enabled;
            Assert.Null(RemoteCommands.Execute(verb, null));
            Assert.Equal(enabled, pink ? settings.Current.PinkFilterEnabled : settings.Current.SpiralEnabled);
        }
        finally { RemoteCommands.StopOverlays(); RemoteCommands.ReleaseOverlayStop(); RemoteCommands.RefreshOverlays = oldRefresh; CoreSettings.ServiceProvider = old; }
    }

    [Theory]
    [InlineData("set_pink_opacity", -1, 0)]
    [InlineData("set_pink_opacity", 90, 50)]
    [InlineData("set_spiral_opacity", 190, 100)]
    [InlineData("set_spiral_opacity", 42, 42)]
    public void Overlay_opacity_uses_Wpf_bounds(string verb, int value, int expected)
    {
        var old = CoreSettings.ServiceProvider;
        var oldRefresh = RemoteCommands.RefreshOverlays;
        RemoteCommands.RefreshOverlays = () => { };
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            Assert.Null(RemoteCommands.Execute(verb, JObject.FromObject(new { value })));
            Assert.Equal(expected, verb == "set_pink_opacity" ? settings.Current.PinkFilterOpacity : settings.Current.SpiralOpacity);
        }
        finally { RemoteCommands.StopOverlays(); RemoteCommands.ReleaseOverlayStop(); RemoteCommands.RefreshOverlays = oldRefresh; CoreSettings.ServiceProvider = old; }
    }

    [Fact]
    public void Remote_mind_wipe_uses_local_clips_and_settings_and_stops_on_disconnect()
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldStart = CoreMindWipe.StartProvider;
        var oldTrigger = CoreMindWipe.TriggerOnceProvider;
        var oldStop = CoreMindWipe.StopProvider;
        var oldCount = CoreMindWipe.ClipCountProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var started = new List<(double, double)>();
        var triggered = 0; var stopped = 0; var clips = 0;
        CoreMindWipe.StartProvider = (f, v) => started.Add((f, v));
        CoreMindWipe.TriggerOnceProvider = () => triggered++;
        CoreMindWipe.StopProvider = () => stopped++;
        CoreMindWipe.ClipCountProvider = () => clips;
        try
        {
            settings.Current.MindWipeFrequency = 9; settings.Current.MindWipeVolume = 37;
            Assert.Null(RemoteCommands.Execute("trigger_mind_wipe", null));
            Assert.Equal(0, triggered); // WPF: an empty pool is a no-op
            clips = 1;
            Assert.Null(RemoteCommands.Execute("trigger_mind_wipe", null));
            Assert.Equal(1, triggered);
            Assert.Null(RemoteCommands.Execute("start_mind_wipe", null));
            Assert.Equal(new[] { (9.0, 0.37) }, started);
            Assert.Null(RemoteCommands.Execute("stop_mind_wipe", null));
            Assert.Equal(1, stopped);
            RemoteCommands.StopEffects(force: false);
            Assert.Equal(2, stopped);
        }
        finally
        {
            CoreMindWipe.StartProvider = oldStart; CoreMindWipe.TriggerOnceProvider = oldTrigger;
            CoreMindWipe.StopProvider = oldStop; CoreMindWipe.ClipCountProvider = oldCount;
            CoreSettings.ServiceProvider = oldSettings; RemoteCommands.ReleaseOverlayStop();
        }
    }

    [Fact]
    public void Overlay_commands_refresh_without_starting_engine_and_remote_cleanup_restores_switches()
    {
        var old = CoreSettings.ServiceProvider;
        var oldRefresh = RemoteCommands.RefreshOverlays;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var states = new List<bool>();
        var renderedPink = false;
        RemoteCommands.RefreshOverlays = () => { states.Add(RemoteCommands.OverlayRunning); renderedPink = settings.Current.PinkFilterEnabled; };
        try
        {
            Assert.False(CoreEngine.IsRunning);
            settings.Current.PinkFilterEnabled = settings.Current.SpiralEnabled = false;
            ConditioningControlPanel.Services.Remote.RemoteWearer.Take();
            settings.Current.PinkFilterOpacity = 17;
            Assert.Null(RemoteCommands.Execute("set_pink_opacity", null));
            Assert.Empty(states);
            Assert.Equal(17, settings.Current.PinkFilterOpacity);
            Assert.Null(RemoteCommands.Execute("set_pink_opacity", new JObject()));
            Assert.Equal(25, settings.Current.PinkFilterOpacity);
            Assert.Null(RemoteCommands.Execute("show_pink_filter", null));
            Assert.False(CoreEngine.IsRunning);
            CoreEngine.Stop(); // panic/Stop reaches remote-only overlays too
            Assert.False(states[^1]);
            Assert.True(RemoteCommands.OverlaysStopped);
            Assert.True(settings.Current.PinkFilterEnabled);
            Assert.Null(RemoteCommands.Execute("show_spiral", null));
            Assert.True(states[^1]);
            Assert.False(RemoteCommands.OverlaysStopped);
            RemoteCommands.StopEffects(force: false); // opted-in disconnect, no local run
            Assert.False(states[^1]);
            Assert.False(settings.Current.SpiralEnabled || settings.Current.PinkFilterEnabled); // WPF wearer restoration
            Assert.False(renderedPink);
        }
        finally
        {
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            RemoteCommands.StopOverlays(); RemoteCommands.ReleaseOverlayStop();
            RemoteCommands.RefreshOverlays = oldRefresh; CoreSettings.ServiceProvider = old;
        }
    }

    [Fact]
    public void Remote_cleanup_over_a_local_run_and_panic_keep_wearer_values()
    {
        var old = CoreSettings.ServiceProvider;
        var oldRefresh = RemoteCommands.RefreshOverlays;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        RemoteCommands.RefreshOverlays = () => { };
        try
        {
            CoreEngine.Start();
            settings.Current.PinkFilterEnabled = true;
            ConditioningControlPanel.Services.Remote.RemoteWearer.Take();
            Assert.Null(RemoteCommands.Execute("set_pink_opacity", JObject.FromObject(new { value = 30 })));
            RemoteCommands.StopEffects(force: false); // user unticks Remote Control mid-run (WPF LeavesLocalRun, #1405)
            Assert.False(RemoteCommands.OverlaysStopped);
            Assert.True(settings.Current.PinkFilterEnabled);
            RemoteCommands.StopEffects(force: true); // trigger_panic takes the run and the remote switches down
            Assert.False(CoreEngine.IsRunning);
            Assert.True(settings.Current.PinkFilterEnabled); // base wearer restoration wins
        }
        finally
        {
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            CoreEngine.Stop(); RemoteCommands.ReleaseOverlayStop();
            RemoteCommands.RefreshOverlays = oldRefresh; CoreSettings.ServiceProvider = old;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Throwing_overlay_refresh_cannot_prevent_safety_handback(bool force, bool wearerStrict)
    {
        var oldSettings = CoreSettings.ServiceProvider;
        var oldRefresh = RemoteCommands.RefreshOverlays;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        RemoteCommands.RefreshOverlays = () => { };
        try
        {
            CoreEngine.Start();
            var s = settings.Current;
            s.StrictLockEnabled = wearerStrict;
            s.PanicKeyEnabled = true;
            s.PinkFilterEnabled = true;
            ConditioningControlPanel.Services.Remote.RemoteWearer.Take();
            Assert.Null(RemoteCommands.Execute("enable_strict_lock", null));
            Assert.Null(RemoteCommands.Execute("stop_pink_filter", null));
            s.PanicKeyEnabled = false; // a stale disabled panic key must also follow the release rule
            bool? strictAtRefresh = null, panicAtRefresh = null;
            RemoteCommands.RefreshOverlays = () =>
            {
                if (!s.PinkFilterEnabled) return; // the earlier Stop pass has not restored visuals yet
                strictAtRefresh = s.StrictLockEnabled;
                panicAtRefresh = s.PanicKeyEnabled;
                throw new InvalidOperationException("overlay window failed to show");
            };
            var error = Record.Exception(() => RemoteCommands.StopEffects(force));
            Assert.Equal((bool?)wearerStrict, strictAtRefresh); // WPF safety release precedes refresh
            Assert.Equal((bool?)true, panicAtRefresh);
            Assert.Null(error);
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop(); // session-end cleanup
            Assert.Equal(wearerStrict, s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);
        }
        finally
        {
            RemoteCommands.RefreshOverlays = () => { };
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            CoreEngine.Stop(); RemoteCommands.ReleaseOverlayStop();
            RemoteCommands.RefreshOverlays = oldRefresh;
            CoreSettings.ServiceProvider = oldSettings;
        }
    }

    [Fact]
    public async Task Status_publishes_available_sessions_and_live_progress()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        var custom = new Session { Id = "custom-test", Name = "Custom test", Icon = "C", DurationMinutes = 3, Difficulty = SessionDifficulty.Hard, IsAvailable = true, Source = SessionSource.Custom, Phases = { new SessionPhase { Name = "First" } } };
        var unavailable = new Session { Id = "unavailable-test", IsAvailable = false };
        var runner = new SessionRunner(new SessionLogService());
        r.SessionsProvider = () => Session.GetAllSessions().Concat(new[] { custom, unavailable });
        r.SessionRunnerProvider = () => runner;
        await r.StartAsync("light");
        try
        {
            await r.PushStatusNowAsync();
            var idle = f.Seen.Last(x => x.Path == "/v2/remote/status").Body;
            var list = (JArray)idle["available_sessions"]!;
            Assert.Contains(list, x => (string?)x["id"] == custom.Id && (string?)x["name"] == custom.GetModeAwareName() && (string?)x["icon"] == "C" && (int?)x["duration_minutes"] == 3 && (string?)x["difficulty"] == "Hard");
            Assert.Contains(list, x => Session.GetAllSessions().Any(s => s.IsAvailable && s.Id == (string?)x["id"]));
            Assert.DoesNotContain(list, x => (string?)x["id"] == unavailable.Id || Session.GetAllSessions().Any(s => !s.IsAvailable && s.Id == (string?)x["id"]));
            Assert.Equal(JTokenType.Null, idle["session_info"]!.Type);
            runner.Start(custom);
            await r.PushStatusNowAsync();
            var running = f.Seen.Last(x => x.Path == "/v2/remote/status").Body["session_info"]!;
            Assert.Equal(custom.GetModeAwareName(), (string?)running["name"]);
            Assert.Equal("C", (string?)running["icon"]);
            Assert.Equal(180, (int?)running["total_seconds"]);
            Assert.False((bool)running["is_paused"]!);
            Assert.Equal(custom.GetModeAwarePhaseName(custom.Phases[0]), (string?)running["current_phase"]);
            runner.Pause();
            runner._pausedElapsed = TimeSpan.FromSeconds(17);
            await r.PushStatusNowAsync();
            var paused = f.Seen.Last(x => x.Path == "/v2/remote/status").Body["session_info"]!;
            Assert.True((bool)paused["is_paused"]!);
            Assert.Equal(17, (int?)paused["elapsed_seconds"]);
        }
        finally { runner.Stop(); CoreEngine.Stop(); CoreSession.IsSessionRunningProvider = null; }
    }

    [Fact]
    public async Task Status_advertises_only_features_available_on_this_head()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        await r.StartAsync("full");
        Poll(f, "{\"controller_connected\":true}");
        await r.PollOnceAsync();
        var caps = f.Seen.Last(s => s.Path == "/v2/remote/status").Body["caps"];
        Assert.Equal(new[] { "haptic_pattern", "haptic_level" }, caps!.Values<string>().ToArray());
    }

    [Fact]
    public void A_sandbox_never_reaches_the_real_relay()
    {
        Assert.Null(RemoteRelay.ResolveBaseUrl(null, sandboxed: true));
        Assert.Null(RemoteRelay.ResolveBaseUrl("https://codebambi-proxy.vercel.app", sandboxed: true));
        Assert.Equal("http://127.0.0.1:9999", RemoteRelay.ResolveBaseUrl("http://127.0.0.1:9999/", sandboxed: true));
        Assert.Equal("https://codebambi-proxy.vercel.app", RemoteRelay.ResolveBaseUrl(null, sandboxed: false));
    }

    [Fact]
    public async Task Start_pairs_with_a_pin_and_the_url_carries_it_in_the_fragment()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        Assert.Equal("ABC123", await r.StartAsync("light"));
        var body = f.Seen.Single().Body;
        Assert.Equal("uid-1", (string?)body["unified_id"]);
        Assert.Equal("light", (string?)body["tier"]);
        Assert.Matches("^[0-9]{4}$", (string?)body["connect_pin"]);
        Assert.Equal(r.ConnectPin, (string?)body["connect_pin"]);
        Assert.Equal($"https://cclabs.app/remote/#code=ABC123&pin={r.ConnectPin}", RemoteRelay.PairingUrl("ABC123", r.ConnectPin));
    }

    [Fact]
    public async Task Commands_run_but_panic_can_never_be_switched_off_and_the_controller_is_told()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        await r.StartAsync("full");
        var received = new List<string>();
        r.CommandReceived += (_, a) => received.Add(a);
        Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"start_flash\"},{\"id\":\"2\",\"action\":\"disable_panic\"}]}");
        await r.PollOnceAsync();

        Assert.True(r.ControllerConnected);
        Assert.Equal(new[] { "start_flash" }, _ran);
        Assert.Equal(new[] { "start_flash" }, received);
        var status = f.Seen.Last(s => s.Path == "/v2/remote/status").Body["last_executed"]!;
        Assert.Equal("disable_panic", (string?)status["action"]);
        Assert.Equal("fail", (string?)status["status"]);
        Assert.Equal("the panic key stays on", (string?)status["reason"]);   // WPF 719ed9ca5
    }

    [Fact]
    public async Task Resume_routes_to_head_and_reports_panic()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        string? refusal = "panic";
        var calls = 0;
        r.ResumeSession = _ => { calls++; return refusal; };
        await r.StartAsync("full");
        Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"resume_session\"}]}");
        await r.PollOnceAsync();
        var status = f.Seen.Last(s => s.Path == "/v2/remote/status").Body["last_executed"]!;
        Assert.Equal("fail", (string?)status["status"]);
        Assert.Equal("panic", (string?)status["reason"]);
        Assert.Equal(1, calls);
        Assert.Empty(_ran);
        refusal = null;
        Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"2\",\"action\":\"resume_session\"}]}");
        await r.PollOnceAsync();
        status = f.Seen.Last(s => s.Path == "/v2/remote/status").Body["last_executed"]!;
        Assert.Equal("ok", (string?)status["status"]);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Lockdown_refuses_only_enable_strict_lock()
    {
        Assert.Null(RemoteCommandGate.Screen("enable_strict_lock", lockdownActive: false));
        Assert.Equal("not during Lockdown", RemoteCommandGate.Screen("enable_strict_lock", lockdownActive: true));
        foreach (var a in new[] { "disable_strict_lock", "enable_panic", "stop_session", "pause_session", "trigger_panic", "start_flash" })
            Assert.Null(RemoteCommandGate.Screen(a, lockdownActive: true));
        Assert.NotNull(RemoteCommandGate.Screen("disable_panic", lockdownActive: false));
        Assert.NotNull(RemoteCommandGate.Screen("disable_panic", lockdownActive: true));
    }

    // WPF ExecuteCommand's LeashRemoteRule door (owner 2026-09-26) and RemoteHoldings.RefusesStrictOff
    // (owner 2026-10-22), on the head that runs through RemoteCommandGate.
    [Fact]
    public void A_leash_refuses_strict_on_and_a_lockdowns_forced_lock_refuses_strict_off()
    {
        Assert.Equal("not while on a leash", RemoteCommandGate.Screen("enable_strict_lock", false, leashed: true));
        Assert.Equal("the panic key stays on", RemoteCommandGate.Screen("disable_panic", false, leashed: true));
        foreach (var a in new[] { "disable_strict_lock", "trigger_panic", "start_flash", "enable_panic" })
            Assert.Null(RemoteCommandGate.Screen(a, false, leashed: true));
        Assert.Equal("the Lockdown holds the strict lock",
            RemoteCommandGate.Screen("disable_strict_lock", lockdownActive: true, lockdownForcedStrict: true));
        Assert.Null(RemoteCommandGate.Screen("disable_strict_lock", lockdownActive: true, lockdownForcedStrict: false));
        Assert.Null(RemoteCommandGate.Screen("disable_strict_lock", lockdownActive: false, lockdownForcedStrict: true));
    }

    [Fact]
    public async Task A_leashed_account_never_runs_enable_strict_lock_from_any_session()
    {
        var leashed = ConditioningControlPanel.Services.Leash.LeashGuard.IsLeashed;
        try
        {
            ConditioningControlPanel.Services.Leash.LeashGuard.IsLeashed = () => true;
            var f = new FakeRelay();
            using var r = Relay(f);
            await r.StartAsync("full");
            Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"enable_strict_lock\"},{\"id\":\"2\",\"action\":\"start_flash\"}]}");
            await r.PollOnceAsync();
            Assert.Equal(new[] { "start_flash" }, _ran);
            var status = f.Seen.Last(s => s.Path == "/v2/remote/status").Body["last_executed"]!;
            Assert.Equal("start_flash", (string?)status["action"]);
        }
        finally { ConditioningControlPanel.Services.Leash.LeashGuard.IsLeashed = leashed; }
    }

    // Owner, 2026-10-22 (main 4adc4c476, 2a712d9da): a controller's panic during a Lockdown that forced
    // strict lock and switched the panic key off leaves both as Lockdown holds them; its own end restores.
    // Restraint-reducing verbs still run and never touch the timer.
    [Fact]
    public void Restraint_reducing_verbs_run_under_Lockdown_keep_its_timer_and_it_still_restores_on_end()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey);
        var prev = LockdownService.Current;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = (false, true, true, true);
            ld.Activate(TimeSpan.FromMinutes(30));
            Assert.True(s.StrictLockEnabled);
            Assert.False(s.PanicKeyEnabled);
            Assert.True(ld.ForcedStrictLock && ld.DisabledPanicKey);
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.False(s.PanicKeyEnabled);      // the Lockdown holds it off
            Assert.True(s.StrictLockEnabled);     // the Lockdown forced it
            Assert.Null(RemoteCommands.Execute("enable_panic", null));
            Assert.True(s.PanicKeyEnabled);
            Assert.True(ld.IsActive);             // the timer was not ended
            Assert.Equal(0, ld.RestartCount);     // nor restarted
            s.PanicKeyEnabled = false;            // whatever the controller left behind...
            ld.Deactivate();
            Assert.True(s.PanicKeyEnabled);       // ...Lockdown restores the pre-lockdown values
            Assert.False(s.StrictLockEnabled);
        }
        finally
        {
            ld.Deactivate();
            ld.Dispose();
            LockdownService.Current = prev;
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = saved;
        }
    }

    [Fact]
    public async Task Rate_limit_backs_off_and_an_expired_session_ends_and_stops_remote_effects()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        var now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        r.Now = () => now;
        await r.StartAsync("light");
        f.Answer = _ => Json((HttpStatusCode)429, "{}");
        await r.PollOnceAsync();
        await r.PollOnceAsync();
        Assert.Equal(20, r.PollInterval);
        Assert.Null(r.LastEndedUtc);

        var ended = 0;
        r.SessionEnded += (_, _) => ended++;
        f.Answer = _ => Json(HttpStatusCode.NotFound, "{}");
        await r.PollOnceAsync();
        Assert.False(r.IsActive);
        Assert.Null(r.SessionCode);
        Assert.Equal(1, ended);
        Assert.Equal(now, r.LastEndedUtc);   // WPF LastEndedUtc: Circe's remote grace starts here
        now = now.AddMinutes(1);
        await r.StopAsync();
        Assert.Equal(now.AddMinutes(-1), r.LastEndedUtc); // idle cleanup does not restart grace
        Assert.Equal(new[] { false }, _stops);   // the subject's own run survives (#878)
    }

    [Fact]
    public async Task An_idle_controller_is_dropped_after_two_minutes_and_does_not_bounce_back_while_idle()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        r.Now = () => now;
        await r.StartAsync("light");
        Poll(f, "{\"controller_connected\":true,\"controller_idle\":true}");
        await r.PollOnceAsync();
        Assert.True(r.ControllerConnected);
        now = now.AddSeconds(119);
        await r.PollOnceAsync();
        Assert.True(r.ControllerConnected);
        now = now.AddSeconds(2);
        await r.PollOnceAsync();
        Assert.False(r.ControllerConnected);
        await r.PollOnceAsync();
        Assert.False(r.ControllerConnected);
        Poll(f, "{\"controller_connected\":true,\"controller_idle\":false}");
        await r.PollOnceAsync();
        Assert.True(r.ControllerConnected);
    }

    // Owner, 2026-10-22: a controller's panic restores the panic key but never switches off a strict lock
    // the wearer set themselves (main used to write it off); one the controller set goes.
    [Fact]
    public void Panic_stop_restores_the_panic_key_and_only_the_controllers_strict_lock_goes()
    {
        var s = CoreSettings.Current;
        var (strict, panic) = (s.StrictLockEnabled, s.PanicKeyEnabled);
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (true, false);
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.True(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);
            Assert.Equal(RemoteCommands.NotOnThisBuild, RemoteCommands.Execute("play_hypnotube", null));

            s.StrictLockEnabled = false;
            ConditioningControlPanel.Services.Remote.RemoteWearer.Take();
            Assert.Null(RemoteCommands.Execute("enable_strict_lock", null));
            Assert.True(s.StrictLockEnabled);
            Assert.Null(RemoteCommands.Execute("trigger_panic", null));
            Assert.False(s.StrictLockEnabled);
        }
        finally
        {
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (strict, panic);
        }
    }

    // Owner, 2026-10-22: the controller leaving during a Lockdown that switched the panic key off leaves it
    // off; a strict lock the controller set before the Lockdown is corrected in Lockdown's record.
    [Fact]
    public async Task Controller_leave_mid_lockdown_never_rearms_panic_and_fixes_lockdowns_strict_record()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey, s.StopEffectsOnRemoteDisconnect);
        var prev = LockdownService.Current;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey, s.StopEffectsOnRemoteDisconnect) =
                (false, true, true, true, false);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, f) { AutoPoll = false };
            await r.StartAsync("full");
            Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"enable_strict_lock\"}]}");
            await r.PollOnceAsync();
            Assert.True(s.StrictLockEnabled);
            ld.Activate(TimeSpan.FromMinutes(30));
            Assert.True(ld.RecordedStrictLock);     // the controller's, recorded as the wearer's own
            Assert.False(s.PanicKeyEnabled);

            Poll(f, "{\"controller_connected\":false}");
            await r.PollOnceAsync();
            Assert.False(s.PanicKeyEnabled);        // the Lockdown holds it off
            Assert.True(s.StrictLockEnabled);       // the Lockdown forced it
            Assert.False(ld.RecordedStrictLock);    // ...and will put the wearer's own back

            await r.StopAsync();                    // the session end asks the same rule
            Assert.False(s.PanicKeyEnabled);
            ld.Deactivate();
            Assert.True(s.PanicKeyEnabled);
            Assert.False(s.StrictLockEnabled);
        }
        finally
        {
            ld.Deactivate();
            ld.Dispose();
            LockdownService.Current = prev;
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey, s.StopEffectsOnRemoteDisconnect) = saved;
        }
    }

    // WPF ReleaseHoldingsForExit (RemoteControlService.V2.cs:261, main bafe52cfc): exit hands back the controller's
    // strict lock before the final save, keeps the wearer's own, and refuses a command fetched before exit.
    [Fact]
    public async Task Exit_releases_the_controllers_strict_lock_keeps_the_wearers_and_refuses_late_commands()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled);
        var savedInvoke = CoreDispatch.InvokeProvider;
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (false, false);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
            await r.StartAsync("full");
            Assert.Null(RemoteCommands.Execute("enable_strict_lock", null));
            r.ReleaseHoldingsForExit();
            r.ReleaseHoldingsForExit();   // idempotent
            Assert.False(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);

            s.StrictLockEnabled = true;   // the wearer's own
            ConditioningControlPanel.Services.Remote.RemoteWearer.Take();
            r.ReleaseHoldingsForExit();
            Assert.True(s.StrictLockEnabled);

            // A command fetched before exit whose UI dispatch lands after it is refused.
            s.StrictLockEnabled = false;
            Func<object?>? parked = null;
            CoreDispatch.InvokeProvider = (fn, _) => { parked ??= fn; return (false, null); };
            Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"3\",\"action\":\"enable_strict_lock\"}]}");
            await r.PollOnceAsync();
            r.ReleaseHoldingsForExit();
            Assert.Equal("the controller left", parked!());
            Assert.False(s.StrictLockEnabled);
        }
        finally
        {
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.StrictLockEnabled, s.PanicKeyEnabled) = saved;
            CoreDispatch.InvokeProvider = savedInvoke;
        }
    }

    // The exit release keeps the Lockdown rule: its forced strict lock stays on, its disabled panic key stays off.
    [Fact]
    public async Task Exit_mid_lockdown_keeps_the_forced_strict_lock_and_the_panic_key_off()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey);
        var prev = LockdownService.Current;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = (false, true, true, true);
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, new FakeRelay()) { AutoPoll = false };
            await r.StartAsync("full");
            Assert.Null(RemoteCommands.Execute("enable_strict_lock", null));
            ld.Activate(TimeSpan.FromMinutes(30));
            r.ReleaseHoldingsForExit();
            Assert.True(s.StrictLockEnabled);       // the Lockdown forced it
            Assert.False(s.PanicKeyEnabled);       // the Lockdown holds it off
            Assert.False(ld.RecordedStrictLock);   // ...and will put the wearer's own back
        }
        finally
        {
            ld.Deactivate();
            ld.Dispose();
            LockdownService.Current = prev;
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = saved;
        }
    }

    // A crash mid-session: the recovery file hands the controller's strict lock back at the next launch.
    [Fact]
    public void A_crash_mid_session_heals_the_controllers_strict_lock_at_launch()
    {
        var s = CoreSettings.Current;
        var strict = s.StrictLockEnabled;
        var dir = ConditioningControlPanel.Services.Remote.RemoteWearer.Dir;
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-remote-wearer-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);
        try
        {
            ConditioningControlPanel.Services.Remote.RemoteWearer.Dir = () => tmp;
            s.StrictLockEnabled = false;
            ConditioningControlPanel.Services.Remote.RemoteWearer.Take();
            Assert.Null(RemoteCommands.Execute("enable_strict_lock", null));
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(tmp, ConditioningControlPanel.Services.Remote.RemoteHoldings.FileName)));
            // the app dies here; the next launch:
            Assert.Equal(1, ConditioningControlPanel.Services.Remote.RemoteWearer.HealOnLaunch());
            Assert.False(s.StrictLockEnabled);
            Assert.False(System.IO.File.Exists(System.IO.Path.Combine(tmp, ConditioningControlPanel.Services.Remote.RemoteHoldings.FileName)));
        }
        finally
        {
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            ConditioningControlPanel.Services.Remote.RemoteWearer.Dir = dir;
            s.StrictLockEnabled = strict;
            System.IO.Directory.Delete(tmp, true);
        }
    }
    // main 71cfc4185 (ccp-bugs #1340): the controller leaving hands back the panic key and the strict
    // lock IT switched on; a strict lock the subject set stays.
    [Fact]
    public async Task Controller_leave_releases_the_controllers_strict_lock_and_the_panic_key_only()
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect);
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect) = (false, false, false);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
            await r.StartAsync("full");
            Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"enable_strict_lock\"}]}");
            await r.PollOnceAsync();
            Assert.True(s.StrictLockEnabled);
            Poll(f, "{\"controller_connected\":false}");
            await r.PollOnceAsync();
            Assert.False(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);

            s.StrictLockEnabled = true;   // the subject's own
            Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"2\",\"action\":\"enable_strict_lock\"}]}");
            await r.PollOnceAsync();
            Poll(f, "{\"controller_connected\":false}");
            await r.PollOnceAsync();
            Assert.True(s.StrictLockEnabled);
        }
        finally { (s.StrictLockEnabled, s.PanicKeyEnabled, s.StopEffectsOnRemoteDisconnect) = saved; }
    }

    // main 7b22ece8c (ccp-bugs #1065): no toy -> refused, never a silent "ok".
    [Fact]
    public void Trigger_haptic_with_no_toy_is_refused()
    {
        var prev = CoreHaptics.Service;
        try
        {
            CoreHaptics.Service = null;
            Assert.Equal("no_device", RemoteCommands.Execute("trigger_haptic", null));
        }
        finally { CoreHaptics.Service = prev; }
    }

    private const string Loop = "{\"levels\":[50,80],\"step_ms\":100,\"loop\":true}";

    /// <summary>A stepped-clock driver in place of the static one (P08), restored after (P02).</summary>
    private static void WithSteppedHaptics(Action<PopQuizSchedulerTests.FakeClock, List<int>> body)
    {
        var prev = RemoteCommands.RemoteHaptics;
        var clock = new PopQuizSchedulerTests.FakeClock();
        var sent = new List<int>();
        RemoteCommands.RemoteHaptics = new ConditioningControlPanel.Services.Remote.CoreRemoteHapticDriver(() => 1.0, clock)
        { Sink = steps => { sent.Add(steps.Count); return null; } };
        try { body(clock, sent); }
        finally { RemoteCommands.RemoteHaptics.Stop(); RemoteCommands.RemoteHaptics = prev; }
    }

    private static string Cmd(string id, string action, string p = "{}") =>
        "{\"controller_connected\":true,\"commands\":[{\"id\":\"" + id + "\",\"action\":\"" + action + "\",\"params\":" + p + "}]}";

    // main 719ed9ca5: a looping pattern keeps playing until haptic_stop, a leaving controller, or panic
    // ends it; after panic the loop never comes back (HapticMixer.PanicStop only mutes 400 ms).
    [Fact]
    public void Remote_haptics_play_and_every_stop_path_ends_them() => WithSteppedHaptics((clock, sent) =>
    {
        var f = new FakeRelay();
        using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
        r.StartAsync("full").Wait();
        var d = RemoteCommands.RemoteHaptics;
        void Playing(string id) { Poll(f, Cmd(id, "haptic_pattern", Loop)); r.PollOnceAsync().Wait(); Assert.True(d.IsPlaying); clock.Advance(TimeSpan.FromSeconds(2)); Assert.True(sent.Count > 1); }
        void Silent() { Assert.False(d.IsPlaying); var n = sent.Count; clock.Advance(TimeSpan.FromSeconds(5)); Assert.Equal(n, sent.Count); }

        Playing("1");
        Poll(f, Cmd("2", "haptic_stop")); r.PollOnceAsync().Wait();
        Silent();

        Playing("3");
        Poll(f, "{\"controller_connected\":false}"); r.PollOnceAsync().Wait();
        Silent();

        Playing("4");
        RemoteCommands.StopHaptics();   // every panic path on both heads
        Silent();

        Assert.Equal("no pattern", RemoteCommands.Execute("haptic_pattern", new JObject()));
    });

    // Safety review P1/P2: a command fetched before a panic or a leave never runs after it, however
    // late its UI dispatch lands; a late enable_strict_lock that did land before the leave is released.
    [Fact]
    public void A_command_in_flight_never_outlives_a_panic_or_a_leave() => WithSteppedHaptics((clock, sent) =>
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, CoreDispatch.InvokeProvider);
        Func<object?>? parked = null;
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (false, true);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
            r.StartAsync("full").Wait();
            Poll(f, "{\"controller_connected\":true}"); r.PollOnceAsync().Wait();
            // The UI thread is busy: the first dispatch times out. Avalonia cancels it then; here it is parked and
            // run by hand, to prove the generation check refuses it even if it did land late. The rest run at once.
            CoreDispatch.InvokeProvider = (fn, _) => { if (parked == null) { parked = fn; return (false, null); } return (true, fn()); };

            Poll(f, Cmd("1", "haptic_level", "{\"level\":60,\"ms\":3000}")); r.PollOnceAsync().Wait();
            RemoteCommands.StopHaptics();
            Assert.Equal("stopped by panic", parked!());
            Assert.False(RemoteCommands.RemoteHaptics.IsPlaying);

            parked = null;
            Poll(f, Cmd("2", "haptic_pattern", Loop)); r.PollOnceAsync().Wait();
            Poll(f, "{\"controller_connected\":false}"); r.PollOnceAsync().Wait();
            Assert.Equal("the controller left", parked!());
            Assert.False(RemoteCommands.RemoteHaptics.IsPlaying);

            Poll(f, "{\"controller_connected\":true}"); r.PollOnceAsync().Wait();
            parked = null;
            Poll(f, Cmd("3", "enable_strict_lock")); r.PollOnceAsync().Wait();
            Assert.Null(parked!());          // lands late, still before the leave
            Assert.True(s.StrictLockEnabled);
            Poll(f, "{\"controller_connected\":false}"); r.PollOnceAsync().Wait();
            Assert.False(s.StrictLockEnabled);
        }
        finally { (s.StrictLockEnabled, s.PanicKeyEnabled, CoreDispatch.InvokeProvider) = saved; }
    });

    // Safety review P2: Avalonia cancels an Invoke that times out, so after a UI stall the leave cleanup is
    // posted instead: the loop stops, the controller's strict lock goes, the panic key comes back.
    [Fact]
    public void A_leave_during_a_ui_stall_still_releases_everything() => WithSteppedHaptics((clock, sent) =>
    {
        var s = CoreSettings.Current;
        var saved = (s.StrictLockEnabled, s.PanicKeyEnabled, CoreDispatch.InvokeProvider, CoreDispatch.PostProvider);
        try
        {
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (false, true);
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, f) { AutoPoll = false };
            r.StartAsync("full").Wait();
            Poll(f, "{\"controller_connected\":true}"); r.PollOnceAsync().Wait();
            Poll(f, Cmd("1", "enable_strict_lock")); r.PollOnceAsync().Wait();
            Poll(f, Cmd("2", "haptic_pattern", Loop)); r.PollOnceAsync().Wait();
            s.PanicKeyEnabled = false;
            Assert.True(s.StrictLockEnabled && RemoteCommands.RemoteHaptics.IsPlaying);

            var posted = new List<Action>();
            CoreDispatch.InvokeProvider = (_, _) => (false, null);   // the UI is stalled: Avalonia cancels the call
            CoreDispatch.PostProvider = posted.Add;
            Poll(f, "{\"controller_connected\":false}"); r.PollOnceAsync().Wait();
            Assert.True(RemoteCommands.RemoteHaptics.IsPlaying);       // nothing ran on the stalled UI yet
            foreach (var a in posted.ToArray()) a();                  // the UI drains
            Assert.False(RemoteCommands.RemoteHaptics.IsPlaying);
            Assert.False(s.StrictLockEnabled);
            Assert.True(s.PanicKeyEnabled);
        }
        finally { (s.StrictLockEnabled, s.PanicKeyEnabled, CoreDispatch.InvokeProvider, CoreDispatch.PostProvider) = saved; }
    });

    // Audit #1937: ending the session stops the controller's effects on the UI thread (they close
    // windows), and still stops them, posted, when a stalled UI cancels the Invoke.
    [Fact]
    public async Task Ending_the_session_stops_effects_on_the_ui_thread()
    {
        var saved = (CoreDispatch.InvokeProvider, CoreDispatch.PostProvider);
        try
        {
            var onUi = false;
            var stoppedOnUi = new List<bool>();
            CoreDispatch.InvokeProvider = (fn, _) => { onUi = true; try { return (true, fn()); } finally { onUi = false; } };
            var f = new FakeRelay();
            using (var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => stoppedOnUi.Add(onUi), f) { AutoPoll = false })
            {
                await r.StartAsync("full");
                await r.StopAsync();
            }
            Assert.Equal(new[] { true }, stoppedOnUi);

            var posted = new List<Action>();
            CoreDispatch.InvokeProvider = (_, _) => (false, null);
            CoreDispatch.PostProvider = posted.Add;
            stoppedOnUi.Clear();
            using (var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => stoppedOnUi.Add(onUi), f) { AutoPoll = false })
            {
                await r.StartAsync("full");
                await r.StopAsync();
                Assert.Empty(stoppedOnUi);
                foreach (var a in posted.ToArray()) a();
                Assert.Single(stoppedOnUi);
            }
        }
        finally { (CoreDispatch.InvokeProvider, CoreDispatch.PostProvider) = saved; }
    }

    // WPF RemoteControlService.EndSessionNow (leash cut): the session ends HERE at once, the relay is told after.
    [Fact]
    public async Task End_session_now_ends_locally_before_the_relay_is_told()
    {
        var f = new FakeRelay();
        RemoteRelay? relay = null;
        var stopSent = new TaskCompletionSource<(bool ActiveThen, int StopsThen, JObject Body)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = f.Answer;
        f.Answer = path =>
        {
            if (path == "/v2/remote/stop") stopSent.TrySetResult((relay!.IsActive, _stops.Count, f.Seen[^1].Body));
            return start(path);
        };
        using var r = relay = Relay(f);
        await r.StartAsync("full");

        r.EndSessionNow();

        Assert.False(r.IsActive);
        var sent = await stopSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(sent.ActiveThen);           // ended here before the request went out
        Assert.Equal(1, sent.StopsThen);
        Assert.Equal("uid-1", (string?)sent.Body["unified_id"]);
        r.EndSessionNow();                       // safe with no session
        Assert.Single(_stops);
    }

    // main d39969827: 1 s polls while a connected controller is busy; a 429 still backs off from 5 s.
    [Fact]
    public async Task Hot_cadence_while_the_controller_is_busy()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        r.Now = () => now;
        await r.StartAsync("light");
        Poll(f, "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"start_flash\"}]}");
        await r.PollOnceAsync();
        Assert.Equal(RemoteRelay.HotPollSeconds, r.PollInterval);
        f.Answer = _ => Json((HttpStatusCode)429, "{}");
        await r.PollOnceAsync();
        Assert.Equal(10, r.PollInterval);
        Poll(f, "{\"controller_connected\":true}");
        now = now.AddSeconds(61);
        await r.PollOnceAsync();
        Assert.Equal(RemoteRelay.PollIntervalSeconds, r.PollInterval);
    }

    [Fact]
    public async Task More_signal_bypasses_emote_debounce_and_reports_failure()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        Assert.False(await r.SendMoreSignalAsync());
        await r.StartAsync("light");
        Assert.True((await r.SendEmoteAsync("hello", "", "custom")).ok);
        Assert.True(await r.SendMoreSignalAsync());
        var body = f.Seen.Last(s => s.Path == "/v2/remote/emote").Body;
        Assert.Equal("uid-1", (string?)body["unified_id"]);
        Assert.Equal("more", (string?)body["text"]);
        Assert.Equal("", (string?)body["icon"]);
        Assert.Equal("signal", (string?)body["kind"]);
        f.Answer = _ => Json(HttpStatusCode.ServiceUnavailable, "{}");
        Assert.False(await r.SendMoreSignalAsync());
        await r.StopAsync();
    }

    [Fact]
    public async Task Emotes_post_trimmed_debounce_double_clicks_and_report_the_rate_limit()
    {
        var f = new FakeRelay();
        using var r = Relay(f);
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        r.Now = () => now;
        Assert.Equal((false, "session not active", (int?)null), await r.SendEmoteAsync("hi", "", "custom"));
        await r.StartAsync("light");

        Assert.True((await r.SendEmoteAsync("  " + new string('x', 70) + " ", "123456789", "preset")).ok);
        var body = f.Seen.Last(s => s.Path == "/v2/remote/emote").Body;
        Assert.Equal(60, body["text"]!.ToString().Length);
        Assert.Equal("12345678", body["icon"]!.ToString());
        Assert.Equal("uid-1", body["unified_id"]!.ToString());

        now = now.AddMilliseconds(100);   // a double click: swallowed, nothing posted
        var posts = f.Seen.Count;
        Assert.Equal("debounced", (await r.SendEmoteAsync("again", "", "custom")).error);
        Assert.Equal(posts, f.Seen.Count);

        now = now.AddMilliseconds(RemoteRelay.EmoteDebounceMs);
        f.Answer = _ => Json((HttpStatusCode)429, "{\"retry_after_seconds\":7}");
        Assert.Equal((false, "rate_limited", (int?)7), await r.SendEmoteAsync("again", "", "custom"));
    }
    [Fact]
    public void Custom_subliminal_uses_the_silent_custom_surface_and_blank_input_is_a_noop()
    {
        var old = (CoreSubliminal.ShowProvider, CoreSubliminal.CustomShowProvider);
        var spoken = new List<string>();
        var custom = new List<string>();
        CoreSubliminal.ShowProvider = spoken.Add;
        CoreSubliminal.CustomShowProvider = custom.Add;
        try
        {
            Assert.Null(RemoteCommands.Execute("trigger_custom_subliminal", new JObject { ["text"] = "<b>Relax</b>" }));
            Assert.Equal(new[] { "<b>Relax</b>" }, custom);   // the head sanitizes before haptic/card
            Assert.Empty(spoken);                          // never a linked whisper or 20 XP
            Assert.Null(RemoteCommands.Execute("trigger_custom_subliminal", new JObject { ["text"] = "  " }));
            Assert.Single(custom);
            CoreSubliminal.CustomShowProvider = null;
            Assert.Equal(RemoteCommands.NotOnThisBuild,
                RemoteCommands.Execute("trigger_custom_subliminal", new JObject { ["text"] = "Relax" }));
            Assert.Empty(spoken);                          // never fall back to an audible phrase
        }
        finally { (CoreSubliminal.ShowProvider, CoreSubliminal.CustomShowProvider) = old; }
    }

    [Theory]
    [InlineData("end", false)]
    [InlineData("panic", false)]
    [InlineData("disconnect", false)]
    [InlineData("end", true)]
    public async Task Melt_verbs_restore_the_wearers_setting_and_player(string end, bool own)
    {
        var s = CoreSettings.Current;
        var saved = (s.BrainDrainEnabled, s.StopEffectsOnRemoteDisconnect, CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain);
        var starts = 0; var stops = 0;
        try
        {
            s.BrainDrainEnabled = own; s.StopEffectsOnRemoteDisconnect = true;
            CoreEngine.StartBrainDrain = () => starts++;
            CoreEngine.StopBrainDrain = () => stops++;
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, f) { AutoPoll = false };
            await r.StartAsync("full");
            Poll(f, Cmd("1", "start_brain_drain")); await r.PollOnceAsync();
            Assert.True(s.BrainDrainEnabled); Assert.Equal(1, starts);
            if (end == "end") await r.StopAsync();
            else if (end == "panic") RemoteCommands.StopEffects(true);
            else { Poll(f, "{\"controller_connected\":false}"); await r.PollOnceAsync(); }
            Assert.Equal(own, s.BrainDrainEnabled);
            if (!own) Assert.True(stops > 0);
        }
        finally
        {
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.BrainDrainEnabled, s.StopEffectsOnRemoteDisconnect, CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain) = saved;
        }
    }

    [Fact]
    public void Melt_stop_verb_turns_the_setting_and_player_off()
    {
        var s = CoreSettings.Current;
        var saved = (s.BrainDrainEnabled, CoreEngine.StopBrainDrain);
        var stops = 0;
        try
        {
            s.BrainDrainEnabled = true; CoreEngine.StopBrainDrain = () => stops++;
            Assert.Null(RemoteCommands.Execute("stop_brain_drain", null));
            Assert.False(s.BrainDrainEnabled); Assert.Equal(1, stops);
        }
        finally { (s.BrainDrainEnabled, CoreEngine.StopBrainDrain) = saved; }
    }

    [Fact]
    public async Task Melt_stop_hands_the_wearers_on_value_and_player_back_at_end()
    {
        var s = CoreSettings.Current;
        var saved = (s.BrainDrainEnabled, s.AudioOnlySession, CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain);
        var starts = 0; var stops = 0;
        try
        {
            s.BrainDrainEnabled = true; s.AudioOnlySession = false;
            CoreEngine.StartBrainDrain = () => starts++; CoreEngine.StopBrainDrain = () => stops++;
            CoreEngine.Start(); starts = 0;
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, f) { AutoPoll = false };
            await r.StartAsync("full");
            Poll(f, Cmd("1", "stop_brain_drain")); await r.PollOnceAsync();
            Assert.False(s.BrainDrainEnabled); Assert.Equal(1, stops);
            await r.StopAsync();
            Assert.True(s.BrainDrainEnabled); Assert.Equal(1, starts);
        }
        finally
        {
            CoreEngine.Stop();
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.BrainDrainEnabled, s.AudioOnlySession, CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain) = saved;
        }
    }

    [Fact]
    public async Task Melt_panic_restores_enabled_setting_without_restarting_player()
    {
        var s = CoreSettings.Current;
        var saved = (s.BrainDrainEnabled, CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain);
        var playing = false; var starts = 0;
        try
        {
            s.BrainDrainEnabled = true;
            CoreEngine.StartBrainDrain = () => { starts++; playing = true; };
            CoreEngine.StopBrainDrain = () => playing = false;
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, f) { AutoPoll = false };
            await r.StartAsync("full");
            Poll(f, Cmd("1", "stop_brain_drain")); await r.PollOnceAsync();
            RemoteCommands.StopEffects(true);
            Assert.True(s.BrainDrainEnabled);
            Assert.False(playing); Assert.Equal(0, starts);
        }
        finally
        {
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.BrainDrainEnabled, CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain) = saved;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Melt_session_end_stops_idle_player_but_preserves_wearers_running_engine(bool running)
    {
        var s = CoreSettings.Current;
        var saved = (s.BrainDrainEnabled, s.AudioOnlySession, CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain);
        var playing = false;
        try
        {
            s.BrainDrainEnabled = true; s.AudioOnlySession = false;
            CoreEngine.StartBrainDrain = () => playing = true;
            CoreEngine.StopBrainDrain = () => playing = false;
            if (running) CoreEngine.Start();
            var f = new FakeRelay();
            using var r = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, f) { AutoPoll = false };
            await r.StartAsync("full");
            Poll(f, Cmd("1", "start_brain_drain")); await r.PollOnceAsync();
            Assert.True(playing);
            await r.StopAsync();
            Assert.True(s.BrainDrainEnabled);
            Assert.Equal(running, playing);
            Assert.Equal(running, CoreEngine.IsRunning);
        }
        finally
        {
            CoreEngine.Stop();
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (s.BrainDrainEnabled, s.AudioOnlySession, CoreEngine.StartBrainDrain, CoreEngine.StopBrainDrain) = saved;
        }
    }

}
