using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal Func<LobbyService> GetLobby = LobbyHead.Get;
    private LobbyService? _badgeLobby;
    private IDisposable? _lobbyBadgeLease;

    // WPF MainWindow.Lobby.cs:38-68: visible, non-minimized panel owns a rail lease.
    private void HookLobbyBadge()
    {
        PropertyChanged += OnLobbyWindowPropertyChanged;
        Closed += (_, _) =>
        {
            PropertyChanged -= OnLobbyWindowPropertyChanged;
            _lobbyBadgeLease?.Dispose();
            _lobbyBadgeLease = null;
            if (_badgeLobby != null) _badgeLobby.Changed -= PaintLobbyBadge;
            _badgeLobby = null;
        };
    }

    private void OnLobbyWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) SyncLobbyBadgeLease();
    }

    private void SyncLobbyBadgeLease()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            if (_badgeLobby == null)
            {
                _badgeLobby = GetLobby();
                _badgeLobby.Changed += PaintLobbyBadge;
            }
            _lobbyBadgeLease ??= _badgeLobby.Watch();
            PaintLobbyBadge(_badgeLobby.Snapshot);
        }
        else
        {
            _lobbyBadgeLease?.Dispose();
            _lobbyBadgeLease = null;
        }
    }

    private void PaintLobbyBadge(LobbySnapshot snapshot) => NavBadges.Set(NavSections.Social, snapshot.OpenCount);
}
