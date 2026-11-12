using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Games;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    internal Func<LobbyPoll> CreateLobby = AvailableSubjectsTabView.BuildLobby;
    internal Func<LobbyGates> ReadLobbyGates = () => LobbyGates.From(Platform.FriendsHead.Identity() != null,
        TierGate.HasPremium, TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote").Allowed);
    internal Func<LobbyRow, Window?> OpenLobbyGame = row => row.Game == LobbyGame.Chess
        ? GameWindow.OpenChessLobby("join", row.Key) : GameWindow.LaunchFriendRace(row.Key);
    internal Func<string, System.Threading.Tasks.Task<string?>> ClaimLobbySubject = key => new RemoteLobbyApi().ClaimAsync(key);
    internal Func<string, bool> OpenLobbyUrl = Platform.ExternalOpener.Open;
    private LobbyPoll? _lobby;
    private DispatcherTimer? _lobbyTimer;
    private ContextMenu? _lobbyDrop;
    private IDisposable? _lobbyLease;

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
    private void StopLobby()
    {
        _lobbyTimer?.Stop();
        _lobbyDrop?.Close();
        _lobbyLease?.Dispose();
        _lobbyLease = null;
    }
    private void PaintLobby(LobbySnapshot snapshot)
    {
        LobbyChipText.Text = snapshot.OpenCount > 0
            ? Loc.GetF("launcher_lobby_open", snapshot.OpenCount) : Loc.Get("launcher_lobby");
        if (_lobbyDrop is { IsOpen: true }) FillLobbyDrop(_lobbyDrop, snapshot);
    }
    private void LobbyChip_Click(object? sender, RoutedEventArgs e)
    {
        if (MainShellWindow.LockdownActive || _lobby == null) return;
        var menu = new ContextMenu { Placement = PlacementMode.Top };
        FillLobbyDrop(menu, _lobby.Snapshot);
        menu.Opened += (_, _) => _lobbyLease ??= _lobby.Watch();
        menu.Closed += (_, _) => { _lobbyLease?.Dispose(); _lobbyLease = null; };
        _lobbyDrop = menu;
        LobbyChip.ContextMenu = menu;
        menu.Open(LobbyChip);
    }
    private void FillLobbyDrop(ContextMenu menu, LobbySnapshot snapshot)
    {
        menu.Items.Clear();
        var rows = snapshot.Joinable.Take(6).ToList();
        if (rows.Count == 0)
            menu.Items.Add(new MenuItem { Header = Loc.Get(snapshot.SignedIn ? "lobby_empty_open" : "lobby_signed_out"), IsEnabled = false });
        foreach (var row in rows)
        {
            var card = new LobbyCardView(row, ReadLobbyGates());
            var item = new MenuItem { Header = card.Title + "  " + card.Line + "  " + card.ButtonText,
                IsEnabled = row.Game is LobbyGame.Chess or LobbyGame.Race or LobbyGame.Remote };
            item.Click += (_, _) => JoinLobbyRow(row);
            menu.Items.Add(item);
        }
        var open = new MenuItem { Header = Loc.Get("launcher_lobby_open_page") };
        open.Click += (_, _) => OpenPanel(p => p.ShowTab("availablesubjects"));
        menu.Items.Add(open);
    }
    private void JoinLobbyRow(LobbyRow row)
    {
        if (MainShellWindow.LockdownActive || !row.CanJoin) return;
        var gates = ReadLobbyGates();
        if (!gates.CanJoin(row.Game)) { if (Panel is { } panel) _ = panel.OpenUnifiedLoginDialog(); return; }
        if (row.Game == LobbyGame.Chess && MainShellWindow.LeashBlocksGames)
        {
            OpenPanel(p => p.PresentLeashGateFromLauncher());
            return;
        }
        if (row.Game == LobbyGame.Chess) HandoffGame(OpenLobbyGame(row), row.Game.ToString());
        else if (row.Game == LobbyGame.Race && RaceJoinLink.IsRoomId(row.Key)) _ = OpenLobbyGame(row);
        else if (row.Game == LobbyGame.Remote) _ = ClaimRemoteLobby(row.Key!);
    }
    private async System.Threading.Tasks.Task ClaimRemoteLobby(string key)
    {
        var url = await ClaimLobbySubject(key);
        if (!string.IsNullOrEmpty(url)) OpenLobbyUrl(url);
        _ = _lobby?.RefreshAsync();
    }

}
