// WPF LauncherWindow.Lobby.cs:42-174 and LauncherWindow.OpenTables.cs:58-83.
using System;
using System.Linq;
using System.Collections.Generic;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    private static readonly Dictionary<string, Bitmap?> LobbyArt = new();
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
        foreach (var row in rows) LobbyDropRows.Children.Add(LobbyDropRow(row));
        var open = new Button { Content = Loc.Get("launcher_lobby_open_page"), HorizontalAlignment = HorizontalAlignment.Center };
        open.Click += (_, _) => { LobbyDrop.IsOpen = false; if (!MainShellWindow.LockdownActive) OpenPanel(p => p.ShowTab("availablesubjects")); };
        LobbyDropRows.Children.Add(open);
    }
    // WPF LauncherWindow.Lobby.cs:177-242, LobbyRowView.cs:83-91.
    private Border LobbyDropRow(LobbyRow row)
    {
        string artPath = row.Game switch
        {
            LobbyGame.Chess => "features/piecebypiece.png", LobbyGame.Goon => "features/goon_game_tile.png",
            LobbyGame.Race => "features/race.png", _ => "features/lobby_remote.png"
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        if (!LobbyArt.TryGetValue(artPath, out var bitmap)) LobbyArt[artPath] = bitmap = ModArt.TryLoadBuiltIn(artPath, 64);
        var icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 10, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            BorderThickness = new Thickness(1), Background = bitmap == null ? null : new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill } };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = LobbyPageRules.Title(row, Loc.Get), Foreground = Brushes.White,
            FontFamily = new FontFamily("Fredoka, Segoe UI"), FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        text.Children.Add(new TextBlock { Text = LobbyPageRules.Line(Loc.Get(LobbyPageRules.LabelKey(row.Game)), LobbySummary.For(row, Loc.Get)),
            Foreground = Brushes.White, FontSize = 12, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
        Grid.SetColumn(text, 1);
        // Goon does not yet have a keyed host bridge on this head.
        var join = new Button { Content = Loc.Get("lobby_btn_join"), IsEnabled = row.Game != LobbyGame.Goon,
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        join.Click += (_, _) => JoinFromLobby(row);
        Grid.SetColumn(join, 2);
        grid.Children.Add(icon); grid.Children.Add(text); grid.Children.Add(join);
        var lift = new ScaleTransform(1, 1);
        var card = new Border { CornerRadius = new CornerRadius(13), Padding = new Thickness(8, 7),
            Margin = new Thickness(0, 0, 0, 6), Background = Glass(0x24, 0x0C),
            BorderBrush = Glass(0x55, 0x0A), BorderThickness = new Thickness(1),
            RenderTransform = lift, RenderTransformOrigin = RelativePoint.Center, Child = grid };
        void Hover(bool on)
        {
            lift.Transitions = Env.AllowTransitions ? new Transitions
            {
                new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(150) },
                new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(150) }
            } : null;
            lift.ScaleX = lift.ScaleY = on ? 1.02 : 1;
        }
        card.PointerEntered += (_, _) => Hover(true);
        card.PointerExited += (_, _) => Hover(false);
        return card;
    }

    private static LinearGradientBrush Glass(byte top, byte bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(top, 255, 255, 255), 0), new GradientStop(Color.FromArgb(bottom, 255, 255, 255), 1) }
    };

    private void JoinFromLobby(LobbyRow row)
    {
        LobbyDrop.IsOpen = false;
        try
        {
            if (MainShellWindow.LockdownActive || !row.CanJoin) return;
            if (_launcherLobby?.SignedIn() != true) { OpenSignIn(); return; }
            if (row.Game == LobbyGame.Race) { GameWindow.JoinGlassroadRoom(row.Key); return; }
            if (row.Game == LobbyGame.Remote) { ClaimRemoteFromLobby(row.Key!); return; }
            if (row.Game != LobbyGame.Chess || !GameWindow.IsPbpTableId(row.Key)) return;
            if (MainShellWindow.LeashBlocksGames) { OpenPanel(p => p.PresentLeashGateFromLauncher()); return; }
            var game = GameWindow.Launch(GameWindow.PbpId);
            if (game != null && game.QueuePbpJoin(row.Key)) TrackLaunchedGame(game, GameWindow.PbpId);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Launcher] lobby join failed"); }
    }

    // WPF MainWindow.RemoteControl.cs:499-518. The returned session URL is never logged.
    private async void ClaimRemoteFromLobby(string key)
    {
        var lobby = _launcherLobby;
        if (lobby == null) return;
        var url = await lobby.ClaimRemoteAsync(key);
        if (url != null) ExternalOpener.Open(url);
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
