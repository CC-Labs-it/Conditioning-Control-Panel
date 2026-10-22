using System;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF App.Lobby + the LobbyWire seed (App.xaml.cs:467, :3114). The wire rides the friends'
/// proxy and sandbox rule (<see cref="FriendsHead.BaseUrl"/>): a sandbox without a loopback url asks
/// nobody, so every round comes back empty.</summary>
internal static class LobbyHead
{
    /// <summary>WPF App.Lobby. Null until startup built it.</summary>
    internal static LobbyPoller? Service { get; set; }

    internal static void Start(string? userDataDir, string? overrideUrl)
    {
        var url = FriendsHead.BaseUrl(userDataDir, overrideUrl);
        LobbyWire.DefaultIdentity = FriendsHead.Identity;
        LobbyWire.DefaultBaseUrl = () => url;
        Service ??= new LobbyPoller(() => new FriendsHead.Timer())
        {
            ReadFriends = () => FriendsHead.Service?.Snapshot.Friends ?? Array.Empty<Friend>(),
        };
    }
}
