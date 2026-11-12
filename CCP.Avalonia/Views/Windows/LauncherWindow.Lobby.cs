using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;
namespace ConditioningControlPanel.Avalonia.Views.Windows;
public partial class LauncherWindow
{
    internal LobbyPoll? LobbyForTest;
    /// <summary>Seams for the Remote claim: the directory wire and the browser (WPF MainWindow.RemoteControl.cs:499).</summary>
    internal Func<string, Task<string?>> ClaimRemote = key => LobbyHead.Remote.ClaimAsync(key);
    internal Func<string, bool> OpenSession = ExternalOpener.Open;
    internal const double LobbyHoverScale = 1.02; // WPF MotionFx.HoverLiftScale, 150 ms ease-out
    private LobbyPoll? _lobby;
    private IDisposable? _lobbyLease;
    private readonly DispatcherTimer _lobbyTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private bool _lobbyHooked;
    private static readonly Dictionary<LobbyGame, Bitmap?> LobbyArtCache = new();
    private static Bitmap? LobbyArt(LobbyGame game)
    {
        if (LobbyArtCache.TryGetValue(game, out var art)) return art;
        var file = game switch { LobbyGame.Chess => "piecebypiece", LobbyGame.Goon => "goon_game_tile", LobbyGame.Race => "race", _ => "lobby_remote" };
        try { using var stream = AssetLoader.Open(new Uri($"avares://CCP.Avalonia/Resources/features/{file}.png")); art = new Bitmap(stream); } catch { art = null; }
        return LobbyArtCache[game] = art;
    }
    internal bool LobbyPolling => _lobbyTimer.IsEnabled;
    internal TimeSpan LobbyPollInterval => _lobbyTimer.Interval;
    private void HookLobby()
    {
        LobbyDrop.Opened += (_, _) => { NavBadges.MarkSeen(NavSections.Social); PaintLobbyOpacity(); if (_lobbyHooked) _lobbyLease ??= _lobby?.Watch(); };
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
        _lobbyTimer.Stop(); LobbyDrop.IsOpen = false;
        _lobbyLease?.Dispose(); _lobbyLease = null;
        if (_lobbyHooked && _lobby != null) _lobby.Changed -= OnLobbyChanged;
        _lobbyHooked = false;
    }
    private void OnLobbyChanged(LobbySnapshot snapshot) { if (_lobbyHooked) PaintLobby(snapshot); }
    private void LobbyChip_Click(object? sender, RoutedEventArgs e)
    {
        if (!_lobbyHooked) return;
        PaintLobby(_lobby?.Snapshot ?? LobbySnapshot.Empty);
        LobbyDrop.IsOpen = !LobbyDrop.IsOpen;
    }
    private void PaintLobbyOpacity() => LobbyChip.Opacity = NavBadges.Get(NavSections.Social) <= 0 || NavBadges.IsFresh(NavSections.Social) ? 1 : 0.60;
    /// <summary>Goon has no host on this head (GameWindow.Escape.cs:7), so its Join stays off.</summary>
    private static bool LobbyDoorOpen(LobbyGame game) => game != LobbyGame.Goon;
    /// <summary>WPF LauncherWindow.Lobby.cs:244 JoinFromDrop: the account and tier gate ask for sign-in,
    /// then each game opens on the table.</summary>
    internal void JoinLobbyRow(LobbyRow row)
    {
        LobbyDrop.IsOpen = false;
        if (!row.CanJoin || string.IsNullOrEmpty(row.Key) || !LobbyDoorOpen(row.Game)) return;
        var gates = Tabs.AvailableSubjectsTabView.CurrentGates();
        if (!gates.SignedIn || !gates.CanJoin(row.Game)) { OpenSignIn(); return; }
        try
        {
            // Chess: WPF skips the seat while a leash punishment waits and the game door shows the gate.
            if (row.Game == LobbyGame.Chess && MainShellWindow.LeashBlocksGames) OpenPanel(p => p.PresentLeashGateFromLauncher());
            else if (row.Game == LobbyGame.Chess) LaunchGameWindow(GameWindow.PbpId, () => GameWindow.OpenChessLobby("join", row.Key));
            else if (row.Game == LobbyGame.Remote) _ = ClaimRemoteRowAsync(row.Key);
            // Race: WPF MainWindow.OpenRaceJoin, a malformed room id never leaves; the race gate runs in the host.
            else if (row.Game == LobbyGame.Race && RaceJoinLink.IsRoomId(row.Key)) GameWindow.LaunchFriendRace(row.Key);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Launcher] lobby join {Game} failed", row.Game); }
    }
    /// <summary>WPF ClaimRemoteSubjectAsync: claim, open the session url, refresh. The url is never logged.</summary>
    internal async Task ClaimRemoteRowAsync(string key)
    {
        var url = await ClaimRemote(key);
        if (string.IsNullOrEmpty(url)) { if (_lobbyHooked) _ = _lobby?.RefreshAsync(); return; }
        try { OpenSession(url); } catch (Exception ex) { Serilog.Log.Warning(ex, "[Launcher] failed to open browser for claimed session"); }
        if (_lobbyHooked) _ = _lobby?.RefreshAsync();
    }
    internal void PaintLobby(LobbySnapshot snapshot)
    {
        LobbyChipText.Text = snapshot.OpenCount > 0 ? Loc.GetF("launcher_lobby_open", snapshot.OpenCount) : Loc.Get("launcher_lobby");
        NavBadges.Set(NavSections.Social, snapshot.OpenCount);
        PaintLobbyOpacity();
        LobbyDropRows.Children.Clear();
        var gates = Tabs.AvailableSubjectsTabView.CurrentGates();
        var rows = snapshot.Joinable.Take(6).ToArray();
        if (rows.Length == 0)
            LobbyDropRows.Children.Add(new TextBlock { Text = Loc.Get(snapshot.SignedIn ? "lobby_empty_open" : "lobby_signed_out"), Margin = new Thickness(6, 4, 6, 10) });
        foreach (var row in rows)
        {
            var detail = LobbyPageRules.Line(Loc.Get(LobbyPageRules.LabelKey(row.Game)), LobbySummary.For(row, Loc.Get));
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = LobbyPageRules.Title(row, Loc.Get), FontWeight = FontWeight.SemiBold });
            label.Children.Add(new TextBlock { Text = detail, FontSize = 11, Opacity = 0.72 });
            var lift = new ScaleTransform();
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 6), RenderTransform = lift };
            grid.PointerEntered += (_, _) => HoverLift(lift, true);
            grid.PointerExited += (_, _) => HoverLift(lift, false);
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            var join = new Button { IsEnabled = LobbyDoorOpen(row.Game), VerticalAlignment = VerticalAlignment.Center,
                Opacity = LobbyPageRules.ButtonOpacity(LobbyPageRules.Locked(row, gates)) };
            join.Click += (_, _) => JoinLobbyRow(row);
            if (!join.IsEnabled) ToolTip.SetTip(join, Loc.Get("exclusives_not_on_this_build"));
            join.Background = Brushes.Transparent; join.BorderThickness = new Thickness(0); join.Padding = new Thickness(0);
            var press = new TranslateTransform(); var face = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 4), Margin = new Thickness(0, 0, 0, 3), RenderTransform = press, Background = new LinearGradientBrush { GradientStops = { new GradientStop(Color.Parse("#FF8CC0"), 0), new GradientStop(Color.Parse("#FF5FA2"), 1) } }, Child = new TextBlock { Text = Loc.Get("lobby_btn_join"), Foreground = new SolidColorBrush(Color.Parse("#2A0618")), FontWeight = FontWeight.Bold } };
            join.Content = new Grid { Children = { new Border { CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 3, 0, 0), Background = new SolidColorBrush(Color.Parse("#B23A6B")) }, face } };
            join.PointerPressed += (_, _) => press.Y = 3;
            join.PointerReleased += (_, _) => press.Y = 0;
            join.PointerExited += (_, _) => press.Y = 0;
            Grid.SetColumn(join, 2);
            grid.Children.Add(join);
            grid.Children.Add(new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Margin = new Thickness(8, 7, 10, 7), ClipToBounds = true,
                Child = new Image { Source = LobbyArt(row.Game), Stretch = Stretch.UniformToFill } });
            var glass = new Border { ZIndex = -1, CornerRadius = new CornerRadius(13), Background = new LinearGradientBrush { GradientStops = { new GradientStop(Color.Parse("#24FFFFFF"), 0), new GradientStop(Color.Parse("#0CFFFFFF"), 1) } }, BorderBrush = new LinearGradientBrush { GradientStops = { new GradientStop(Color.Parse("#55FFFFFF"), 0), new GradientStop(Color.Parse("#0AFFFFFF"), 1) } }, BorderThickness = new Thickness(1) };
            Grid.SetColumnSpan(glass, 3); grid.Children.Add(glass);
            LobbyDropRows.Children.Add(grid);
        }
        var open = new Button { Content = Loc.Get("launcher_lobby_open_page"), HorizontalAlignment = HorizontalAlignment.Center };
        open.Click += (_, _) => { LobbyDrop.IsOpen = false; OpenPanel(p => p.ShowTab("availablesubjects")); };
        LobbyDropRows.Children.Add(open);
    }
    /// <summary>WPF MotionFx.HoverLift: scale to 1.02 over 150 ms, or snap there when transitions are off.</summary>
    internal static void HoverLift(ScaleTransform scale, bool on)
    {
        static DoubleTransition Ease(AvaloniaProperty p) => new() { Property = p, Duration = TimeSpan.FromMilliseconds(150), Easing = new QuadraticEaseOut() };
        scale.Transitions = AmbientFxCanvas.Env.AllowTransitions ? new Transitions { Ease(ScaleTransform.ScaleXProperty), Ease(ScaleTransform.ScaleYProperty) } : null;
        scale.ScaleX = scale.ScaleY = on ? LobbyHoverScale : 1.0;
    }
}
