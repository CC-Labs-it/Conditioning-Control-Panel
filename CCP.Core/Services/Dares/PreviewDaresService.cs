using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// THE LOCAL DARES PREVIEW (BRIEF lane C). In a DEBUG build started with <c>CCP_DARES_PREVIEW=1</c>,
/// <c>App.Dares</c> is this: no network at all, state in <c>&lt;user data&gt;/dares-preview.json</c>
/// (a restart brings the debt card back), three fake friends, an offer that accepts itself after about
/// 4 s, a friend's count that climbs, deadlines squeezed into minutes (3h = 3 min, today = 5, weekend =
/// 8, week = 10) and <see cref="SettleNowAsync"/> to end a bet at once.
///
/// <para>Walls: Sparkles move only inside the preview file (never the real balance); a lock debt books
/// nothing; a Lockdown debt runs the REAL Lockdown (<see cref="DareLockdownKeeper"/>), because that is
/// what the owner must see. This player's own counts come from a real <see cref="DareMeter"/> fed by
/// the same hooks as the live service. In a Release build <see cref="Enabled"/> is false whatever the
/// environment says, and the App wiring sits behind <c>#if DEBUG</c> as well.</para>
/// </summary>
public sealed class PreviewDaresService : IDaresService, IDisposable
{
    public const string EnvVar = "CCP_DARES_PREVIEW";
    public const string FileName = "dares-preview.json";

    public sealed record FakeFriend(string Id, string Name, int Tier);

    /// <summary>The three fake friends. One is tier 0, so the locked Lockdown tile can be seen.</summary>
    public static readonly IReadOnlyList<FakeFriend> Fakes = new[]
    {
        new FakeFriend("u_preview_velvet", "Velvet", 2),
        new FakeFriend("u_preview_moth", "Moth", 1),
        new FakeFriend("u_preview_juniper", "Juniper", 0),
    };

    public static readonly TimeSpan AcceptAfter = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan FriendActsAfter = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan OfferAfter = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan BanLength = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan OwedLapse = TimeSpan.FromMinutes(30);

    internal static bool EnabledFor(string? env)
    {
#if DEBUG
        return env?.Trim() == "1";
#else
        return false;
#endif
    }

    public static bool Enabled
    {
        get { try { return EnabledFor(Environment.GetEnvironmentVariable(EnvVar)); } catch { return false; } }
    }

    /// <summary>A deadline squeezed for the preview.</summary>
    public static TimeSpan Compressed(DareDeadline d) => d switch
    {
        DareDeadline.ThreeHours => TimeSpan.FromMinutes(3),
        DareDeadline.Today => TimeSpan.FromMinutes(5),
        DareDeadline.Weekend => TimeSpan.FromMinutes(8),
        _ => TimeSpan.FromMinutes(10),
    };

    /// <summary>What a fake friend's count reaches by the end of a duel, before its pace.</summary>
    public static int DuelScale(DareKind k) => k switch
    {
        DareKind.Pics => 300, DareKind.Minutes => 4, DareKind.Video => 3, DareKind.Bubbles => 120, _ => 1,
    };

    // ---- the file ----

    public sealed class Row
    {
        public DareBet Bet { get; set; } = null!;
        /// <summary>The fake friend's pace: above 1 makes a target, below misses it.</summary>
        public double Pace { get; set; } = 1;
        /// <summary>When the fake friend does its next thing (accept, fill words, pay, tick).</summary>
        public DateTimeOffset? Due { get; set; }
        public int FriendTier { get; set; } = 1;
        public DateTimeOffset? OwedSince { get; set; }
    }

    public sealed class Data
    {
        public int Sp { get; set; } = 1000;
        public DateTimeOffset? BannedUntil { get; set; }
        public DateTimeOffset? FirstSeen { get; set; }
        public bool OfferMade { get; set; }
        public int Seq { get; set; }
        public List<Row> Rows { get; set; } = new();
    }

    private readonly string? _path;
    private readonly Func<DateTimeOffset> _now;
    private readonly Random _rng;
    private readonly DareMeter _meter;
    private readonly DareLockdownKeeper _keeper;
    private readonly Func<bool> _mySub;
    private readonly Func<string, (string Name, int Tier)?> _friend;
    private readonly double? _pace;
    private Data _data;
    private IUiTimer? _timer;
    private volatile bool _held;

    public PreviewDaresService(string? path, DareMeter meter, DareLockdownKeeper keeper,
        Func<DateTimeOffset>? now = null, Random? rng = null, Func<bool>? mySub = null,
        Func<string, (string Name, int Tier)?>? friend = null, double? pace = null)
    {
        _pace = pace;
        _path = path;
        _meter = meter;
        _keeper = keeper;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _rng = rng ?? new Random();
        _mySub = mySub ?? (() => TierGate.HasPremium);
        _friend = friend ?? (id => FakeOr(null, id));
        _data = Load();
        _data.FirstSeen ??= _now();
        _meter.SetAccount("preview");
        _keeper.Ended += OnServeEnded;
        Step();
    }

    /// <summary>The app's preview: its own files beside the live ones, the app's Lockdown host.</summary>
    public static PreviewDaresService Create(string userData, IDareHost host, Func<IFriendsService?> friends, out DareMeter meter)
    {
        meter = new DareMeter(new DareMeter.FileStore(Path.Combine(userData, "dares-preview-meter.json")));
        var keeper = new DareLockdownKeeper(host,
            new DareLockdownKeeper.FileStore(Path.Combine(userData, "dares-preview-serve.json")));
        return new PreviewDaresService(Path.Combine(userData, FileName), meter, keeper, friend: id => FakeOr(friends(), id));
    }

    /// <summary>A fake friend first, else the app's friends list.</summary>
    public static (string Name, int Tier)? FakeOr(IFriendsService? friends, string id)
    {
        var fake = Fakes.FirstOrDefault(f => f.Id == id);
        if (fake != null) return (fake.Name, fake.Tier);
        try
        {
            var f = friends?.Snapshot.Friends.FirstOrDefault(x => x.Id == id);
            return f == null ? null : (f.Name, f.Tier);
        }
        catch { return null; }
    }

    // ---- IDaresService ----

    public event Action? Changed;

    public bool IsPreview => true;

    /// <summary>The camera check's state, set by the app's hooks.</summary>
    public Func<DareCameraState>? CameraProbe { get; set; }

    public DareCameraState Camera => CameraProbe?.Invoke() ?? DareCameraState.Idle;

    /// <summary>The preview wallet. Never the real balance.</summary>
    public int PreviewSp => _data.Sp;

    public int? Sparkles => _data.Sp;

    public DaresSnapshot Snapshot { get; private set; } = DaresSnapshot.Dark;

    public void Start(IUiTimer timer)
    {
        if (_timer != null) return;
        _timer = timer;
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += OnTick;
        if (!_held) timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        try { if (!_held) Step(); }
        catch (Exception ex) { Log.Debug("Dares preview step failed: {E}", ex.Message); }
    }

    /// <summary>A stop or panic: the clock halts and every press is refused until <see cref="Resume"/>
    /// (the live service's CancelPending hold).</summary>
    public void CancelPending()
    {
        _held = true;
        var timer = _timer;
        timer?.OnUiThread(() => { if (_held) timer.Stop(); });
    }

    public void Resume()
    {
        _held = false;
        var timer = _timer;
        timer?.OnUiThread(() => { if (!_held) timer.Start(); });
    }

    public Task RefreshAsync(CancellationToken ct = default) { if (!_held) Step(); return Task.CompletedTask; }

    public Task<DareCreateOutcome> CreateAsync(string friendId, DareSlip slip, CancellationToken ct = default)
    {
        if (_held) return Done(new DareCreateOutcome(DareCreateStatus.TryLater));
        var now = _now();
        if (_friend(friendId) is not { } who) return Done(new DareCreateOutcome(DareCreateStatus.NotFriends));
        var s = DareSlipCheck.Numbered(slip);
        if (!DareSlipCheck.IsValid(s)) return Done(new DareCreateOutcome(DareCreateStatus.Refused));
        if (Banned(now)) return Done(new DareCreateOutcome(DareCreateStatus.Banned));
        if (OpenCount() >= DareGrammar.MaxOpenBets) return Done(new DareCreateOutcome(DareCreateStatus.Limit));
        if ((s.FromStake.Kind == DareStakeKind.Lockdown && !_mySub()) || (s.ToStake.Kind == DareStakeKind.Lockdown && !DareSlipCheck.LockdownTierOk(who.Tier)))
            return Done(new DareCreateOutcome(DareCreateStatus.LockdownNeedsSub));
        if (s.FromStake.Kind == DareStakeKind.Sp && _data.Sp < s.FromStake.Amount) return Done(new DareCreateOutcome(DareCreateStatus.Short));

        var writing = DareSlipCheck.HasDare(s);
        var id = "pv" + (++_data.Seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var bet = new DareBet(id, DareSide.From, new DareFriend(friendId, who.Name, null), s.Type, s.What, s.Deadline,
            now + Compressed(s.Deadline), s.Camera, s.FromStake, s.ToStake, DareSlipCheck.Seals(s),
            writing ? DareState.Writing : DareState.Sent, null, 0, 0, null, null, null, now, null, null, null);
        _data.Rows.Insert(0, new Row { Bet = bet, Pace = _pace ?? 0.6 + _rng.NextDouble() * 0.8, Due = now + (writing ? FriendActsAfter : AcceptAfter), FriendTier = who.Tier });
        Commit();
        return Done(new DareCreateOutcome(writing ? DareCreateStatus.Writing : DareCreateStatus.Sent, id, writing ? "PRV" + (_data.Seq % 1000).ToString("D3", System.Globalization.CultureInfo.InvariantCulture) : null));
    }

    public Task<DareActResult> ActAsync(string betId, DareOp op, CancellationToken ct = default)
    {
        if (_held) return Done(DareActResult.Refused);
        var now = _now();
        var row = _data.Rows.FirstOrDefault(r => r.Bet.Id == betId);
        if (row == null) return Done(DareActResult.Gone);
        var b = row.Bet;
        var r = DareActResult.Done;
        switch (op)
        {
            case DareOp.Accept:
                if (!b.IsOfferToMe) { r = DareActResult.Gone; break; }
                if (Banned(now)) { r = DareActResult.Banned; break; }
                if (b.ToStake.Kind == DareStakeKind.Lockdown && !_mySub()) { r = DareActResult.LockdownNeedsSub; break; }
                if (b.ToStake.Kind == DareStakeKind.Sp && _data.Sp < b.ToStake.Amount) { r = DareActResult.Short; break; }
                row.Bet = Run(b, now);
                break;
            case DareOp.Pass:
                if (!b.IsOfferToMe) { r = DareActResult.Gone; break; }
                row.Bet = b with { State = DareState.Passed };
                break;
            case DareOp.Cancel:
                if (b.Me != DareSide.From || b.State is not (DareState.Writing or DareState.Sent)) { r = DareActResult.Gone; break; }
                row.Bet = b with { State = DareState.Cancelled };
                break;
            case DareOp.DidIt:
                if (b is { State: DareState.Running, Me: DareSide.To, What.Kind: DareKind.Dare })
                    row.Bet = Settle(b with { ToCount = 1 }, now, early: true);
                else if (b.IOwe && b.MyStake.Kind == DareStakeKind.Dare)
                {
                    row.Bet = b with { Debt = b.Debt! with { State = DareDebtState.Claimed } };
                    row.Due = now + FriendActsAfter;
                }
                else r = DareActResult.Refused;
                break;
            case DareOp.Tick:
                if (!b.TheyOwe || b.Debt?.State != DareDebtState.Claimed) { r = DareActResult.Refused; break; }
                row.Bet = b with { Debt = b.Debt with { State = DareDebtState.Paid } };
                break;
            case DareOp.Renounce:
                if (!b.IOwe) { r = DareActResult.Refused; break; }
                row.Bet = b with { Debt = b.Debt! with { State = DareDebtState.Renounced }, RenouncedBy = b.Me };
                _data.BannedUntil = now + BanLength;
                break;
            case DareOp.Pay:
                r = Pay(row, now);
                break;
            case DareOp.StartLockdown:
                r = StartLockdown(row, now);
                break;
            default:
                r = DareActResult.Refused;
                break;
        }
        Commit();
        return Done(r);
    }

    public Task SettleNowAsync(string betId, CancellationToken ct = default)
    {
        var row = _data.Rows.FirstOrDefault(r => r.Bet.Id == betId);
        if (!_held && row?.Bet.State == DareState.Running)
        {
            var now = _now();
            row.Bet = Settle(Climb(row, row.Bet, row.Bet.EndsAt), now, early: false);
            Commit();
        }
        return Task.CompletedTask;
    }

    // ---- presses ----

    private DareActResult Pay(Row row, DateTimeOffset now)
    {
        var b = row.Bet;
        if (!b.IOwe) return DareActResult.Refused;
        switch (b.MyStake.Kind)
        {
            case DareStakeKind.Sp:
                // Preview wallet only: the real balance never moves.
                _data.Sp -= b.MyStake.Amount;
                row.Bet = b with { Debt = b.Debt! with { State = DareDebtState.Paid } };
                return DareActResult.Done;
            case DareStakeKind.Lock:
                // Books nothing real: only marks it paid.
                row.Bet = b with { Debt = b.Debt! with { State = DareDebtState.Paid } };
                return DareActResult.Done;
            case DareStakeKind.Lockdown:
                return StartLockdown(row, now);
            default:
                row.Bet = b with { Debt = b.Debt! with { State = DareDebtState.Claimed } };
                row.Due = now + FriendActsAfter;
                return DareActResult.Done;
        }
    }

    private DareActResult StartLockdown(Row row, DateTimeOffset now)
    {
        var b = row.Bet;
        if (!b.IOwe || b.MyStake.Kind != DareStakeKind.Lockdown) return DareActResult.Refused;
        if (_keeper.Serving) return DareActResult.LockdownBusy;
        if (!_keeper.Begin("preview", b.Id, b.MyStake.Amount, now)) return DareActResult.LockdownBusy;
        row.Bet = b with { Debt = b.Debt! with { State = DareDebtState.Serving } };
        return DareActResult.Done;
    }

    private void OnServeEnded(DareServe serve) => ApplyServe(serve);

    private void ApplyServe(DareServe serve)
    {
        if (serve.Outcome == null) return;
        var row = _data.Rows.FirstOrDefault(r => r.Bet.Id == serve.BetId);
        _keeper.Delivered(serve.BetId);
        if (row?.Bet.Debt is not { } d) { Commit(); return; }
        if (serve.Outcome == DareLockdownRule.Served) row.Bet = row.Bet with { Debt = d with { State = DareDebtState.Paid } };
        else if (!d.RetryUsed) row.Bet = row.Bet with { Debt = d with { State = DareDebtState.Owed, RetryUsed = true } };
        else row.Bet = row.Bet with { Debt = d with { State = DareDebtState.Paid, EndedEarly = true } };
        Commit();
    }

    // ---- the clock ----

    /// <summary>One second of the preview world: the fake friends act, counts climb, bets settle.</summary>
    public void Step()
    {
        var now = _now();
        if (_keeper.Pending is { Outcome: not null } owed) ApplyServe(owed);
        MaybeOffer(now);
        foreach (var row in _data.Rows.ToList())
        {
            var b = row.Bet;
            var due = row.Due is { } d && now >= d;
            switch (b.State)
            {
                case DareState.Writing when due:
                    row.Bet = b with { State = DareState.Sent };
                    row.Due = now + AcceptAfter;
                    break;
                case DareState.Sent when due && b.Me == DareSide.From:
                    row.Bet = Run(b, now);
                    row.Due = null;
                    break;
                case DareState.Running:
                    var climbed = Climb(row, b, now);
                    row.Bet = now >= b.EndsAt ? Settle(climbed, now, early: false) : SettleEarly(climbed, now);
                    if (row.Bet.State == DareState.Settled && row.Bet.TheyOwe) row.Due = now + FriendActsAfter;
                    break;
                case DareState.Settled:
                    Debts(row, b, now, due);
                    break;
            }
        }
        // Open bets plus the last 20 finished, newest first.
        var finished = _data.Rows.Where(r => !IsOpen(r.Bet)).Skip(20).ToList();
        foreach (var f in finished) _data.Rows.Remove(f);
        _meter.Track(_data.Rows.Select(r => r.Bet));
        Commit();
    }

    private void Debts(Row row, DareBet b, DateTimeOffset now, bool due)
    {
        if (b.Debt is not { } d) return;
        if (b.IOwe && d.State == DareDebtState.Owed)
        {
            row.OwedSince ??= now;
            if (now - row.OwedSince >= OwedLapse)
            {
                row.Bet = b with { Debt = d with { State = DareDebtState.Renounced }, RenouncedBy = b.Me };
                _data.BannedUntil = now + BanLength;
            }
            return;
        }
        if (b.IOwe && d.State == DareDebtState.Claimed && due)
        {
            row.Bet = b with { Debt = d with { State = DareDebtState.Paid } };
            row.Due = null;
            return;
        }
        if (!b.TheyOwe || !due) return;
        // The fake friend pays: Sparkles land in the preview wallet; a dare is claimed for me to tick.
        if (b.TheirStake.Kind == DareStakeKind.Dare)
        {
            if (d.State == DareDebtState.Owed) row.Bet = b with { Debt = d with { State = DareDebtState.Claimed } };
        }
        else
        {
            if (b.TheirStake.Kind == DareStakeKind.Sp) _data.Sp += b.TheirStake.Amount;
            row.Bet = b with { Debt = d with { State = DareDebtState.Paid } };
        }
        row.Due = null;
    }

    private void MaybeOffer(DateTimeOffset now)
    {
        if (_data.OfferMade || _data.FirstSeen is not { } first || now - first < OfferAfter) return;
        _data.OfferMade = true;
        var f = Fakes[0];
        var id = "pv" + (++_data.Seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _data.Rows.Insert(0, new Row
        {
            FriendTier = f.Tier,
            Bet = new DareBet(id, DareSide.To, new DareFriend(f.Id, f.Name, null), DareType.Cant, new DareWhat(DareKind.Pics, "100"),
                DareDeadline.ThreeHours, now + Compressed(DareDeadline.ThreeHours), false,
                new DareStake(DareStakeKind.Sp, 100), new DareStake(DareStakeKind.Sp, 50), Array.Empty<DareSeal>(),
                DareState.Sent, null, 0, 0, null, null, null, now, null, null, null),
        });
    }

    private static DareBet Run(DareBet b, DateTimeOffset now) =>
        b with { State = DareState.Running, AcceptedAt = now, EndsAt = now + Compressed(b.Deadline) };

    /// <summary>Counts at <paramref name="at"/>: mine from the meter, the fake friend's from its pace.</summary>
    private DareBet Climb(Row row, DareBet b, DateTimeOffset at)
    {
        var mine = _meter.Reading(b.Id)?.Count ?? b.MyCount;
        var start = b.AcceptedAt ?? b.At;
        var span = (b.EndsAt - start).TotalSeconds;
        var frac = span <= 0 ? 1 : Math.Clamp((at - start).TotalSeconds / span, 0, 1);
        var theirs = b.TheirCount;
        var friendCounts = b.Type == DareType.Duel || b.Me == DareSide.From;
        if (friendCounts)
        {
            if (b.What.Kind is DareKind.Session or DareKind.Dare)
                theirs = row.Pace >= 1 && frac >= 0.5 ? 1 : 0;
            else
            {
                var goal = b.Type == DareType.Cant && int.TryParse(b.What.Target, out var t) ? t : DuelScale(b.What.Kind);
                theirs = Math.Max(theirs, (int)Math.Floor(goal * row.Pace * frac));
            }
        }
        var climbed = b.Me == DareSide.From
            ? b with { FromCount = Math.Max(b.FromCount, mine), ToCount = theirs }
            : b with { ToCount = Math.Max(b.ToCount, mine), FromCount = theirs };
        return b.Camera
            ? climbed with { FromCameraSeen = FakeSeen(b, DareSide.From, at), ToCameraSeen = FakeSeen(b, DareSide.To, at) }
            : climbed;
    }

    /// <summary>The preview's camera share: drifts either side of the pass bar so the card line shows
    /// both colours. Null for a side that does not count (the sender of a "can't") and for the first
    /// seconds, the way the server sends null while nothing is counted. Display only: settling still
    /// reads the real meter.</summary>
    internal static int? FakeSeen(DareBet b, DareSide side, DateTimeOffset at)
    {
        if (!b.Camera || (b.Type == DareType.Cant && side == DareSide.From)) return null;
        var secs = (at - (b.AcceptedAt ?? b.At)).TotalSeconds;
        if (secs < 2) return null;
        var phase = side == DareSide.From ? 0 : 1.7;
        return Math.Clamp((int)Math.Round(DareGrammar.CameraPassPercent + 22 * Math.Sin(secs / 15 + phase)), 0, 100);
    }

    private bool MyCameraPass(DareBet b)
    {
        if (!b.Camera) return true;
        var r = _meter.Reading(b.Id);
        if (r is not { CountedSeconds: > 0 } x) return false;
        return x.SeenSeconds >= DareGrammar.CameraPassFraction * x.CountedSeconds;
    }

    private DareBet SettleEarly(DareBet b, DateTimeOffset now)
    {
        if (b.Type != DareType.Cant) return b;
        var need = b.What.Kind switch
        {
            DareKind.Session or DareKind.Dare => 1,
            _ => int.TryParse(b.What.Target, out var t) ? t : int.MaxValue,
        };
        var receiverPass = b.Me == DareSide.To ? MyCameraPass(b) : true;
        return b.ToCount >= need && receiverPass ? Settle(b, now, early: true) : b;
    }

    private DareBet Settle(DareBet b, DateTimeOffset now, bool early)
    {
        bool PassOf(DareSide s) => s == b.Me ? MyCameraPass(b) : true;
        DareSide? winner;
        if (b.Type == DareType.Cant)
        {
            var need = b.What.Kind is DareKind.Session or DareKind.Dare ? 1 : int.TryParse(b.What.Target, out var t) ? t : int.MaxValue;
            winner = b.ToCount >= need && PassOf(DareSide.To) ? DareSide.To : DareSide.From;
        }
        else
        {
            var from = PassOf(DareSide.From) ? b.FromCount : 0;
            var to = PassOf(DareSide.To) ? b.ToCount : 0;
            winner = from == to ? null : from > to ? DareSide.From : DareSide.To;
        }
        return b with
        {
            State = DareState.Settled,
            Winner = winner,
            SettledAt = now,
            FromCameraPass = b.Camera ? PassOf(DareSide.From) : null,
            ToCameraPass = b.Camera ? PassOf(DareSide.To) : null,
            Debt = winner == null ? null : new DareDebt(DareDebtState.Owed, false, false),
        };
    }

    // ---- plumbing ----

    private bool Banned(DateTimeOffset now) => _data.BannedUntil is { } u && u > now;

    private static bool IsOpen(DareBet b) =>
        b.State is DareState.Writing or DareState.Sent or DareState.Running
        || (b.State == DareState.Settled && b.Debt is { State: DareDebtState.Owed or DareDebtState.Serving or DareDebtState.Claimed });

    private int OpenCount() => _data.Rows.Count(r => IsOpen(r.Bet));

    private void Commit()
    {
        var now = _now();
        var me = new DaresMe(Banned(now) ? _data.BannedUntil : null, true, true, 0);
        var next = new DaresSnapshot(true, me, _data.Rows.Select(r => r.Bet).ToList());
        Save();
        _meter.Save();
        if (DaresSnapshotEquality.Same(Snapshot, next)) return;
        Snapshot = next;
        try { Changed?.Invoke(); }
        catch (Exception ex) { Log.Debug("Dares preview handler failed: {E}", ex.Message); }
    }

    private static Task<T> Done<T>(T v) => Task.FromResult(v);

    private Data Load()
    {
        try
        {
            if (_path != null && File.Exists(_path))
                return JsonConvert.DeserializeObject<Data>(File.ReadAllText(_path)) ?? new Data();
        }
        catch (Exception ex) { Log.Debug("Dares preview file unreadable: {E}", ex.Message); }
        return new Data();
    }

    private void Save()
    {
        if (_path == null) return;
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(_data, Formatting.Indented));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) { Log.Debug("Dares preview save failed: {E}", ex.Message); }
    }

    public void Dispose()
    {
        _held = true;
        if (_timer != null) { _timer.Stop(); _timer.Tick -= OnTick; }
        _timer = null;
        _keeper.Ended -= OnServeEnded;
        _meter.Save();
    }
}
