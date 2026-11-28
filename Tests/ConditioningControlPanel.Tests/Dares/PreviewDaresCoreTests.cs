using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Dares;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>The local preview: a whole bet in minutes, its walls, and the file that brings it back.</summary>
public class PreviewDaresCoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dares-preview-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private sealed class Host : IDareHost
    {
        public bool Active;
        public TimeSpan Started;
        public string? Account() => "preview";
        public int TzMinutes() => 0;
        public bool ChasterReady() => throw new InvalidOperationException("the preview never books");
        public int BookLockSeconds(int seconds) => throw new InvalidOperationException("the preview never books");
        public void AdoptSp(string account, int sp) => throw new InvalidOperationException("the preview never touches the wallet");
        public bool LockdownActive => Active;
        public bool StartLockdown(TimeSpan duration) { Active = true; Started = duration; return true; }
        public event Action<TimeSpan, TimeSpan>? LockdownEnded;
        public void End(TimeSpan ran) { Active = false; LockdownEnded?.Invoke(Started, ran); }
    }

    private readonly Host _host = new();

    /// <summary>pace above 1: the fake friend makes its target; below 1: it misses.</summary>
    private PreviewDaresService Make(bool sub = true, double pace = 1.2) => new(
        Path.Combine(_dir, PreviewDaresService.FileName),
        new DareMeter(new DareMeter.FileStore(Path.Combine(_dir, "meter.json"))),
        new DareLockdownKeeper(_host, new DareLockdownKeeper.FileStore(Path.Combine(_dir, "serve.json"))),
        () => _now, new Random(1), () => sub, pace: pace);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static DareSlip Slip(DareStake mine, DareStake theirs, DareKind kind = DareKind.Pics, string? target = "100") =>
        new(DareType.Cant, new DareWhat(kind, target), DareDeadline.ThreeHours, mine, theirs, false);

    [Fact]
    public void Release_never_enables_it_and_the_debug_switch_is_exactly_1()
    {
#if DEBUG
        Assert.True(PreviewDaresService.EnabledFor("1"));
#else
        Assert.False(PreviewDaresService.EnabledFor("1"));
#endif
        Assert.False(PreviewDaresService.EnabledFor("yes"));
        Assert.False(PreviewDaresService.EnabledFor(null));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Theory]
    [InlineData("SkillPoints")]
    [InlineData("HttpClient")]
    [InlineData("DaresApi")]
    [InlineData("V2WalletAdoption")]
    [InlineData("SparklesLedger")]
    [InlineData("NoteSeconds")]
    public void The_preview_never_touches_the_real_wallet_the_tab_or_the_network(string banned)
    {
        foreach (var name in new[] { "PreviewDaresService.cs", "PreviewFriendsService.cs" })
            Assert.DoesNotContain(banned, File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "Dares", name)));
    }

    [Theory]
    [InlineData("ConditioningControlPanel/App.xaml.cs", 3)]
    [InlineData("CCP.Avalonia/App.axaml.cs", 2)]
    public void App_reaches_the_preview_only_inside_if_DEBUG(string app, int least)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), app));
        var depth = 0;
        var stack = new System.Collections.Generic.Stack<bool>();
        var callers = 0;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("#if")) { var dbg = line == "#if DEBUG"; stack.Push(dbg); if (dbg) depth++; continue; }
            if (line.StartsWith("#else") || line.StartsWith("#elif")) { if (stack.Count > 0 && stack.Peek()) { depth--; stack.Pop(); stack.Push(false); } continue; }
            if (line.StartsWith("#endif")) { if (stack.Count > 0 && stack.Pop()) depth--; continue; }
            if (line.StartsWith("//") || !(line.Contains("PreviewDaresService.") || line.Contains("new Services.Dares.PreviewFriendsService"))) continue;
            callers++;
            Assert.True(depth > 0, app + " reaches the dares preview outside #if DEBUG: " + line);
        }
        Assert.True(callers >= least);
    }

    [Fact]
    public async Task A_sent_bet_is_accepted_runs_and_settles_now()
    {
        var p = Make();
        var o = await p.CreateAsync(PreviewDaresService.Fakes[0].Id, Slip(new DareStake(DareStakeKind.Sp, 100), new DareStake(DareStakeKind.Sp, 50)));
        Assert.Equal(DareCreateStatus.Sent, o.Status);
        Assert.True(p.IsPreview);
        _now += TimeSpan.FromSeconds(5);
        p.Step();
        var b = p.Snapshot.Bets.Single();
        Assert.Equal(DareState.Running, b.State);
        Assert.Equal(_now + TimeSpan.FromMinutes(3), b.EndsAt);

        _now += TimeSpan.FromMinutes(1);
        await p.SettleNowAsync(b.Id);
        b = p.Snapshot.Bets.Single();
        Assert.Equal(DareState.Settled, b.State);
        Assert.NotNull(b.Winner);
        Assert.NotNull(b.Debt);
    }

    [Theory]
    [InlineData(1.2, -200)]   // the friend makes it: I owe my 200
    [InlineData(0.7, 50)]     // the friend misses: they owe their 50, paid after a moment
    public async Task Sparkles_move_only_in_the_preview_wallet(double pace, int moved)
    {
        var p = Make(pace: pace);
        var start = p.PreviewSp;
        await p.CreateAsync(PreviewDaresService.Fakes[1].Id, Slip(new DareStake(DareStakeKind.Sp, 200), new DareStake(DareStakeKind.Sp, 50)));
        _now += TimeSpan.FromSeconds(5);
        p.Step();
        var id = p.Snapshot.Bets.Single().Id;
        _now += TimeSpan.FromMinutes(4);
        p.Step();
        var b = p.Snapshot.Bets.Single(x => x.Id == id);
        Assert.Equal(DareState.Settled, b.State);
        if (moved < 0)
        {
            Assert.True(b.IOwe);
            Assert.Equal(DareActResult.Done, await p.ActAsync(id, DareOp.Pay));
        }
        else
        {
            Assert.True(b.TheyOwe);
            _now += TimeSpan.FromSeconds(10);
            p.Step();
        }
        Assert.Equal(start + moved, p.PreviewSp);
    }

    [Fact]
    public async Task A_lockdown_stake_needs_a_subscriber_on_the_owing_side()
    {
        var tierZero = PreviewDaresService.Fakes.Single(f => f.Tier == 0);
        var p = Make();
        var o = await p.CreateAsync(tierZero.Id, Slip(new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Lockdown, 10)));
        Assert.Equal(DareCreateStatus.LockdownNeedsSub, o.Status);

        var free = Make(sub: false);
        var mine = await free.CreateAsync(PreviewDaresService.Fakes[0].Id, Slip(new DareStake(DareStakeKind.Lockdown, 10), new DareStake(DareStakeKind.Sp, 50)));
        Assert.Equal(DareCreateStatus.LockdownNeedsSub, mine.Status);
    }

    [Fact]
    public async Task A_lockdown_debt_runs_the_real_lockdown_with_one_retry_and_survives_a_restart()
    {
        var p = Make();
        // Me = sender, my Lockdown on the line, and the friend makes it.
        await p.CreateAsync(PreviewDaresService.Fakes[0].Id, Slip(new DareStake(DareStakeKind.Lockdown, 10), new DareStake(DareStakeKind.Sp, 50), DareKind.Session, "easy"));
        _now += TimeSpan.FromSeconds(5);
        p.Step();
        var id = p.Snapshot.Bets.Single().Id;
        _now += TimeSpan.FromMinutes(10);
        p.Step();
        var b = p.Snapshot.Bets.Single(x => x.Id == id);
        Assert.True(b.IOwe);

        // A restart keeps the debt card.
        var again = Make();
        Assert.True(again.Snapshot.Bets.Single(x => x.Id == id).IOwe);

        Assert.Equal(DareActResult.Done, await again.ActAsync(id, DareOp.StartLockdown));
        Assert.Equal(TimeSpan.FromMinutes(10), _host.Started);
        _host.End(TimeSpan.FromMinutes(2));
        b = again.Snapshot.Bets.Single(x => x.Id == id);
        Assert.Equal(DareDebtState.Owed, b.Debt!.State);
        Assert.True(b.Debt.RetryUsed);

        Assert.Equal(DareActResult.Done, await again.ActAsync(id, DareOp.StartLockdown));
        _host.End(TimeSpan.FromMinutes(10));
        Assert.Equal(DareDebtState.Paid, again.Snapshot.Bets.Single(x => x.Id == id).Debt!.State);
    }

    [Fact]
    public async Task Renounce_bans_new_bets_for_a_while()
    {
        var p = Make();
        await p.CreateAsync(PreviewDaresService.Fakes[0].Id, Slip(new DareStake(DareStakeKind.Lock, 10), new DareStake(DareStakeKind.Lock, 10), DareKind.Session, "easy"));
        _now += TimeSpan.FromSeconds(5);
        p.Step();
        var id = p.Snapshot.Bets.Single().Id;
        _now += TimeSpan.FromMinutes(10);
        p.Step();
        Assert.True(p.Snapshot.Bets.Single(x => x.Id == id).IOwe);
        Assert.Equal(DareActResult.Done, await p.ActAsync(id, DareOp.Renounce));
        Assert.NotNull(p.Snapshot.Me.BannedUntil);
        var o = await p.CreateAsync(PreviewDaresService.Fakes[1].Id, Slip(new DareStake(DareStakeKind.Sp, 50), new DareStake(DareStakeKind.Sp, 50)));
        Assert.Equal(DareCreateStatus.Banned, o.Status);
    }

    [Fact]
    public async Task A_friend_offer_arrives_and_waits_on_me()
    {
        var p = Make();
        _now += PreviewDaresService.OfferAfter + TimeSpan.FromSeconds(1);
        p.Step();
        var offer = p.Snapshot.Bets.Single();
        Assert.True(offer.IsOfferToMe);
        _now += TimeSpan.FromSeconds(30);
        p.Step();
        Assert.True(p.Snapshot.Bets.Single().IsOfferToMe);   // never accepts itself on my side
        Assert.Equal(DareActResult.Done, await p.ActAsync(offer.Id, DareOp.Accept));
        Assert.Equal(DareState.Running, p.Snapshot.Bets.Single().State);
    }

    [Fact]
    public void Dare_slips_wait_for_their_words_then_go_out()
    {
        var p = Make();
        var o = p.CreateAsync(PreviewDaresService.Fakes[0].Id, Slip(new DareStake(DareStakeKind.Dare), new DareStake(DareStakeKind.Sp, 50))).Result;
        Assert.Equal(DareCreateStatus.Writing, o.Status);
        Assert.Equal(6, o.WriteCode!.Length);
        _now += PreviewDaresService.FriendActsAfter + TimeSpan.FromSeconds(1);
        p.Step();
        Assert.Equal(DareState.Sent, p.Snapshot.Bets.Single().State);
    }

    [Fact]
    public void The_stand_in_shows_fakes_only_while_the_real_list_is_empty()
    {
        var empty = new PreviewFriendsService(new StubFriends(FriendsSnapshot.Empty, available: false));
        Assert.True(empty.Available);
        Assert.Equal(3, empty.Snapshot.Friends.Count);
        Assert.Contains(empty.Snapshot.Friends, f => f.Tier == 0);
        Assert.Equal(SendResult.Sent, empty.PokeAsync(PreviewDaresService.Fakes[0].Id, "hi").Result);

        var real = FriendsSnapshot.Empty with
        {
            Friends = new[] { new Friend("u_real", "Real", null, 1, true, FriendPresence.None, false) },
        };
        var full = new PreviewFriendsService(new StubFriends(real, available: true));
        Assert.Equal("u_real", full.Snapshot.Friends.Single().Id);
    }

    private sealed class StubFriends : IFriendsService
    {
        public StubFriends(FriendsSnapshot s, bool available) { Snapshot = s; Available = available; }
        public bool Available { get; }
        public FriendsSnapshot Snapshot { get; }
        public bool PresenceShared { get; set; }
#pragma warning disable CS0067
        public event Action<FriendsSnapshot>? SnapshotChanged;
        public event Action<InboxItem>? Delivered;
        public event Action<SendKind, Friend>? Sent;
        public event Action<FriendRequest>? RequestArrived;
        public event Action<string>? RequestGone;
#pragma warning restore CS0067
        public Task RefreshAsync() => Task.CompletedTask;
        public Task<SendResult> PokeAsync(string friendId, string pokeId) => throw new InvalidOperationException("a fake friend never reaches the wire");
        public Task<SendResult> InviteAsync(string friendId, string destination, string? code) => Task.FromResult(SendResult.TryLater);
        public Task<SendResult> SendWatchAsync(string friendId, WatchRef watch) => Task.FromResult(SendResult.TryLater);
        public Task<AddResult> AddByCodeAsync(string code) => Task.FromResult(AddResult.TryLater);
        public Task<ActResult> AcceptAsync(string requesterId) => Task.FromResult(ActResult.TryLater);
        public Task<ActResult> DeclineAsync(string requesterId) => Task.FromResult(ActResult.TryLater);
        public Task<ActResult> CancelRequestAsync(string targetId) => Task.FromResult(ActResult.TryLater);
        public Task<ActResult> RemoveAsync(string friendId) => Task.FromResult(ActResult.TryLater);
        public Task<ActResult> BlockAsync(string friendId) => Task.FromResult(ActResult.TryLater);
        public Task<ActResult> UnblockAsync(string friendId) => Task.FromResult(ActResult.TryLater);
        public Task<ActResult> SetSquelchAsync(string friendId, bool on) => Task.FromResult(ActResult.TryLater);
        public Task<ActResult> ReportAsync(string friendId, string reason) => Task.FromResult(ActResult.TryLater);
        public void SetActivity(PresenceActivity activity) { }
        public void SetDrawerOpen(bool open) { }
    }
}
