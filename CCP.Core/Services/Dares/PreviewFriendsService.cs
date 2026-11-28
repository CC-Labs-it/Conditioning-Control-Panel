using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>
/// THE DARES PREVIEW'S FRIENDS (DEBUG <c>CCP_DARES_PREVIEW=1</c> only, wired in App.xaml.cs and App.axaml.cs). Stands in
/// for <c>App.Friends</c> so the drawer has people to dare in a clean sandbox: while the real list is
/// empty or nobody is signed in, the snapshot holds the three fake friends of
/// <see cref="PreviewDaresService.Fakes"/>; once the real list has anyone, it is the real one, untouched.
/// A press on a fake friend answers locally and sends nothing; everything else goes to the real service.
/// </summary>
public sealed class PreviewFriendsService : IFriendsService, IDisposable
{
    /// <summary>App exit stops the real poll underneath.</summary>
    public void Dispose() => (_inner as IDisposable)?.Dispose();

    private readonly IFriendsService _inner;

    public PreviewFriendsService(IFriendsService inner)
    {
        _inner = inner;
        _inner.SnapshotChanged += _ => Raise();
        _inner.Delivered += i => Delivered?.Invoke(i);
        _inner.Sent += (k, f) => Sent?.Invoke(k, f);
        _inner.RequestArrived += r => RequestArrived?.Invoke(r);
        _inner.RequestGone += id => RequestGone?.Invoke(id);
    }

    /// <summary>The fake friends as drawer rows: online, in the panel.</summary>
    public static IReadOnlyList<Friend> FakeRows(DateTimeOffset now) => PreviewDaresService.Fakes
        .Select(f => new Friend(f.Id, f.Name, null, f.Tier, true, new FriendPresence(PresenceActivity.Panel, null, now), false)
        {
            Platforms = FriendPlatform.Normalise(new[] { FriendPlatform.Desktop }),
        })
        .ToList();

    /// <summary>True while the stand-in shows the fakes instead of the real list.</summary>
    public bool ShowingFakes => !_inner.Available || _inner.Snapshot.Friends.Count == 0;

    public static bool IsFake(string? id) => PreviewDaresService.Fakes.Any(f => f.Id == id);

    public bool Available => true;

    public FriendsSnapshot Snapshot
    {
        get
        {
            if (!ShowingFakes) return _inner.Snapshot;
            var real = _inner.Available ? _inner.Snapshot : FriendsSnapshot.Empty;
            return real with { Friends = _fakes ??= FakeRows(DateTimeOffset.UtcNow), MyCode = string.IsNullOrEmpty(real.MyCode) ? "PREVIEW" : real.MyCode };
        }
    }

    private IReadOnlyList<Friend>? _fakes;

    public bool PresenceShared { get => _inner.PresenceShared; set => _inner.PresenceShared = value; }

    public event Action<FriendsSnapshot>? SnapshotChanged;
    public event Action<InboxItem>? Delivered;
    public event Action<SendKind, Friend>? Sent;
    public event Action<FriendRequest>? RequestArrived;
    public event Action<string>? RequestGone;

    private void Raise()
    {
        try { SnapshotChanged?.Invoke(Snapshot); }
        catch (Exception ex) { Serilog.Log.Debug("Preview friends handler failed: {E}", ex.Message); }
    }

    public async Task RefreshAsync()
    {
        if (_inner.Available) await _inner.RefreshAsync();
        Raise();
    }

    private Friend? Fake(string id) => IsFake(id) ? Snapshot.Friends.FirstOrDefault(f => f.Id == id) : null;

    private Task<SendResult> FakeSend(string id, SendKind kind)
    {
        if (Fake(id) is { } f) Sent?.Invoke(kind, f);
        return Task.FromResult(SendResult.Sent);
    }

    public Task<SendResult> PokeAsync(string friendId, string pokeId) =>
        IsFake(friendId) ? FakeSend(friendId, SendKind.Poke) : _inner.PokeAsync(friendId, pokeId);

    public Task<SendResult> InviteAsync(string friendId, string destination, string? code) =>
        IsFake(friendId) ? FakeSend(friendId, SendKind.Invite) : _inner.InviteAsync(friendId, destination, code);

    public Task<SendResult> SendWatchAsync(string friendId, WatchRef watch) =>
        IsFake(friendId) ? FakeSend(friendId, SendKind.Watch) : _inner.SendWatchAsync(friendId, watch);

    public Task<SendOutcome> InviteToRaceAsync(string friendId, string roomId) =>
        IsFake(friendId) ? Task.FromResult(new SendOutcome(SendResult.Sent, null)) : _inner.InviteToRaceAsync(friendId, roomId);

    public Task<AddResult> AddByCodeAsync(string code) => _inner.AddByCodeAsync(code);
    public Task<FriendLink?> GetLinkAsync() => _inner.GetLinkAsync();
    public Task<FriendLink?> NewLinkAsync() => _inner.NewLinkAsync();
    public Task<ClaimResult> ClaimLinkAsync(string linkOrToken) => _inner.ClaimLinkAsync(linkOrToken);

    private Task<ActResult> Act(string id, Func<Task<ActResult>> real) =>
        IsFake(id) ? Task.FromResult(ActResult.Done) : real();

    public Task<ActResult> AcceptAsync(string requesterId) => Act(requesterId, () => _inner.AcceptAsync(requesterId));
    public Task<ActResult> DeclineAsync(string requesterId) => Act(requesterId, () => _inner.DeclineAsync(requesterId));
    public Task<ActResult> CancelRequestAsync(string targetId) => Act(targetId, () => _inner.CancelRequestAsync(targetId));
    public Task<ActResult> RemoveAsync(string friendId) => Act(friendId, () => _inner.RemoveAsync(friendId));
    public Task<ActResult> BlockAsync(string friendId) => Act(friendId, () => _inner.BlockAsync(friendId));
    public Task<ActResult> UnblockAsync(string friendId) => Act(friendId, () => _inner.UnblockAsync(friendId));
    public Task<ActResult> SetSquelchAsync(string friendId, bool on) => Act(friendId, () => _inner.SetSquelchAsync(friendId, on));
    public Task<ActResult> ReportAsync(string friendId, string reason) => Act(friendId, () => _inner.ReportAsync(friendId, reason));

    public void SetActivity(PresenceActivity activity) => _inner.SetActivity(activity);
    public void EnterActivity(PresenceActivity activity) => _inner.EnterActivity(activity);
    public void LeaveActivity(PresenceActivity activity) => _inner.LeaveActivity(activity);
    public void SetDrawerOpen(bool open) => _inner.SetDrawerOpen(open);
    public SentTrail? LastSentTo(string friendId) => IsFake(friendId) ? null : _inner.LastSentTo(friendId);
    public void ReportReceipt(ReceiptReport report) => _inner.ReportReceipt(report);

    public event Action? SentTrailsChanged { add => _inner.SentTrailsChanged += value; remove => _inner.SentTrailsChanged -= value; }
    public event Action<IReadOnlyList<SenderReceipt>>? ReceiptsArrived { add => _inner.ReceiptsArrived += value; remove => _inner.ReceiptsArrived -= value; }
    public event Action<FriendEvent>? Happened { add => _inner.Happened += value; remove => _inner.Happened -= value; }
}
