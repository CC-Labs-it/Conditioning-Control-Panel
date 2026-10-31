using System;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF App.xaml.cs:1272,3272: one app-owned service shared by all Lobby consumers.
internal static class LobbyHead
{
    private static LobbyService? _service;
    internal static LobbyService Get() => _service ??= Create();

    private static LobbyService Create()
    {
        var remote = new RemoteLobbyApi();
        return new LobbyService(new FriendsHead.Timer(), () => FriendsHead.Identity() != null,
            () => FriendsHead.Service?.Snapshot.Friends ?? Array.Empty<Friend>(),
            () => new PbpLobbyApi().FetchAsync(), () => GoonOpenTables.RefreshAsync(), () => remote.FetchAsync());
    }

    internal static void Stop()
    {
        _service?.Dispose();
        _service = null;
    }
}
