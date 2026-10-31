using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>WPF's Lobby frame and narrow-column switcher. The old sample subject roster is gone.</summary>
public partial class AvailableSubjectsTabView : UserControl
{
    internal bool IsNarrow { get; private set; }
    internal int SelectedColumn { get; private set; }
    internal Func<LobbyGates> ReadGates = () => LobbyGates.From(Platform.FriendsHead.Identity() != null,
        TierGate.HasPremium, TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote").Allowed);
    internal Func<LobbyService>? CreateLobby;
    internal Func<LobbyService> GetLobby = Platform.LobbyHead.Get;
    private LobbyService? _lobby;
    private bool _ownsLobby;
    private IDisposable? _lease;
    private LobbySnapshot _snapshot = LobbySnapshot.Empty;

    public AvailableSubjectsTabView()
    {
        InitializeComponent();
        Repaint();
        InitializeSubjectsMotion();
        SizeChanged += (_, e) => SetNarrow(LobbyPageRules.IsNarrowWidth(e.NewSize.Width));
        // The shell hides tabs with IsVisible, so Loaded fires once; WPF redraws on every open
        // (MainWindow.Lobby.cs:84), which picks up sign-in, Remote gate and language changes.
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) { Repaint(); SyncWatch(); } };
        Loaded += (_, _) =>
        {
            if (_lobby == null)
            {
                _ownsLobby = CreateLobby != null;
                _lobby = CreateLobby?.Invoke() ?? GetLobby();
                _snapshot = _lobby.Snapshot;
                _lobby.Changed += OnLobbyChanged;
            }
            Repaint();
            SyncWatch();
            SubjectsAmbientFx.StartLayers(new AmbientFxConfig
                { Layers = AmbientFxLayers.FogDrift, FogPuffs = 2, Intensity = 0.40 });
        };
        Unloaded += (_, _) =>
        {
            _lease?.Dispose(); _lease = null;
            if (_lobby != null)
            {
                _lobby.Changed -= OnLobbyChanged;
                if (_ownsLobby) _lobby.Dispose();
                _lobby = null;
            }
            SubjectsAmbientFx.Stop();
        };
    }

    private void SyncWatch()
    {
        if (IsVisible && _lobby != null) _lease ??= _lobby.Watch();
        else { _lease?.Dispose(); _lease = null; }
    }

    private void OnLobbyChanged(LobbySnapshot snapshot)
    {
        var gates = ReadGates();
        PaintSnapshot(gates.SignedIn ? snapshot : LobbySnapshot.Empty, gates,
            gates.SignedIn && snapshot.SignedIn && _lobby?.LastRoundOk == false);
    }

    internal void Repaint()
    {
        var gates = ReadGates();
        // WPF recomputes the error panel from LastRoundOk on every paint (MainWindow.Lobby.cs:116).
        PaintSnapshot(gates.SignedIn ? _snapshot with { SignedIn = true } : LobbySnapshot.Empty, gates,
            gates.SignedIn && _lobby?.LastRoundOk == false);
    }

    internal void SetNarrow(bool narrow)
    {
        IsNarrow = narrow;
        LobbyTabStrip.IsVisible = narrow;
        var columns = new Control[] { LobbyOpenColumn, LobbyPlayingSection, LobbyFriendsSection };
        for (var i = 0; i < columns.Length; i++)
        {
            var column = columns[i];
            column.IsVisible = !narrow || i == SelectedColumn;
            Grid.SetColumn(column, narrow ? 0 : i);
            Grid.SetColumnSpan(column, narrow ? 3 : 1);
            column.Margin = narrow ? new Thickness(0) : new Thickness(i == 0 ? 0 : i == 1 ? 4 : 8, 0, i == 0 ? 8 : i == 1 ? 4 : 0, 0);
        }
        var tabs = new[] { TabOpen, TabPlaying, TabFriends };
        for (var i = 0; i < tabs.Length; i++) tabs[i].Opacity = i == SelectedColumn ? 1 : 0.6;
    }

    internal void SelectColumn(int index)
    {
        SelectedColumn = LobbyPageRules.Column(index);
        SetNarrow(IsNarrow);
    }

    private void LobbyTab_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control c && int.TryParse(c.Tag?.ToString(), out var index)) SelectColumn(index);
    }

    internal void PaintSnapshot(LobbySnapshot snapshot, LobbyGates gates, bool error = false)
    {
        _snapshot = snapshot;
        AvailableSubjectsList.ItemsSource = snapshot.Open.Select(r => new LobbyCardView(r, gates)).ToList();
        LobbyPlayingList.ItemsSource = snapshot.Playing.Select(r => new LobbyCardView(r, gates)).ToList();
        LobbyFriendsList.ItemsSource = snapshot.Friends.Select(r => new LobbyCardView(r, gates)).ToList();
        LobbyOpenCount.Text = snapshot.Open.Count.ToString(CultureInfo.InvariantCulture);
        LobbyPlayingCount.Text = snapshot.Playing.Count.ToString(CultureInfo.InvariantCulture);
        LobbyFriendsCount.Text = snapshot.Friends.Count.ToString(CultureInfo.InvariantCulture);
        LobbyPlayingEmpty.IsVisible = snapshot.Playing.Count == 0;
        LobbyFriendsEmpty.IsVisible = snapshot.Friends.Count == 0;
        TxtLobbyEmpty.Text = Loc.Get(snapshot.SignedIn ? "lobby_empty_open" : "lobby_signed_out");
        AvailableSubjectsEmptyPanel.IsVisible = snapshot.Open.Count == 0;
        AvailableSubjectsErrorPanel.IsVisible = error;
        TxtLobbyCount.Text = snapshot.OpenCount > 0 ? Loc.GetF("lobby_open_count", snapshot.OpenCount) : Loc.Get("desc_available_subjects");
        TabOpen.Content = Loc.Get("lobby_list_open") + "  " + snapshot.Open.Count;
        TabPlaying.Content = Loc.Get("lobby_list_playing") + "  " + snapshot.Playing.Count;
        TabFriends.Content = Loc.Get("lobby_list_friends") + "  " + snapshot.Friends.Count;
        RemoteHostLock.IsVisible = !gates.CanHost(LobbyGame.Remote);
        BtnBecomeASubject.Opacity = gates.CanHost(LobbyGame.Remote) ? 1 : 0.7;
        TxtBecomeASubjectSubtitle.IsVisible = gates.SignedIn && !gates.CanHost(LobbyGame.Remote);
        // ponytail: Chess/Goon host and all row Join flows wait for the Lobby service/hosts in the b-lane.
        // Keep those destinations disabled instead of constructing a made-up game or remote claim flow.
    }

    private void BtnBecomeASubject_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not MainShellWindow shell) return;
        if (!ReadGates().SignedIn) { _ = shell.OpenUnifiedLoginDialog(); return; }
        var gate = TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote");
        if (!gate.Allowed) { TierGate.ShowDenied(gate); return; }
        shell.ShowTab("remotecontrol"); // WPF hosting enters Remote's consent/opt-in page; it does not start a session.
    }
}

public sealed class LobbyCardView
{
    public LobbyCardView(LobbyRow row, LobbyGates gates)
    {
        Title = LobbyPageRules.Title(row, Loc.Get);
        Line = LobbyPageRules.Line(Loc.Get(LobbyPageRules.LabelKey(row.Game)), LobbySummary.For(row, Loc.Get));
        Friend = row.Friend;
        HasButton = LobbyPageRules.HasButton(row);
        Locked = LobbyPageRules.Locked(row, gates);
        ButtonText = Loc.Get(row.CanJoin ? "lobby_btn_join" : "lobby_btn_watch");
        ButtonOpacity = LobbyPageRules.ButtonOpacity(Locked);
    }
    public string Title { get; }
    public string Line { get; }
    public bool Friend { get; }
    public bool HasButton { get; }
    public bool Locked { get; }
    public string ButtonText { get; }
    public double ButtonOpacity { get; }
}
