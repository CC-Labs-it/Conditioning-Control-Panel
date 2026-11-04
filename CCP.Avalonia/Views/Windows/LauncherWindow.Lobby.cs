using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    internal LobbyPoll? LobbyForTest;
    private LobbyPoll? _lobby;
    private IDisposable? _lobbyLease;
    private readonly DispatcherTimer _lobbyTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private bool _lobbyHooked;
    internal bool LobbyPolling => _lobbyTimer.IsEnabled;
    internal TimeSpan LobbyPollInterval => _lobbyTimer.Interval;

    private void HookLobby()
    {
        LobbyDrop.Opened += (_, _) => { if (_lobbyHooked) _lobbyLease ??= _lobby?.Watch(); };
        LobbyDrop.Closed += (_, _) => { _lobbyLease?.Dispose(); _lobbyLease = null; };
        _lobbyTimer.Tick += (_, _) => RefreshLobbyTick();
        PaintLobby(LobbySnapshot.Empty);
    }

    internal void RefreshLobbyTick() { if (_lobbyHooked) _ = _lobby?.RefreshAsync(); }

    private void StartLobby()
    {
        if (_lobbyHooked) return;
        _lobby = LobbyForTest ?? LobbyHead.Service;
        _lobby.Changed += OnLobbyChanged;
        _lobbyHooked = true;
        PaintLobby(_lobby.Snapshot);
        _ = _lobby.RefreshAsync();
        _lobbyTimer.Start();
    }

    private void StopLobby()
    {
        _lobbyTimer.Stop();
        LobbyDrop.IsOpen = false;
        _lobbyLease?.Dispose(); _lobbyLease = null;
        if (_lobbyHooked && _lobby != null) _lobby.Changed -= OnLobbyChanged;
        _lobbyHooked = false;
    }

    private void OnLobbyChanged(LobbySnapshot snapshot)
    {
        if (_lobbyHooked) PaintLobby(snapshot);
    }

    private void LobbyChip_Click(object? sender, RoutedEventArgs e)
    {
        if (!_lobbyHooked) return;
        PaintLobby(_lobby?.Snapshot ?? LobbySnapshot.Empty);
        LobbyDrop.IsOpen = !LobbyDrop.IsOpen;
    }

    internal void PaintLobby(LobbySnapshot snapshot)
    {
        LobbyChipText.Text = snapshot.OpenCount > 0
            ? Loc.GetF("launcher_lobby_open", snapshot.OpenCount) : Loc.Get("launcher_lobby");
        LobbyDropRows.Children.Clear();
        var rows = snapshot.Joinable.Take(6).ToArray();
        if (rows.Length == 0)
            LobbyDropRows.Children.Add(new TextBlock { Text = Loc.Get(snapshot.SignedIn ? "lobby_empty_open" : "lobby_signed_out"), Margin = new Thickness(6, 4, 6, 10) });
        foreach (var row in rows)
        {
            var detail = LobbyPageRules.Line(Loc.Get(LobbyPageRules.LabelKey(row.Game)), LobbySummary.For(row, Loc.Get));
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = LobbyPageRules.Title(row, Loc.Get), FontWeight = global::Avalonia.Media.FontWeight.SemiBold });
            label.Children.Add(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.72 });
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
            grid.Children.Add(label);
            // No destination on this base implements the Lobby join contract. Keep the button shut.
            var join = new Button { Content = Loc.Get("lobby_btn_join"), IsEnabled = false, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(join, Loc.Get("exclusives_not_on_this_build"));
            Grid.SetColumn(join, 1);
            grid.Children.Add(join);
            LobbyDropRows.Children.Add(grid);
        }
        var open = new Button { Content = Loc.Get("launcher_lobby_open_page"), HorizontalAlignment = HorizontalAlignment.Center };
        open.Click += (_, _) => { LobbyDrop.IsOpen = false; OpenPanel(p => p.ShowTab("availablesubjects")); };
        LobbyDropRows.Children.Add(open);
    }
}
