using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services.Chaster;

/// <summary>The Locktober raffle on Circe's Tab. The "time CCP added" totals shown on the page are
/// this machine's own count (display only); the raffle's days and total are the ones the server
/// reads off the lock's Chaster history after a push lands and when the page opens
/// (<see cref="ChasterRaffle"/>). Every failure is quiet: no raffle is a page with no card.
///
/// <para>Only added time counts (owner, 2026-09-29): every booking also lands in the tab's day
/// ledger (<see cref="ChasterLadder.NoteBooked"/>), and the verify carries it
/// (<see cref="ChasterLadder.Claims"/>), so a credit that cancelled part of a slip-up before the
/// push does not take that part out of the raffle.</para></summary>
public sealed partial class ChasterService
{
    private readonly SemaphoreSlim _ladderGate = new(1, 1);
    private readonly IChasterLadderApi? _ladderApi;
    private DateTime? _ladderVerifiedAtUtc;
    private Timer? _ladderRetry;
    private int _raffleAsked;

    /// <summary>Whether the server's raffle month is this UTC month (<see cref="CircesTab.Book"/>'s
    /// raffle day rule). Not known yet this month: ask once per run, quietly, so the next slip-up
    /// knows. Call inside the tab lock.</summary>
    private bool RaffleRunning(DateTime nowUtc)
    {
        if (LadderApi == null) return false;
        if (_tab.RaffleMonth == ChasterLadder.MonthKey(nowUtc)) return true;
        if (IsLinked && Interlocked.Exchange(ref _raffleAsked, 1) == 0)
            _ = Task.Run(async () =>
            {
                try
                {
                    var epoch = RaffleEpoch;
                    var card = await LadderApi.MeAsync().ConfigureAwait(false);
                    NoteRaffleMonth(card);
                    NoteRaffleCard(card, epoch);
                }
                catch (Exception ex) { Diag.Swallowed(ex, "raffle month read skipped"); }
            });
        return false;
    }

    /// <summary>Remember the raffle month while the server says it is running.</summary>
    private void NoteRaffleMonth(RaffleCard? card)
    {
        if (card == null || card.Today < 1 || card.Today > card.DaysInMonth) return;
        lock (_gate)
        {
            if (_tab.RaffleMonth == card.Month) return;
            _tab.RaffleMonth = card.Month;
            SaveTab();
        }
    }

    /// <summary>Null = no ladder (tests). Setting one also starts the raffle's day ledger: every
    /// booking's gross goes into it as it is raised (<see cref="Booked"/>).</summary>
    public IChasterLadderApi? LadderApi
    {
        get => _ladderApi;
        init
        {
            _ladderApi = value;
            if (value != null) Booked += NoteLadderBooking;
        }
    }

    // Booked is raised outside the tab lock, on whatever thread booked. Not saved here: the push the
    // booking arms saves the tab within half a minute, and a line lost to a crash only ever claims less.
    private void NoteLadderBooking(string eventId, TabBooking booking)
    {
        if (booking.AppliedSeconds == 0) return;
        try { lock (_gate) ChasterLadder.NoteBooked(_tab, booking.AppliedSeconds, _utcNow()); }
        catch (Exception ex) { Diag.Swallowed(ex, "ladder ledger skipped a booking"); }
    }

    /// <summary>The player's "post my days in Discord" switch, read at each refresh.</summary>
    public Func<bool>? RafflePostDays { get; init; }

    /// <summary>The player's "show my name on the ladder" switch, read at each board read.</summary>
    public Func<bool>? LadderShowName { get; init; }

    /// <summary>What the server last read off Chaster for this month, or null when it has not.</summary>
    public LadderVerify? LastLadderVerify { get; private set; }

    /// <summary>The last raffle card the server sent (its real counted days), or null. The
    /// billboard and the tab page both read their counted days from this one card.</summary>
    public RaffleCard? LastRaffleCard { get; private set; }

    /// <summary>The raffle card changed (a new one, or a stale one dropped). Raised on whatever
    /// thread read it.</summary>
    public event Action? RaffleChanged;

    /// <summary>Which CCP account the raffle reads belong to: bumped on every account change. A
    /// read that started under an older one is dropped when it lands.</summary>
    public int RaffleEpoch => Volatile.Read(ref _raffleEpoch);
    private int _raffleEpoch;
    private int _watchingIdentity;

    /// <summary>Keep a card the server sent, unless the account changed since the read began.
    /// <paramref name="dropOnNull"/>: no card drops the old one (a fresh verify with no card to
    /// follow it, so the count it just stored wins; the page's own read).</summary>
    private void NoteRaffleCard(RaffleCard? card, int epoch, bool dropOnNull = false)
    {
        if (epoch != RaffleEpoch) return;
        if (card == null && (!dropOnNull || LastRaffleCard == null)) return;
        LastRaffleCard = card;
        RaiseRaffleChanged();
    }

    private void RaiseRaffleChanged()
    {
        try { RaffleChanged?.Invoke(); }
        catch (Exception ex) { Diag.Swallowed(ex, "raffle changed handler"); }
    }

    /// <summary>The raffle card and the verify are the CCP account's (<c>/chaster/raffle/me</c>
    /// signs with it), while the Chaster link is this PC's and outlives a sign-out. The app's
    /// service follows the account, so a sign-out or a switch never shows the last one's days.
    /// Not in the constructor: a test's service must not hear another test's account change.</summary>
    internal void WatchIdentity()
    {
        if (Interlocked.Exchange(ref _watchingIdentity, 1) == 0)
            App.UnifiedIdentityChanged += OnIdentityChanged;
    }

    private void OnIdentityChanged(object? sender, EventArgs e) => ForgetRaffle();

    /// <summary>Another account, or none: drop the card and the verify, ask the server again,
    /// and let the next verify run at once. Raised so the billboard and the page let go too.</summary>
    internal void ForgetRaffle()
    {
        Interlocked.Increment(ref _raffleEpoch);
        LastRaffleCard = null;
        LastLadderVerify = null;
        _ladderVerifiedAtUtc = null;
        Interlocked.Exchange(ref _raffleAsked, 0);
        RaiseRaffleChanged();
    }

    /// <summary>A verify that read the lock afresh is followed by the card, so the days a
    /// surface lights are the ones that verify just stored. Only while the raffle month runs.</summary>
    private async Task VerifyThenReadAsync()
    {
        try
        {
            var epoch = RaffleEpoch;
            if (!await VerifyLadderAsync(CancellationToken.None).ConfigureAwait(false) || LadderApi == null) return;
            bool running;
            lock (_gate) running = _tab.RaffleMonth == ChasterLadder.MonthKey(_utcNow());
            if (running) NoteRaffleCard(await LadderApi.MeAsync().ConfigureAwait(false), epoch, dropOnNull: true);
        }
        catch (Exception ex) { Diag.Swallowed(ex, "raffle card read skipped"); }
    }

    /// <summary>Lifetime seconds CCP added to the lock. Added only: nothing taken off counts.</summary>
    public long AddedLifetimeSeconds { get { lock (_gate) return ChasterLadder.Lifetime(_tab); } }

    /// <summary>This UTC month's adds, as this machine counted them.</summary>
    public int AddedThisMonthSeconds { get { lock (_gate) return ChasterLadder.ThisMonth(_tab, _utcNow()); } }

    private void LadderPushLanded() => _ = VerifyThenReadAsync();

    /// <summary>Have the server read this month off the chosen lock, at most every 15 minutes.
    /// Only while the link is live and a lock is picked. True when a fresh Ok reading landed.</summary>
    public async Task<bool> VerifyLadderAsync(CancellationToken ct)
    {
        if (LadderApi == null || !IsLinked) return false;
        var lockId = (_options() ?? ChasterOptions.Off).LockId;
        if (!ChasterClient.IsLockId(lockId)) return false;
        await _ladderGate.WaitAsync(ct).ConfigureAwait(false);
        var epoch = RaffleEpoch;
        try
        {
            var now = _utcNow();
            if (!ChasterLadder.VerifyDue(_ladderVerifiedAtUtc, now))
            {
                ArmLadderRetry(ChasterLadder.VerifyAt(_ladderVerifiedAtUtc, now), now);
                return false;
            }
            var access = await AccessTokenAsync(ct).ConfigureAwait(false);
            if (access == null) return false;
            _ladderVerifiedAtUtc = now;
            IReadOnlyDictionary<string, LadderClaim> claims;
            lock (_gate) claims = ChasterLadder.Claims(_tab, _utcNow());
            var verdict = await LadderApi.VerifyAsync(lockId!, access, claims, ct).ConfigureAwait(false);
            if (epoch != RaffleEpoch)
            {
                // The account changed mid-read: the reading was the last one's, and the new
                // one's first verify is not held back by it.
                _ladderVerifiedAtUtc = null;
                return false;
            }
            if (verdict != null) LastLadderVerify = verdict;
            return verdict is { Ok: true };
        }
        catch (Exception ex) { Diag.Swallowed(ex, "ladder verify skipped"); return false; }
        finally { _ladderGate.Release(); }
    }

    /// <summary>The player's raffle card for the page, after a due verify. The server's copy of
    /// the "post my days" switch is brought back in line with the player's when they differ.
    /// No card drops the kept one: the page and the billboard show the same answer. Null when
    /// the account changed while it read.</summary>
    public async Task<RaffleCard?> RaffleAsync(CancellationToken ct = default)
    {
        if (LadderApi == null) return null;
        var epoch = RaffleEpoch;
        await VerifyLadderAsync(ct).ConfigureAwait(false);
        var card = await LadderApi.MeAsync(ct).ConfigureAwait(false);
        NoteRaffleMonth(card);
        var want = RafflePostDays?.Invoke() == true;
        if (card != null && card.PostDays != want && await LadderApi.OptInAsync(want, ct).ConfigureAwait(false))
            card = card with { PostDays = want };
        if (epoch != RaffleEpoch) return null;
        NoteRaffleCard(card, epoch, dropOnNull: true);
        return card;
    }

    /// <summary>The month's top ten for the pinned scrap. The raffle read verifies first, so this
    /// one does not. The server's copy of the name switch is brought back in line with the
    /// player's when they differ.</summary>
    public async Task<LadderBoard?> LadderAsync(CancellationToken ct = default)
    {
        if (LadderApi == null) return null;
        var board = await LadderApi.TopAsync(ct).ConfigureAwait(false);
        var want = LadderShowName?.Invoke() == true;
        if (board != null && board.ShowName != want && await LadderApi.ShowNameAsync(want, ct).ConfigureAwait(false))
            board = await LadderApi.TopAsync(ct).ConfigureAwait(false) ?? board;
        return board;
    }

    /// <summary>The "show my name" toggle. True when the server took it.</summary>
    public Task<bool> SetLadderShowNameAsync(bool show, CancellationToken ct = default) =>
        LadderApi == null ? Task.FromResult(false) : LadderApi.ShowNameAsync(show, ct);

    /// <summary>The page's toggle. True when the server took it.</summary>
    public Task<bool> SetRafflePostDaysAsync(bool post, CancellationToken ct = default) =>
        LadderApi == null ? Task.FromResult(false) : LadderApi.OptInAsync(post, ct);

    // A push that landed inside the 15 minutes is verified when they are up, not dropped.
    private void ArmLadderRetry(DateTime? at, DateTime now)
    {
        if (at is not { } when) return;
        var delay = when - now;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        var timer = new Timer(_ => _ = VerifyThenReadAsync(), null, delay + TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        Interlocked.Exchange(ref _ladderRetry, timer)?.Dispose();
    }

    private void DisposeLadder()
    {
        if (Interlocked.Exchange(ref _watchingIdentity, 0) == 1)
            App.UnifiedIdentityChanged -= OnIdentityChanged;
        Interlocked.Exchange(ref _ladderRetry, null)?.Dispose();
    }
}
