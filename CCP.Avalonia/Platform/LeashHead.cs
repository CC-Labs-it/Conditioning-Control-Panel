// PORTED from WPF ConditioningControlPanel/App.xaml.cs:2991-3022 (THE LEASH wiring) and :6289-6292 (exit),
// Services/Leash/LeashServiceApp.cs CreateForApp + AppDayInputs, LeashCutSafetyApp.cs AppTargets and
// ChasterLeashTab.cs. The service itself is Core (CCP.Core/Services/Leash); this is the head's half.
// The task runner is Platform/LeashTaskHost (gate card: MainShellWindow.LeashGate).
// ponytail: the explainer/drawer UI lands with the Drawer unit.
using System;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF App.Leash on this head. Built after the friends poll; rides it (no timer of its own).</summary>
internal static class LeashHead
{
    /// <summary>WPF App.Leash. Null until startup built it and after exit.</summary>
    internal static ILeashService? Service
    {
        get => _service;
        set { if (ReferenceEquals(_service, value)) return; _service = value; ServiceChanged?.Invoke(); }
    }
    private static ILeashService? _service;
    internal static event Action? ServiceChanged;

    /// <summary>WPF App.LeashedChanged: the account went on or off a leash.</summary>
    internal static event Action<bool>? LeashedChanged;

    /// <summary>Seeds Core's static seams for this head (WPF LeashCutSafetyApp.Seed + App.xaml.cs:3003, :3011).</summary>
    internal static void Seed()
    {
        LeashCutSafety.LiveTargets = Targets.Instance;
        LeashCutSafety.RunOnUi = a =>
        {
            if (Dispatcher.UIThread.CheckAccess()) a();
            else Dispatcher.UIThread.Invoke(a);
        };
        LeashGuard.IsLeashed = () => Service?.Snapshot.Me != null;
        LeashTaskRunner.TimerFactory ??= () => new FriendsHead.Timer();
    }

    /// <summary>WPF App.xaml.cs LEASH: the service on the friends poll (report out, block in, 20 s
    /// cadence while leashed or holding). The account is read per call, so sign-in/out needs no restart
    /// (GateDue is null whenever no account is signed in). Null when the friends wire has no url.</summary>
    internal static LeashService? Start(FriendsService friends, string? userDataDir, string? overrideUrl, ILeashApi? api = null)
    {
        Seed();
        if (FriendsHead.BaseUrl(userDataDir, overrideUrl) is not { } url) return null;
        var svc = new LeashService(
            api ?? new LeashApi(null, FriendsHead.Identity, url),
            () => FriendsHead.Identity()?.UnifiedId,
            dayInputs: DayInputs,
            tab: new Tab(),
            kick: friends.Kick,
            cutStore: new LeashService.FileCutStore(Path.Combine(CorePaths.UserData, "leash_cut_pending.txt")));
        svc.Attach(friends);
        friends.LeashReportProvider = svc.BuildReportJson;
        friends.LeashActive = () => svc.Active;
        friends.LeashBlockArrived += svc.ApplyBlock;
        svc.LeashedChanged += on =>
        {
            try { LeashedChanged?.Invoke(on); }
            catch (Exception ex) { Serilog.Log.Debug("Leash changed handler failed: {E}", ex.Message); }
        };
        Publish(svc);
        return svc;
    }

    /// <summary>WPF MainWindow.Leash.cs:95 LeashBlocksGames: the launcher's gate asks the service, and the
    /// panel's gate card presents it.</summary>
    private static void Publish(ILeashService svc)
    {
        Service = svc;
        Views.Windows.MainShellWindow.LeashGateDueProvider = () => Service?.GateDue != null;
        Views.Windows.MainShellWindow.PresentLeashGateProvider = shell => shell.PresentLeashGate();
    }

    /// <summary>WPF LeashLocator's CCP_LEASH_DEMO: a Debug build in a sandbox (CCP_USERDATA_DIR) with
    /// CCP_LEASH_DEMO=&lt;block.json&gt; runs a leash with no server, fed that friends-poll leash block, so a
    /// live check can see the gate. Never in a Release build, never outside a sandbox.</summary>
    internal static void StartDemoIfAsked()
    {
#if DEBUG
        var path = Environment.GetEnvironmentVariable("CCP_LEASH_DEMO");
        if (Service != null || !SandboxNet.Active || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        Seed();
        var svc = new LeashService(new DemoApi(), () => "u_demo");
        // As the wire reads it: dates stay strings (JObject.Parse would turn them into local DateTimes).
        svc.ApplyBlock(Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(File.ReadAllText(path),
            new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None }));
        Publish(svc);
        Serilog.Log.Information("[Leash] demo leash from {Path}", path);
#endif
    }

#if DEBUG
    private sealed class DemoApi : ILeashApi
    {
        public System.Threading.Tasks.Task<Newtonsoft.Json.Linq.JObject?> CallAsync(string op, Newtonsoft.Json.Linq.JObject body, System.Threading.CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult<Newtonsoft.Json.Linq.JObject?>(new Newtonsoft.Json.Linq.JObject { ["ok"] = true });
    }
#endif

    /// <summary>WPF App.OnExit:6289-6292: the leash goes with the app.</summary>
    internal static void Stop()
    {
        Service = null;
        Views.Windows.MainShellWindow.LeashGateDueProvider = null;
        Views.Windows.MainShellWindow.PresentLeashGateProvider = null;
    }

    /// <summary>WPF LeashServiceApp.AppDayInputs: R's numbers, read fresh.</summary>
    internal static LeashDayInputs? DayInputs()
    {
        var local = DateTime.Now;
        int minutes = 0;
        try { minutes = App.FeatureDayLog?.ConditioningMinutesOn(local) ?? 0; }
        catch (Exception ex) { Serilog.Log.Debug("Leash minutes read failed: {E}", ex.Message); }

        int done = 0, total = 0;
        try
        {
            var slots = App.Quests?.GetDailySlots();
            if (slots != null)
                foreach (var (q, _) in slots)
                {
                    if (q == null) continue;
                    total++;
                    if (q.IsCompleted) done++;
                }
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash quests read failed: {E}", ex.Message); }

        var streak = 0;
        try { streak = App.Achievements?.Progress?.ConsecutiveDays ?? 0; } catch { }

        bool linked = false;
        DateTime? ends = null;
        bool hidden = false;
        int? tab = null;
        try
        {
            var c = ChasterHead.Service;
            if (c != null && c.TakesLeashTime)
            {
                linked = true;
                var l = c.Lock;
                ends = l?.EndsAtUtc;
                hidden = l?.TimerHidden == true;
                tab = c.BalanceSeconds;
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("Leash chaster read failed: {E}", ex.Message); }

        return new LeashDayInputs(local, minutes, done, total, streak, linked, ends, hidden, tab);
    }

    /// <summary>WPF ChasterLeashTab: the same NoteSeconds path, literal row ids.</summary>
    private sealed class Tab : ILeashTab
    {
        public int BookPunish(int seconds)
        {
            if (seconds <= 0) return 0;
            try { return ChasterHead.Service?.NoteSeconds("leash", seconds).AppliedSeconds ?? 0; }
            catch (Exception ex) { Serilog.Log.Debug("Leash tab punish failed: {E}", ex.Message); return 0; }
        }

        public int BookCredit(int seconds)
        {
            if (seconds <= 0) return 0;
            try { return ChasterHead.Service?.NoteSeconds("leash_credit", -seconds).AppliedSeconds ?? 0; }
            catch (Exception ex) { Serilog.Log.Debug("Leash tab credit failed: {E}", ex.Message); return 0; }
        }
    }

    /// <summary>WPF LeashCutSafetyApp.AppTargets on this head. Every member tolerates a service that is not built.</summary>
    internal sealed class Targets : LeashCutSafety.ITargets
    {
        internal static readonly Targets Instance = new();

        public bool LockdownActive => LockdownService.Current?.IsActive == true;
        public void EndLockdown() => LockdownService.Current?.Deactivate();
        public void DiscardLockdownRecovery() => LockdownService.DiscardRecovery();

        public bool RemoteActive =>
            Views.Tabs.RemoteControlTabView.Relay.IsValueCreated && Views.Tabs.RemoteControlTabView.Relay.Value.IsActive;
        public void EndRemote() => Views.Tabs.RemoteControlTabView.Relay.Value.EndSessionNow();

        public bool StrictVideoRunning => CoreEngine.Video is { IsPlaying: true, IsStrict: true };
        public void StopVideo() => CoreEngine.Video?.ForceCleanup();

        public void ReleaseSafetySettings()
        {
            var s = CoreSettings.Current;
            s.StrictLockEnabled = false;
            s.PanicKeyEnabled = true;
            CoreSettings.SaveImmediate();
            // WPF SyncNoPanicState: the panic hook and its checkbox move with the flag.
            try { LockdownService.PanicKeyUiSync?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Warning("[Leash] Panic-key UI sync failed: {Error}", ex.Message); }
        }

        /// <summary>WPF LeashCutSafetyApp: only the holder's leash time not on the lock yet goes
        /// (CircesTab.DropUnpushedLeash, owner 2026-10-22). False when there is no tab or nothing was dropped.</summary>
        public bool DropUnpushedLeashBookings() => (ChasterHead.Service?.DropUnpushedLeash() ?? 0) > 0;
    }
}
