using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
namespace ConditioningControlPanel.Avalonia.Platform;
/// <summary>One Lobby feed for the launcher chip and the Lobby page, as WPF App.Lobby.</summary>
internal static class LobbyHead
{
    private static LobbyPoll? _service;
    internal static LobbyPoll Service => _service ??= Create();
    /// <summary>The directory wire, shared by the feed and a Remote claim (its 409 shows on the next round).</summary>
    internal static readonly RemoteLobbyApi Remote = new();
    // Friends and Goon are read per account: a cached answer from the last account never reaches this one.
    private static LobbyPoll Create() => new(new FriendsHead.Timer(), () => FriendsHead.Identity() != null,
        () => FriendsHead.Service is FriendsService f && f.SnapshotAccount == FriendsHead.Identity()?.UnifiedId ? f.Snapshot.Friends : Array.Empty<Friend>(),
        () => new PbpLobbyApi().FetchAsync(), () => new GoonOpenTablesApi().FetchAsync(), () => Remote.FetchAsync(),
        () => LobbyWire.DefaultBaseUrl() is { Length: > 0 } url ? new RaceOpenApi(identity: FriendsHead.Identity, baseUrl: url).FetchAsync() : Task.FromResult(RaceOpenReply.Empty),
        () => FriendsHead.Identity()?.UnifiedId);
}
