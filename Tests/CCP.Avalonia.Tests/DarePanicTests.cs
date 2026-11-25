using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using System.Threading;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Dares;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class DarePanicTests
{
    private sealed class Host : IDareHost
    {
        internal bool Active;
        public string? Account() => "u_me";
        public int TzMinutes() => 0;
        public bool ChasterReady() => false;
        public int BookLockSeconds(int seconds) => 0;
        public void AdoptSp(string account, int sp) { }
        public bool LockdownActive => Active;
        public bool StartLockdown(TimeSpan duration) { Active = true; return true; }
        public event Action<TimeSpan, TimeSpan>? LockdownEnded { add { } remove { } }
    }
    private sealed class Api : IDaresApi
    {
        internal readonly TaskCompletionSource<JObject?> Start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JObject?> CallAsync(string op, JObject body, CancellationToken ct = default)
        {
            if (op == "start") return Start.Task;
            return Task.FromResult<JObject?>(JObject.Parse("""
                {"ok":true,"me":{},"bets":[{"id":"b1","role":"to","type":"cant","state":"settled","deadline":"3h",
                "friend":{"id":"f","name":"Fen"},"what":{"kind":"pics","target":100},
                "ends_at":"2026-11-18T13:00:00Z","at":"2026-11-18T09:00:00Z","accepted_at":"2026-11-18T10:00:00Z",
                "camera":false,"stakes":{"from":{"kind":"sp","amount":50},"to":{"kind":"lockdown","amount":10}},
                "dares":[],"winner":"from","counts":{"from":0,"to":0},"debt":{"state":"owed"}}]}
                """));
        }
    }

    [Fact]
    public void Host_uses_the_real_lockdown_and_restores_the_player_settings() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.Null(LockdownService.Current);
        Assert.False(File.Exists(Path.Combine(CorePaths.UserData, "lockdown_recovery.json")));
        var provider = CoreSettings.ServiceProvider;
        var sync = LockdownService.PanicKeyUiSync;
        CoreSettings.ServiceProvider = null;
        LockdownService.PanicKeyUiSync = null;
        var settings = CoreSettings.Current;
        var saved = (LockdownService.Current, settings.StrictLockEnabled, settings.PanicKeyEnabled,
            settings.LockdownForceStrictLock, settings.LockdownDisablePanicKey);
        using var lockdown = new LockdownService();
        LockdownService.Current = lockdown;
        using var host = new DareHead.Host();
        TimeSpan? configured = null;
        TimeSpan? elapsed = null;
        host.LockdownEnded += (set, ran) => { configured = set; elapsed = ran; };
        try
        {
            settings.StrictLockEnabled = false;
            settings.PanicKeyEnabled = true;
            settings.LockdownForceStrictLock = true;
            settings.LockdownDisablePanicKey = true;
            Assert.True(host.StartLockdown(TimeSpan.FromMinutes(10)));
            Assert.True(host.LockdownActive);
            Assert.True(settings.StrictLockEnabled);
            Assert.False(settings.PanicKeyEnabled);
            Assert.False(host.StartLockdown(TimeSpan.FromMinutes(5)));
            lockdown.Deactivate();
            Assert.Equal(TimeSpan.FromMinutes(10), configured);
            Assert.True(elapsed < TimeSpan.FromMinutes(10));
            Assert.False(settings.StrictLockEnabled);
            Assert.True(settings.PanicKeyEnabled);
        }
        finally
        {
            lockdown.Deactivate();
            (LockdownService.Current, settings.StrictLockEnabled, settings.PanicKeyEnabled,
                settings.LockdownForceStrictLock, settings.LockdownDisablePanicKey) = saved;
            LockdownService.PanicKeyUiSync = sync;
            CoreSettings.ServiceProvider = provider;
        }
    });

    [Fact]
    public void Wallet_receipt_requires_same_account_and_is_cancelled_by_stop() => AvaloniaTestDispatcher.Run(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = null;
        var settings = CoreSettings.Current;
        var saved = settings.SkillPoints;
        string? account = "test_account";
        using var host = new DareHead.Host(() => account);
        try
        {
            settings.SkillPoints = 100;
            host.AdoptSp("test_account", 60);
            host.Invalidate();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(100, settings.SkillPoints);
            host.AdoptSp("test_account", 60);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(60, settings.SkillPoints);
            host.AdoptSp("test_account", 30);
            account = "other_account";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(60, settings.SkillPoints);
        }
        finally
        {
            settings.SkillPoints = saved;
            CoreSettings.ServiceProvider = provider;
        }
    });

    [Fact]
    public void Browser_load_is_not_counted_as_video_playback()
    {
        var saved = (SettingsTabView.RemixBrowserMediaBusy, SettingsTabView.DareBrowserMediaPlaying, SettingsTabView.RemixMediaTick);
        try
        {
            SettingsTabView.RemixBrowserMediaBusy = true;
            SettingsTabView.DareBrowserMediaPlaying = false;
            SettingsTabView.RemixMediaTick = Stopwatch.GetTimestamp();
            Assert.False(SettingsTabView.DareBrowserMediaActive);
            SettingsTabView.DareBrowserMediaPlaying = true;
            Assert.True(SettingsTabView.DareBrowserMediaActive);
            SettingsTabView.RemixMediaTick = Stopwatch.GetTimestamp() - Stopwatch.Frequency * 21;
            Assert.False(SettingsTabView.DareBrowserMediaActive);
        }
        finally
        {
            (SettingsTabView.RemixBrowserMediaBusy, SettingsTabView.DareBrowserMediaPlaying, SettingsTabView.RemixMediaTick) = saved;
        }
    }

    [Fact]
    public async Task Panic_registry_cancels_an_in_flight_dare_lockdown()
    {
        Assert.Null(DareHead.Service);
        var api = new Api();
        var host = new Host();
        var keeper = new DareLockdownKeeper(host, new DareLockdownKeeper.MemoryStore());
        using var service = new DaresService(api, host, new DareMeter(), keeper);
        DareHead.Service = service;
        try
        {
            await service.RefreshAsync();
            var pending = service.ActAsync("b1", DareOp.StartLockdown);
            Assert.False(pending.IsCompleted);
            PanicSurfaces.All.Single(x => x.Id == "dares").Stop(null);
            api.Start.SetResult(new JObject { ["ok"] = true, ["status"] = "serving" });
            Assert.Equal(DareActResult.Refused, await pending);
            Assert.False(host.Active);
            Assert.Null(keeper.Pending);
            Assert.Contains("dares", MainShellWindow.BlinkStopIds);
        }
        finally { DareHead.Service = null; DareHead.Resume(); }
    }

    [Fact]
    public async Task An_ordinary_engine_stop_keeps_dares_running_and_a_panic_holds_them_until_a_user_start()
    {
        Assert.Null(DareHead.Service);
        var api = new Api();
        var host = new Host();
        var keeper = new DareLockdownKeeper(host, new DareLockdownKeeper.MemoryStore());
        using var service = new DaresService(api, host, new DareMeter(), keeper);
        DareHead.Service = service;
        try
        {
            await service.RefreshAsync();
            DareHead.Interrupt();   // an ordinary engine stop (StopEngine, OnEngineStopped)
            var live = service.ActAsync("b1", DareOp.StartLockdown);
            Assert.False(live.IsCompleted);   // still serving: the press reached the wire
            PanicSurfaces.All.Single(x => x.Id == "dares").Stop(null);
            api.Start.SetResult(new JObject { ["ok"] = true, ["status"] = "serving" });
            Assert.Equal(DareActResult.Refused, await live);
            DareHead.Interrupt();   // the panic's own engine stop must not lift the hold
            Assert.Equal(DareActResult.Refused, await service.ActAsync("b1", DareOp.StartLockdown));
            Assert.False(host.Active);
            DareHead.Resume();   // a user start
            Assert.Equal(DareActResult.Done, await service.ActAsync("b1", DareOp.StartLockdown));
            Assert.True(host.Active);
        }
        finally { DareHead.Service = null; DareHead.Resume(); }
    }
}
