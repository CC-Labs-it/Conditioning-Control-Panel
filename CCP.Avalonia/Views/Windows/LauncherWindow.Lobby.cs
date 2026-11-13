using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
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
    // One claim path: the Subjects page client (honest user agent, merged-account recovery). The url holds the PIN.
    internal Func<string, System.Threading.CancellationToken, System.Threading.Tasks.Task<string?>> ClaimLobbySubject =
        async (key, ct) => await new RemoteDirectoryClaimClient().ClaimAsync(key, ct) is { Status: RemoteClaimStatus.Success } r
            ? r.SessionUrl : null;
    internal Func<string, bool> OpenLobbyUrl = Platform.ExternalOpener.Open;
    private LobbyPoll? _lobby;
    private DispatcherTimer? _lobbyTimer;
    private ContextMenu? _lobbyDrop;
    private IDisposable? _lobbyLease;
    private System.Threading.CancellationTokenSource? _lobbyClaim;

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
        var menu = new ContextMenu { Placement = PlacementMode.Top, Width = 380,
            Background = new SolidColorBrush(Color.Parse("#F2160B22")),
            BorderBrush = new SolidColorBrush(Color.Parse("#40FFFFFF")), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16), Padding = new Thickness(12) };
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
            var artPath = row.Game switch
            {
                LobbyGame.Chess => "features/piecebypiece.png",
                LobbyGame.Goon => "features/goon_game_tile.png",
                LobbyGame.Race => "features/race.png",
                _ => "features/lobby_remote.png"
            };
            var art = new Image { Width = 36, Height = 36, Stretch = Stretch.UniformToFill,
                Source = ModArt.TryLoad(artPath, 96), Margin = new Thickness(0, 0, 10, 0) };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children =
            {
                new TextBlock { Text = card.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = card.Line, FontSize = 11.5, Opacity = 0.72, TextTrimming = TextTrimming.CharacterEllipsis }
            } };
            var join = new Button { Content = new TextBlock { Text = card.ButtonText, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse("#2A0618")) }, Opacity = card.ButtonOpacity,
                Background = new SolidColorBrush(Color.Parse("#FF8CC0")), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 4, 14, 5),
                IsEnabled = row.Game is LobbyGame.Chess or LobbyGame.Race or LobbyGame.Remote,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var travel = new TranslateTransform();
            join.RenderTransform = travel;
            join.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(join).Properties.IsLeftButtonPressed) travel.Y = 3;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            join.AddHandler(InputElement.PointerReleasedEvent, (_, _) => travel.Y = 0, RoutingStrategies.Tunnel, handledEventsToo: true);
            join.PointerExited += (_, _) => travel.Y = 0;
            join.PointerCaptureLost += (_, _) => travel.Y = 0;
            join.Click += (_, e) => { e.Handled = true; _lobbyDrop?.Close(); JoinLobbyRow(row); };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            Grid.SetColumn(text, 1); Grid.SetColumn(join, 2);
            grid.Children.Add(art); grid.Children.Add(text); grid.Children.Add(join);
            var frame = new Border { Width = 350, CornerRadius = new CornerRadius(13), Padding = new Thickness(8, 7),
                Background = new SolidColorBrush(Color.Parse("#24FFFFFF")),
                BorderBrush = new SolidColorBrush(Color.Parse("#55FFFFFF")), BorderThickness = new Thickness(1), Child = grid };
            var scale = new ScaleTransform(1, 1);
            frame.RenderTransform = scale;
            void Lift(bool on)
            {
                scale.Transitions = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowTransitions ? new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new QuadraticEaseOut() },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new QuadraticEaseOut() },
                } : null;
                scale.ScaleX = scale.ScaleY = on ? 1.02 : 1;
            }
            frame.PointerEntered += (_, _) => Lift(true);
            frame.PointerExited += (_, _) => Lift(false);
            var item = new MenuItem { Header = frame };
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
    /// <summary>WPF MainWindow.ClaimRemoteSubjectAsync (MainWindow.RemoteControl.cs:499). Lockdown starting
    /// mid-claim cancels it, and a url that still comes back is not opened through the veil.</summary>
    private async System.Threading.Tasks.Task ClaimRemoteLobby(string key)
    {
        var token = (_lobbyClaim ??= new System.Threading.CancellationTokenSource()).Token;
        string? url = null;
        try { url = await ClaimLobbySubject(key, token); }
        catch (OperationCanceledException) { }
        if (!string.IsNullOrEmpty(url) && !token.IsCancellationRequested && !MainShellWindow.LockdownActive) OpenLobbyUrl(url);
        _ = _lobby?.RefreshAsync();
    }
    internal static void CancelLobbyClaimForPanic() => Instance?.CancelLobbyClaim();

    private void CancelLobbyClaim()
    {
        _lobbyClaim?.Cancel();
        _lobbyClaim = null;
    }

}
