using System;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>One Lobby feed for the launcher and page, as in WPF App.Lobby.</summary>
internal static class LobbyHead
{
    private static LobbyPoll? _service;
    internal static LobbyPoll Service => _service ??= Create();

    private static LobbyPoll Create()
    {
        var remote = new RemoteLobbyApi();
        return new LobbyPoll(new FriendsHead.Timer(), () => FriendsHead.Identity()?.UnifiedId,
            () => FriendsHead.Service is FriendsService f && f.SnapshotAccount == FriendsHead.Identity()?.UnifiedId
                ? f.Snapshot.Friends : Array.Empty<Friend>(),
            () => new PbpLobbyApi().FetchAsync(), () => new GoonOpenTablesApi().FetchAsync(),
            () => remote.FetchAsync(), () =>
            {
                var url = LobbyWire.DefaultBaseUrl();
                return string.IsNullOrEmpty(url) ? System.Threading.Tasks.Task.FromResult(RaceOpenReply.Empty)
                    : new RaceOpenApi(identity: FriendsHead.Identity, baseUrl: url).FetchAsync();
            });
    }
}
