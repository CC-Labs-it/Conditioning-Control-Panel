using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Animation;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    internal LobbyPoll? LobbyForTest;
    private LobbyPoll? _lobby;
    private IDisposable? _lobbyLease;
    private readonly DispatcherTimer _lobbyTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private bool _lobbyHooked;
    private static readonly Dictionary<LobbyGame, Bitmap?> LobbyArtCache = new();
    private static Bitmap? LobbyArt(LobbyGame game)
    {
        if (LobbyArtCache.TryGetValue(game, out var art)) return art;
        var file = game switch { LobbyGame.Chess => "piecebypiece", LobbyGame.Goon => "goon_game_tile", LobbyGame.Race => "race", _ => "lobby_remote" };
        try { using var stream = AssetLoader.Open(new Uri($"avares://CCP.Avalonia/Resources/features/{file}.png")); art = new Bitmap(stream); }
        catch { art = null; }
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

    private void PaintLobbyOpacity() => LobbyChip.Opacity = NavBadges.Get(NavSections.Social) <= 0 || NavBadges.IsFresh(NavSections.Social) ? 1 : 0.60;

    internal void JoinLobbyRow(LobbyRow row)
    {
        if (!row.CanJoin || row.Game != LobbyGame.Race || !RaceJoinLink.IsRoomId(row.Key) || !Games.GameWindow.GlassroadDoorOpen()) return;
        LobbyDrop.IsOpen = false;
        if (FriendsHead.Identity() == null) { OpenPanel(p => _ = p.OpenUnifiedLoginDialog()); return; }
        var gate = TierGate.RequiresLab(Loc.Get("launcher_game_race_title"));
        if (!gate.Allowed) { TierGate.ShowDenied(gate); return; }
        Games.GameWindow.Launch(Games.GameWindow.GlassroadId, row.Key);
    }

    internal void PaintLobby(LobbySnapshot snapshot)
    {
        LobbyChipText.Text = snapshot.OpenCount > 0
            ? Loc.GetF("launcher_lobby_open", snapshot.OpenCount) : Loc.Get("launcher_lobby");
        NavBadges.Set(NavSections.Social, snapshot.OpenCount);
        PaintLobbyOpacity();
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
            var lift = new TranslateTransform();
            if (AmbientFxCanvas.Env.AllowTransitions) lift.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(150) } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 6), RenderTransform = lift };
            grid.PointerEntered += (_, _) => { if (AmbientFxCanvas.Env.AllowTransitions) lift.Y = -2; };
            grid.PointerExited += (_, _) => lift.Y = 0;
            Grid.SetColumn(label, 1);
            grid.Children.Add(label);
            // Glassroad is the only join destination available on this head, behind its preview door.
            var join = new Button { Content = Loc.Get("lobby_btn_join"), IsEnabled = row.Game == LobbyGame.Race && Games.GameWindow.GlassroadDoorOpen() && RaceJoinLink.IsRoomId(row.Key), VerticalAlignment = VerticalAlignment.Center };
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
            var art = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Margin = new Thickness(8, 7, 10, 7), ClipToBounds = true,
                Child = new Image { Source = LobbyArt(row.Game), Stretch = Stretch.UniformToFill } };
            grid.Children.Add(art);
            var glass = new Border { ZIndex = -1, CornerRadius = new CornerRadius(13), Background = new LinearGradientBrush { GradientStops = { new GradientStop(Color.Parse("#24FFFFFF"), 0), new GradientStop(Color.Parse("#0CFFFFFF"), 1) } }, BorderBrush = new LinearGradientBrush { GradientStops = { new GradientStop(Color.Parse("#55FFFFFF"), 0), new GradientStop(Color.Parse("#0AFFFFFF"), 1) } }, BorderThickness = new Thickness(1) };
            Grid.SetColumnSpan(glass, 3); grid.Children.Add(glass);
            LobbyDropRows.Children.Add(grid);
        }
        var open = new Button { Content = Loc.Get("launcher_lobby_open_page"), HorizontalAlignment = HorizontalAlignment.Center };
        open.Click += (_, _) => { LobbyDrop.IsOpen = false; OpenPanel(p => p.ShowTab("availablesubjects")); };
        LobbyDropRows.Children.Add(open);
    }
}
