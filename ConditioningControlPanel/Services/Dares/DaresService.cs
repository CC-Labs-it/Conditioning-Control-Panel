using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>When the dares poll runs. Pure: signed out = no request (only an account check), the
/// feature off = a slow look every 10 minutes, a fault = the friends slow cadence, anything open
/// (an offer, a running bet, a debt either way) = the friends fast cadence, else the slow one.</summary>
public static class DaresPollRule
{
    public const int IdleCheckSeconds = 15;
    public const int OffSeconds = 600;

    public static int NextIntervalSeconds(bool signedIn, bool off, bool fault, bool anyOpen)
    {
        if (!signedIn) return IdleCheckSeconds;
        if (off) return OffSeconds;
        if (fault) return FriendsPollRule.SlowSeconds;
        return anyOpen ? FriendsPollRule.FastSeconds : FriendsPollRule.SlowSeconds;
    }

    /// <summary>Wants the fast cadence: a slip, an offer, a running bet, or a debt not yet settled.</summary>
    public static bool AnyOpen(DaresSnapshot s) => s.Bets.Any(b =>
        b.State is DareState.Writing or DareState.Sent or DareState.Running || b.IOwe || b.TheyOwe);
}

/// <summary>
/// THE DARES SERVICE, hung off <c>App.Dares</c> (CONTRACT.md beside this file). Owns its poll
/// (<see cref="DaresPollRule"/>), the snapshot, the reports of this player's own counts
/// (<see cref="DareMeter"/>), and every side effect of a press:
/// <list type="bullet">
/// <item>Pay on Sparkles: the reply's <c>sp</c> is a debited receipt and is adopted through the wallet rule.</item>
/// <item>Pay on lock minutes: booked on the player's own Chaster tab FIRST, then <c>pay</c> with what booked;
/// <see cref="DareActResult.NeedsChaster"/> when no lock can take time.</item>
/// <item>StartLockdown: <c>start</c>, then the real Lockdown (<see cref="DareLockdownKeeper"/>); its outcome
/// goes back as <c>served</c>, after a crash too.</item>
/// </list>
/// Events are raised on the thread that called in, which in the app is the dispatcher (the timer, or
/// the UI awaiting a press). Nothing throws; a fault reads as TryLater.
/// </summary>
public sealed class DaresService : IDaresService, IDisposable
{
    private readonly IDaresApi _api;
    private readonly IDareHost _host;
    private readonly DareMeter _meter;
    private readonly DareLockdownKeeper _keeper;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, DareReading> _lastReported = new(StringComparer.Ordinal);
    private readonly HashSet<string> _lockBooked = new(StringComparer.Ordinal);

    private DispatcherTimer? _timer;
    private string? _lastAccount;
    private bool _off;
    private bool _fault;
    private bool _busy;
    private bool _disposed;

    public DaresService(IDaresApi api, IDareHost host, DareMeter meter, DareLockdownKeeper keeper, Func<DateTimeOffset>? now = null)
    {
        _api = api;
        _host = host;
        _meter = meter;
        _keeper = keeper;
        _now = now ?? (() => ServerClock.UtcNow);
        _keeper.Ended += OnServeEnded;
    }

    /// <summary>The app's wiring: the real wire, the app host, the meter file and the serve file.</summary>
    public static DaresService CreateForApp(out DareMeter meter)
    {
        var host = new AppDareHost();
        meter = new DareMeter(new DareMeter.FileStore(Path.Combine(App.UserDataPath, "dares_meter.json")));
        var keeper = new DareLockdownKeeper(host,
            new DareLockdownKeeper.FileStore(Path.Combine(App.UserDataPath, "dares_lockdown_serve.json")));
        return new DaresService(new DaresApi(), host, meter, keeper);
    }

    // ---- IDaresService ----

    public event Action? Changed;

    public DaresSnapshot Snapshot { get; private set; } = DaresSnapshot.Dark;

    public bool IsPreview => false;

    /// <summary>The camera check's state, set by the app's hooks (null until they exist).</summary>
    public Func<DareCameraState>? CameraProbe { get; set; }

    public DareCameraState Camera => CameraProbe?.Invoke() ?? DareCameraState.Idle;

    /// <summary>Raised after each successful state read with the bets, so the meter hooks can wake.</summary>
    public event Action? BetsRead;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!CheckAccount(out var account)) return;
        await ReadStateAsync(account!, ct);
    }

    public async Task<DareCreateOutcome> CreateAsync(string friendId, DareSlip slip, CancellationToken ct = default)
    {
        if (!CheckAccount(out _)) return new DareCreateOutcome(DareCreateStatus.TryLater);
        if (string.IsNullOrEmpty(friendId) || slip == null) return new DareCreateOutcome(DareCreateStatus.Refused);
        var numbered = DareSlipRules.Numbered(slip);
        if (!DareSlipRules.IsValid(numbered)) return new DareCreateOutcome(DareCreateStatus.Refused);
        var reply = await _api.CallAsync("create", new JObject
        {
            ["to"] = friendId,
            ["slip"] = DaresParse.SlipToWire(numbered),
            ["tz_min"] = Math.Clamp(_host.TzMinutes(), -840, 840),
        }, ct);
        if (DaresParse.IsOff(reply)) GoOff();
        var outcome = DaresParse.Create(reply);
        if (outcome.Status is DareCreateStatus.Sent or DareCreateStatus.Writing) await RefreshAsync(ct);
        return outcome;
    }

    public async Task<DareActResult> ActAsync(string betId, DareOp op, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(betId)) return DareActResult.Refused;
        if (!CheckAccount(out var account)) return DareActResult.TryLater;
        var bet = Snapshot.Bets.FirstOrDefault(b => b.Id == betId);
        DareActResult result;
        switch (op)
        {
            case DareOp.Accept: result = DaresParse.Act(await Call("accept", betId, ct)); break;
            case DareOp.Pass: result = DaresParse.Act(await Call("pass", betId, ct)); break;
            case DareOp.Cancel: result = DaresParse.Act(await Call("cancel", betId, ct)); break;
            case DareOp.Tick: result = DaresParse.Act(await Call("tick", betId, ct)); break;
            case DareOp.Renounce: result = DaresParse.Act(await Call("renounce", betId, ct)); break;
            case DareOp.DidIt: result = await DidItAsync(bet, betId, ct); break;
            case DareOp.StartLockdown: result = await StartLockdownAsync(account!, bet, ct); break;
            case DareOp.Pay: result = await PayAsync(account!, bet, ct); break;
            default: result = DareActResult.Refused; break;
        }
        if (result == DareActResult.Off) GoOff();
        if (result is DareActResult.Done or DareActResult.Gone) await RefreshAsync(ct);
        return result;
    }

    // ---- the presses with side effects ----

    private async Task<DareActResult> PayAsync(string account, DareBet? bet, CancellationToken ct)
    {
        if (bet == null) return DareActResult.Gone;
        if (!bet.IOwe) return DareActResult.Refused;
        var stake = bet.MyStake;
        switch (stake.Kind)
        {
            case DareStakeKind.Lockdown: return await StartLockdownAsync(account, bet, ct);
            case DareStakeKind.Dare: return await DidItAsync(bet, bet.Id, ct);
            case DareStakeKind.Sp:
            {
                var reply = await Call("pay", bet.Id, ct);
                var r = DaresParse.Act(reply);
                if (r == DareActResult.Done && reply?["sp"] is JValue { Type: JTokenType.Integer } sp) _host.AdoptSp(account, sp.Value<int>());
                return r;
            }
            case DareStakeKind.Lock:
            {
                int booked = stake.Amount * 60;
                if (!_lockBooked.Contains(bet.Id))
                {
                    if (!_host.ChasterReady()) return DareActResult.NeedsChaster;
                    booked = _host.BookLockSeconds(stake.Amount * 60);
                    _lockBooked.Add(bet.Id);
                }
                var r = DaresParse.Act(await _api.CallAsync("pay", new JObject { ["id"] = bet.Id, ["booked"] = booked }, ct));
                if (r is DareActResult.Done or DareActResult.Gone) _lockBooked.Remove(bet.Id);
                return r;
            }
            default: return DareActResult.Refused;
        }
    }

    private async Task<DareActResult> StartLockdownAsync(string account, DareBet? bet, CancellationToken ct)
    {
        if (bet == null) return DareActResult.Gone;
        if (!bet.IOwe || bet.MyStake.Kind != DareStakeKind.Lockdown) return DareActResult.Refused;
        if (_keeper.Serving || _host.LockdownActive) return DareActResult.LockdownBusy;
        // Already "serving" on the server (the start went out and the Lockdown never ran here): run it.
        if (bet.Debt?.State != DareDebtState.Serving)
        {
            var r = DaresParse.Act(await Call("start", bet.Id, ct));
            if (r != DareActResult.Done) return r;
        }
        return _keeper.Begin(account, bet.Id, bet.MyStake.Amount, _now()) ? DareActResult.Done : DareActResult.Refused;
    }

    private async Task<DareActResult> DidItAsync(DareBet? bet, string betId, CancellationToken ct)
    {
        // A running "can't" bet about a dare: the receiver's "I did it" is its count of 1.
        if (bet is { State: DareState.Running, Me: DareSide.To, Type: DareType.Cant, What.Kind: DareKind.Dare })
        {
            var reply = await _api.CallAsync("report", new JObject { ["id"] = betId, ["count"] = 1 }, ct);
            return DaresParse.Act(reply);
        }
        return DaresParse.Act(await Call("did", betId, ct));
    }

    private async void OnServeEnded(DareServe serve)
    {
        try { await SendServeAsync(); }
        catch (Exception ex) { App.Logger?.Debug("Dares serve send failed: {E}", ex.Message); }
    }

    private async Task SendServeAsync()
    {
        var s = _keeper.Pending;
        if (s?.Outcome == null || _disposed) return;
        if (!string.Equals(_host.Account(), s.Account, StringComparison.Ordinal)) return;
        var reply = await _api.CallAsync("served", new JObject { ["id"] = s.BetId, ["outcome"] = s.Outcome });
        if (reply == null) return;
        // A worded answer (paid, owed again, gone) is final for this serve: never send it twice.
        _keeper.Delivered(s.BetId);
        if (DaresParse.IsOff(reply)) { GoOff(); return; }
        await RefreshAsync();
    }

    // ---- the poll ----

    public void Start()
    {
        if (_disposed || _timer != null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) =>
        {
            _timer?.Stop();
            try { await TickAsync(); }
            catch (Exception ex) { App.Logger?.Debug("Dares poll failed: {E}", ex.Message); }
            finally { Reschedule(); }
        };
        _timer.Start();
    }

    /// <summary>Poll now (sign-in, the drawer opening). Safe from any thread.</summary>
    public void Kick()
    {
        var t = _timer;
        if (t == null || _disposed) return;
        void Go()
        {
            if (_disposed || _timer == null || _busy) return;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromMilliseconds(300);
            _timer.Start();
        }
        if (t.Dispatcher.CheckAccess()) Go(); else t.Dispatcher.BeginInvoke((Action)Go);
    }

    internal int NextIntervalSeconds() =>
        DaresPollRule.NextIntervalSeconds(SafeAccount() != null, _off, _fault, DaresPollRule.AnyOpen(Snapshot) || _keeper.Pending != null);

    private void Reschedule()
    {
        if (_disposed || _timer == null) return;
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(NextIntervalSeconds());
        _timer.Start();
    }

    /// <summary>One poll: an owed serve out, the state in, then a report for each bet this client counts.</summary>
    public async Task TickAsync(CancellationToken ct = default)
    {
        if (_busy || _disposed) return;
        if (!CheckAccount(out var account)) return;
        _busy = true;
        try
        {
            if (_keeper.Pending?.Outcome != null) await SendServeAsync();
            if (!await ReadStateAsync(account!, ct)) return;
            await ReportAsync(account!, ct);
            _meter.Save();
        }
        finally { _busy = false; }
    }

    private async Task<bool> ReadStateAsync(string account, CancellationToken ct)
    {
        var reply = await _api.CallAsync("state", new JObject(), ct);
        if (!string.Equals(SafeAccount(), account, StringComparison.Ordinal)) return false;
        if (reply == null) { _fault = true; return false; }
        _fault = false;
        if (reply.Value<bool?>("ok") != true)
        {
            // Off, or a refusal from an older server: the doors hide.
            _off = DaresParse.IsOff(reply);
            Publish(DaresSnapshot.Dark);
            return false;
        }
        _off = false;
        var snap = DaresParse.State(reply) ?? DaresSnapshot.Dark;
        _meter.Track(snap.Bets);
        Publish(snap);
        try { BetsRead?.Invoke(); } catch (Exception ex) { App.Logger?.Debug("Dares bets handler failed: {E}", ex.Message); }
        return true;
    }

    private async Task ReportAsync(string account, CancellationToken ct)
    {
        foreach (var b in Snapshot.Bets.ToList())
        {
            if (!DareMeter.Reports(b) || _meter.Reading(b.Id) is not { } r) continue;
            if (_lastReported.TryGetValue(b.Id, out var last) && last == r) continue;
            var body = new JObject { ["id"] = b.Id, ["count"] = r.Count };
            if (r.Camera) { body["seen_s"] = r.SeenSeconds; body["counted_s"] = r.CountedSeconds; }
            var reply = await _api.CallAsync("report", body, ct);
            if (!string.Equals(SafeAccount(), account, StringComparison.Ordinal)) return;
            if (reply?.Value<bool?>("ok") != true) continue;
            _lastReported[b.Id] = r;
            if (DaresParse.Bet(reply["bet"] as JObject) is { } fresh)
                Publish(Snapshot with { Bets = Snapshot.Bets.Select(x => x.Id == fresh.Id ? fresh : x).ToList() });
        }
    }

    // ---- plumbing ----

    private Task<JObject?> Call(string op, string id, CancellationToken ct) =>
        _api.CallAsync(op, new JObject { ["id"] = id }, ct);

    private void GoOff()
    {
        _off = true;
        Publish(DaresSnapshot.Dark);
    }

    private string? SafeAccount()
    {
        try { return _host.Account(); } catch { return null; }
    }

    /// <summary>True when signed in. A different account (or none) drops the snapshot.</summary>
    private bool CheckAccount(out string? account)
    {
        account = SafeAccount();
        if (!string.Equals(account, _lastAccount, StringComparison.Ordinal))
        {
            _lastAccount = account;
            _off = false;
            _fault = false;
            _lastReported.Clear();
            _lockBooked.Clear();
            if (account != null) _meter.SetAccount(account);
            Publish(DaresSnapshot.Dark);
        }
        return account != null;
    }

    private void Publish(DaresSnapshot next)
    {
        if (DaresSnapshotEquality.Same(Snapshot, next)) return;
        Snapshot = next;
        try { Changed?.Invoke(); }
        catch (Exception ex) { App.Logger?.Debug("Dares change handler failed: {E}", ex.Message); }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Stop();
        _timer = null;
        _meter.Save();
    }
}

/// <summary>Snapshots hold lists; this compares contents so a quiet poll redraws nothing.</summary>
public static class DaresSnapshotEquality
{
    public static bool Same(DaresSnapshot a, DaresSnapshot b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Available != b.Available || !Equals(a.Me, b.Me) || a.Bets.Count != b.Bets.Count) return false;
        for (var i = 0; i < a.Bets.Count; i++)
        {
            var x = a.Bets[i];
            var y = b.Bets[i];
            if (!(x with { Dares = Array.Empty<DareSeal>() }).Equals(y with { Dares = Array.Empty<DareSeal>() })) return false;
            if (!x.Dares.SequenceEqual(y.Dares)) return false;
        }
        return true;
    }
}
