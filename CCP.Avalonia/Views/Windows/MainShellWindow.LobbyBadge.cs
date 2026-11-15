using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal LobbyPoll? LobbyBadgeForTest;
    private LobbyPoll? _badgeLobby;
    private IDisposable? _badgeLease;
    private bool _badgeClosed;

    // WPF MainWindow.Lobby.cs:31-71,98-104: one shared feed, watched only on screen.
    private void HookLobbyBadge()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) SyncLobbyBadge();
        };
        Closed += (_, _) =>
        {
            _badgeClosed = true;
            _badgeLease?.Dispose();
            _badgeLease = null;
            if (_badgeLobby != null) _badgeLobby.Changed -= OnLobbyBadgeChanged;
        };
        SyncLobbyBadge();
    }

    private void SyncLobbyBadge()
    {
        if (_badgeClosed) return;
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            if (_badgeLobby == null)
            {
                _badgeLobby = LobbyBadgeForTest ?? LobbyHead.Service;
                _badgeLobby.Changed += OnLobbyBadgeChanged;
            }
            _badgeLease ??= _badgeLobby.Watch();
            OnLobbyBadgeChanged(_badgeLobby.Snapshot);
        }
        else
        {
            _badgeLease?.Dispose();
            _badgeLease = null;
        }
    }

    private void OnLobbyBadgeChanged(LobbySnapshot snapshot) => NavBadges.Set(NavSections.Social, snapshot.OpenCount);
}
