// WPF MainWindow.Lobby.cs:28-103, MainWindow.TabNavigation.cs:340-341,609-615.
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal LobbyService Lobby { get; } = new();
    private IDisposable? _lobbyLease;
    private IDisposable? _lobbyBadgeLease;

    private void InitializeLobby()
    {
        Lobby.Changed += OnLobbyChanged;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) SyncLobbyBadge();
        };
        Closed += (_, _) =>
        {
            _lobbyLease?.Dispose();
            _lobbyBadgeLease?.Dispose();
            Lobby.Changed -= OnLobbyChanged;
            Lobby.Dispose();
        };
        SyncLobbyBadge();
    }

    private void SyncLobbyBadge()
    {
        if (Lobby.BaseUrl() != null && IsVisible && WindowState != WindowState.Minimized)
        {
            _lobbyBadgeLease ??= Lobby.Watch();
            NavBadges.Set(NavSections.Social, Lobby.Snapshot.OpenCount);
        }
        else { _lobbyBadgeLease?.Dispose(); _lobbyBadgeLease = null; }
    }

    private void LeaveLobbyTab()
    {
        Lobby.CancelDirectory();
        _lobbyLease?.Dispose();
        _lobbyLease = null;
    }

    private void EnterLobbyTab()
    {
        if (Lobby.BaseUrl() == null) return; // the app has not configured a wire in this sandbox
        PaintLobby(Lobby.Snapshot);
        _lobbyLease ??= Lobby.Watch();
    }

    private void OnLobbyChanged(LobbySnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OnLobbyChanged(snapshot)); return; }
        NavBadges.Set(NavSections.Social, snapshot.OpenCount);
        PaintLobby(snapshot);
    }

    private void PaintLobby(LobbySnapshot snapshot)
    {
        var page = Named<Tabs.AvailableSubjectsTabView>("AvailableSubjectsTab");
        if (page != null) page.PaintSnapshot(snapshot, page.ReadGates(), snapshot.SignedIn && !Lobby.LastRoundOk);
    }
}
