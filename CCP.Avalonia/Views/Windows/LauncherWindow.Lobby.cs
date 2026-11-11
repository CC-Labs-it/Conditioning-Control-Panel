// WPF LauncherWindow.Lobby.cs:42-174 and LauncherWindow.OpenTables.cs:58-83.
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    private readonly DispatcherTimer _lobbyRefresh = new() { Interval = TimeSpan.FromSeconds(60) };
    private LobbyService? _launcherLobby;
    private IDisposable? _lobbyDropLease;

    private void StartLobbyChip()
    {
        var lobby = Panel?.Lobby;
        if (lobby == null) return;
        if (!ReferenceEquals(_launcherLobby, lobby))
        {
            StopLobbyChip();
            _launcherLobby = lobby;
            lobby.Changed += OnLauncherLobbyChanged;
            NavBadges.Changed += OnLauncherBadgeChanged;
        }
        _lobbyRefresh.Tick -= RefreshLauncherLobby;
        _lobbyRefresh.Tick += RefreshLauncherLobby;
        PaintLauncherLobby(lobby.Snapshot);
        _ = lobby.RefreshAsync();
        _lobbyRefresh.Start();
    }

    private void StopLobbyChip()
    {
        LobbyDrop.IsOpen = false;
        _lobbyDropLease?.Dispose();
        _lobbyDropLease = null;
        _lobbyRefresh.Stop();
        if (_launcherLobby != null)
        {
            _launcherLobby.Changed -= OnLauncherLobbyChanged;
            NavBadges.Changed -= OnLauncherBadgeChanged;
            _launcherLobby = null;
        }
    }

    private void RefreshLauncherLobby(object? sender, EventArgs e) { if (_launcherLobby != null) _ = _launcherLobby.RefreshAsync(); }
    private void OnLauncherLobbyChanged(LobbySnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => { if (_launcherLobby != null) PaintLauncherLobby(_launcherLobby.Snapshot); }); return; }
        if (_launcherLobby != null) PaintLauncherLobby(snapshot);
    }
    private void OnLauncherBadgeChanged(string section, int count)
    {
        if (section != NavSections.Social) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => { if (_launcherLobby != null) PaintLobbyOpacity(); }); return; }
        PaintLobbyOpacity();
    }
    private void PaintLobbyOpacity()
    {
        int count = NavBadges.Get(NavSections.Social);
        LobbyChip.Opacity = count <= 0 || NavBadges.IsFresh(count, NavBadges.Seen(NavSections.Social))
            ? 1 : MainShellWindow.NavBadgeSeenOpacity + 0.15;
    }
    private void PaintLauncherLobby(LobbySnapshot snapshot)
    {
        LobbyChip.Content = snapshot.OpenCount > 0 ? Loc.GetF("launcher_lobby_open", snapshot.OpenCount) : Loc.Get("launcher_lobby");
        NavBadges.Set(NavSections.Social, snapshot.OpenCount);
        PaintLobbyOpacity();
        LobbyDropRows.Children.Clear();
        var rows = snapshot.Joinable.Take(6).ToList();
        if (rows.Count == 0)
            LobbyDropRows.Children.Add(new TextBlock { Text = Loc.Get(snapshot.SignedIn ? "lobby_empty_open" : "lobby_signed_out"), Foreground = Brushes.White, Margin = new Thickness(6, 4, 6, 10) });
        foreach (var row in rows)
        {
            var card = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 6) };
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = LobbyPageRules.Title(row, Loc.Get), Foreground = Brushes.White });
            text.Children.Add(new TextBlock { Text = LobbyPageRules.Line(Loc.Get(LobbyPageRules.LabelKey(row.Game)), LobbySummary.For(row, Loc.Get)), Foreground = Brushes.White, FontSize = 12, Opacity = 0.75 });
            card.Children.Add(text);
            // Keyed join hosts are not yet available on this head (Lobby page also disables them).
            var join = new Button { Content = Loc.Get("lobby_btn_join"), IsEnabled = false, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(join, 1);
            card.Children.Add(join);
            LobbyDropRows.Children.Add(card);
        }
        var open = new Button { Content = Loc.Get("launcher_lobby_open_page"), HorizontalAlignment = HorizontalAlignment.Center };
        open.Click += (_, _) => { LobbyDrop.IsOpen = false; if (!MainShellWindow.LockdownActive) OpenPanel(p => p.ShowTab("availablesubjects")); };
        LobbyDropRows.Children.Add(open);
    }
    private void LobbyChip_Click(object? sender, RoutedEventArgs e)
    {
        if (_launcherLobby != null && !MainShellWindow.LockdownActive) LobbyDrop.IsOpen = !LobbyDrop.IsOpen;
    }
    private void LobbyDrop_Opened(object? sender, EventArgs e)
    {
        if (_launcherLobby == null) return;
        _lobbyDropLease ??= _launcherLobby.Watch();
        NavBadges.MarkSeen(NavSections.Social);
        PaintLobbyOpacity();
    }
    private void LobbyDrop_Closed(object? sender, EventArgs e) { _lobbyDropLease?.Dispose(); _lobbyDropLease = null; }
}
