using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Lobby;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>WPF Lobby frame, watched discovery and narrow-column switcher.</summary>
public partial class AvailableSubjectsTabView : UserControl
{
    internal bool IsNarrow { get; private set; }
    internal int SelectedColumn { get; private set; }
    internal Func<LobbyGates> ReadGates = () => LobbyGates.From(Platform.FriendsHead.Identity() != null,
        TierGate.HasPremium, TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote").Allowed);
    internal Func<LobbyPoll>? CreateLobby;
    // Prime gate and the Linux race card live behind GameWindow.LaunchFriendRace (owner B2).
    internal Action<string> OpenRace = room => GameWindow.LaunchFriendRace(room);
    private LobbyPoll? _lobby;
    private IDisposable? _lease;
    private LobbySnapshot _snapshot = LobbySnapshot.Empty;
    // Stable sources: a show, an entitlement refresh or a poll with unchanged data rebuilds nothing,
    // and a changed poll replaces only the card slots whose values differ (perf: tab switch).
    private readonly ObservableCollection<LobbyCardView> _open = new(), _playing = new(), _friends = new();
    private LobbyGates? _paintedGates;
    private string? _paintedLanguage;
    private bool _paintedError;

    public AvailableSubjectsTabView()
    {
        InitializeComponent();
        AvailableSubjectsList.ItemsSource = _open;
        LobbyPlayingList.ItemsSource = _playing;
        LobbyFriendsList.ItemsSource = _friends;
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
                _lobby = CreateLobby?.Invoke() ?? BuildLobby();
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
            if (_lobby != null) { _lobby.Changed -= OnLobbyChanged; _lobby.Dispose(); _lobby = null; }
            SubjectsAmbientFx.Stop();
        };
    }
    private static LobbyPoll BuildLobby()
    {
        var remote = new RemoteLobbyApi();
        return new LobbyPoll(new Platform.FriendsHead.Timer(),
            () => Platform.FriendsHead.Identity() != null,
            () => Platform.FriendsHead.Service?.Snapshot.Friends ?? Array.Empty<Friend>(),
            () => new PbpLobbyApi().FetchAsync(), () => GoonOpenTables.RefreshAsync(), () => remote.FetchAsync(),
            () => LobbyWire.DefaultBaseUrl() is { } url
                ? new RaceOpenApi(identity: LobbyWire.DefaultIdentity, baseUrl: url).FetchAsync()
                : Task.FromResult(RaceOpenReply.Empty));
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
        var language = LocalizationManager.Instance.CurrentLanguage;
        if (_paintedGates == gates && _paintedLanguage == language && _paintedError == error
            && _snapshot.SignedIn == snapshot.SignedIn && _snapshot.Open.SequenceEqual(snapshot.Open)
            && _snapshot.Playing.SequenceEqual(snapshot.Playing) && _snapshot.Friends.SequenceEqual(snapshot.Friends)) return;
        // Copies: a caller may reuse a mutable list for its next snapshot.
        _snapshot = snapshot with { Open = snapshot.Open.ToArray(), Playing = snapshot.Playing.ToArray(), Friends = snapshot.Friends.ToArray() };
        _paintedGates = gates; _paintedLanguage = language; _paintedError = error;
        UpdateCards(_open, snapshot.Open, gates);
        UpdateCards(_playing, snapshot.Playing, gates);
        UpdateCards(_friends, snapshot.Friends, gates);
        // WPF MainWindow.Lobby.cs:146-155: the first fill staggers in; after that only a new table pops.
        QueueLobbyEntrance(_open.Concat(_playing).Concat(_friends).Select(v => v.Id).ToList());
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
        ChessHostLock.IsVisible = !gates.CanHost(LobbyGame.Chess);
        GoonHostLock.IsVisible = !gates.CanHost(LobbyGame.Goon);
        BtnHostChess.Opacity = gates.CanHost(LobbyGame.Chess) ? 1 : 0.7;
        BtnHostGoon.Opacity = gates.CanHost(LobbyGame.Goon) ? 1 : 0.7;
        RemoteHostLock.IsVisible = !gates.CanHost(LobbyGame.Remote);
        BtnBecomeASubject.Opacity = gates.CanHost(LobbyGame.Remote) ? 1 : 0.7;
        TxtBecomeASubjectSubtitle.IsVisible = gates.SignedIn && !gates.CanHost(LobbyGame.Remote);
        // Goon doors and Remote claims remain part B.
        // Keep those destinations disabled instead of constructing a made-up game or remote claim flow.
    }

    private static void UpdateCards(ObservableCollection<LobbyCardView> cards, IReadOnlyList<LobbyRow> rows, LobbyGates gates)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var card = new LobbyCardView(rows[i], gates);
            if (i == cards.Count) cards.Add(card);
            else if (cards[i] != card) cards[i] = card;
        }
        while (cards.Count > rows.Count) cards.RemoveAt(cards.Count - 1);
    }

    private void BtnHostChess_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not MainShellWindow shell) return;
        if (!ReadGates().SignedIn) { _ = shell.OpenUnifiedLoginDialog(); return; }
        GameWindow.OpenChessLobby("host");
    }

    private void BtnLobbyRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: LobbyCardView card }) return;
        if (card.Row.Game == LobbyGame.Race) { PressRaceRow(card.Row); return; }
        if (card.Row.Game != LobbyGame.Chess) return;
        if (TopLevel.GetTopLevel(this) is not MainShellWindow shell) return;
        if (!ReadGates().CanJoin(LobbyGame.Chess)) { _ = shell.OpenUnifiedLoginDialog(); return; }
        GameWindow.OpenChessLobby(card.Row.CanWatch ? "spectate" : "join", card.Row.Key);
    }

    /// <summary>WPF MainWindow.Lobby.cs:254-257 and :283-287: sign-in first, then a malformed room id never leaves.</summary>
    internal void PressRaceRow(LobbyRow row)
    {
        if (row.Game != LobbyGame.Race || !row.CanJoin) return;
        if (!ReadGates().CanJoin(LobbyGame.Race))
        {
            if (TopLevel.GetTopLevel(this) is MainShellWindow shell) _ = shell.OpenUnifiedLoginDialog();
            return;
        }
        if (!RaceJoinLink.IsRoomId(row.Key)) { Serilog.Log.Warning("[Lobby] race join refused: bad room id"); return; }
        try { OpenRace(row.Key!); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "[Lobby] join {Game} failed", row.Game); }
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

/// <summary>A record: value equality is what lets an unchanged row keep its slot and container.</summary>
public sealed record LobbyCardView
{
    public LobbyCardView(LobbyRow row, LobbyGates gates)
    {
        Row = row;
        Title = LobbyPageRules.Title(row, Loc.Get);
        Line = LobbyPageRules.Line(Loc.Get(LobbyPageRules.LabelKey(row.Game)), LobbySummary.For(row, Loc.Get));
        Friend = row.Friend;
        HasButton = LobbyPageRules.HasButton(row);
        Locked = LobbyPageRules.Locked(row, gates);
        ButtonText = Loc.Get(row.CanJoin ? "lobby_btn_join" : "lobby_btn_watch");
        ButtonOpacity = LobbyPageRules.ButtonOpacity(Locked);
    }
    public LobbyRow Row { get; }
    /// <summary>WPF LobbyRowView.Id: stable across polls, for the new-table pop.</summary>
    public string Id => LobbyPageRules.Id(Row);
    // WPF LobbyRowView.cs:27-57,103-139: the game's small feature art and, when they wear something, the host's look.
    public IBrush? ArtBrush => ArtFor(Row.Game) is { } art ? new ImageBrush(art) { Stretch = Stretch.UniformToFill } : null;
    public bool HasLook => !Row.Look.IsEmpty;
    public IBrush? FaceBrush => !HasLook ? null
        : Helpers.ModArt.AvatarPreset(Row.Look.AvatarId) is { } face ? new ImageBrush(face) { Stretch = Stretch.UniformToFill }
        : LeaderboardRow.BuildAvatarBrush(Row.HostName);
    public Bitmap? FrameArt => HasLook ? Helpers.ModArt.ShopFrame(Row.Look.FrameId) : null;
    public double FrameSize => 22 / ShopCosmeticsRegistry.FrameWindowRatio;
    public Thickness FrameMargin => new(-(FrameSize - 22) / 2);
    internal static string ArtPath(LobbyGame game) => game switch
    {
        LobbyGame.Chess => "features/piecebypiece.png",
        LobbyGame.Goon => "features/goon_game_tile.png",
        LobbyGame.Race => "features/race.png",
        _ => "features/lobby_remote.png",
    };
    private static readonly System.Collections.Generic.Dictionary<LobbyGame, Bitmap?> ArtCache = new();
    private static Bitmap? ArtFor(LobbyGame game)
    {
        lock (ArtCache)
        {
            if (!ArtCache.TryGetValue(game, out var art)) ArtCache[game] = art = Helpers.ModArt.TryLoadBuiltIn(ArtPath(game), 96);
            return art;
        }
    }
    public bool DoorAvailable => Row.Game is LobbyGame.Chess or LobbyGame.Race;
    private static IBrush? Theme(string key) =>
        global::Avalonia.Application.Current?.TryFindResource(key, out var value) == true ? value as IBrush : null;
    public IBrush? CardRim => Theme("DepthFloatRim");
    public IBrush? ButtonBevel => Theme("DepthRaisedBevel");
    public IBrush? ButtonSheen => Theme("DepthRaisedSheen");
    public double CardShadowPx => DepthLaw.FloatPx;
    public IBrush CardShadow => SharedCardShadow;
    private static readonly IBrush SharedCardShadow = CreateCardShadow();
    private static IBrush CreateCardShadow()
    {
        var c = Color.FromUInt32(DepthLaw.ShadowColor(NavStripTable.AccentRgb(NavSections.Social) | 0xFF000000, DepthLaw.FloatAlpha));
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new(c, 0), new(Color.FromArgb(0, c.R, c.G, c.B), 1) },
        };
    }
    public string Title { get; }
    public string Line { get; }
    public bool Friend { get; }
    public bool HasButton { get; }
    public bool Locked { get; }
    public string ButtonText { get; }
    public double ButtonOpacity { get; }
}
