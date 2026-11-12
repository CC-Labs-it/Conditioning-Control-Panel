using System;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    internal Func<LobbyPoll> CreateLobby = AvailableSubjectsTabView.BuildLobby;
    private LobbyPoll? _lobby;
    private DispatcherTimer? _lobbyTimer;

    private void StartLobby()
    {
        if (_lobby == null)
        {
            _lobby = CreateLobby();
            _lobby.Changed += PaintLobby;
        }
        PaintLobby(_lobby.Snapshot);
        _ = _lobby.RefreshAsync();
        _lobbyTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _lobbyTimer.Tick -= LobbyTick;
        _lobbyTimer.Tick += LobbyTick;
        _lobbyTimer.Start();
    }

    private void LobbyTick(object? sender, EventArgs e) => _ = _lobby?.RefreshAsync();
    private void StopLobby() => _lobbyTimer?.Stop();
    private void PaintLobby(LobbySnapshot snapshot) => LobbyChipText.Text = snapshot.OpenCount > 0
        ? Loc.GetF("launcher_lobby_open", snapshot.OpenCount) : Loc.Get("launcher_lobby");
    private void LobbyChip_Click(object? sender, RoutedEventArgs e)
    {
        if (!MainShellWindow.LockdownActive) OpenPanel(p => p.ShowTab("availablesubjects"));
    }
}
