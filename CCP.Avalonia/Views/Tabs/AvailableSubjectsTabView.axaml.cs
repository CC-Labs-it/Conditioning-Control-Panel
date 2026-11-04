using System;
using System.Globalization;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>WPF's Lobby frame and narrow-column switcher. The old sample subject roster is gone.</summary>
public partial class AvailableSubjectsTabView : UserControl
{
    internal bool IsNarrow { get; private set; }
    internal int SelectedColumn { get; private set; }
    internal Func<LobbyGates> ReadGates = () => LobbyGates.From(Platform.FriendsHead.Identity() != null,
        TierGate.HasPremium, TierGate.RequiresPremium(Loc.Get("tab_remote_control"), "remote").Allowed);
    internal readonly LobbyService Discovery = new(new Platform.FriendsHead.Timer());
    private IDisposable? _lobbyLease;
    private Visual[] _lobbyAncestors = Array.Empty<Visual>();
    private LobbySnapshot _snapshot = LobbySnapshot.Empty;

    public AvailableSubjectsTabView()
    {
        InitializeComponent();
        Discovery.ReadFriends = () => Platform.FriendsHead.Service?.Snapshot.Friends ?? Array.Empty<ConditioningControlPanel.Services.Friends.Friend>();
        Discovery.Changed += snapshot => PaintSnapshot(snapshot, ReadGates(), snapshot.SignedIn && !Discovery.LastRoundOk);
        Repaint();
        InitializeSubjectsMotion();
        // WPF MainWindow.Lobby.cs:82-96: only the open Lobby owns the page lease.
        // WPF observes effective visibility (AvailableSubjectsTabView.xaml.cs:66; MainWindow.Lobby.cs:47).
        PropertyChanged += OnLobbyVisibilityChanged;
        SizeChanged += (_, e) => SetNarrow(LobbyPageRules.IsNarrowWidth(e.NewSize.Width));
        // The shell hides tabs with IsVisible, so Loaded fires once; WPF redraws on every open
        // (MainWindow.Lobby.cs:84), which picks up sign-in, Remote gate and language changes.
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && IsVisible) Repaint(); };
        Loaded += (_, _) =>
        {
            Repaint();
            _lobbyAncestors = this.GetVisualAncestors().ToArray();
            foreach (var ancestor in _lobbyAncestors) ancestor.PropertyChanged += OnLobbyVisibilityChanged;
            SyncDiscovery();
            SubjectsAmbientFx.StartLayers(new AmbientFxConfig
                { Layers = AmbientFxLayers.FogDrift, FogPuffs = 2, Intensity = 0.40 });
        };
        Unloaded += (_, _) =>
        {
            foreach (var ancestor in _lobbyAncestors) ancestor.PropertyChanged -= OnLobbyVisibilityChanged;
            _lobbyAncestors = Array.Empty<Visual>();
            _lobbyLease?.Dispose(); _lobbyLease = null;
            SubjectsAmbientFx.Stop();
        };
    }

    private void OnLobbyVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty) SyncDiscovery();
    }

    private void SyncDiscovery()
    {
        // Read local values because an ancestor's notification can precede propagation to children.
        if (IsLoaded && IsVisible && _lobbyAncestors.All(ancestor => ancestor.IsVisible)) _lobbyLease ??= Discovery.Watch();
        else { _lobbyLease?.Dispose(); _lobbyLease = null; }
    }

    private void Repaint()
    {
        var gates = ReadGates();
        PaintSnapshot(_snapshot with { SignedIn = gates.SignedIn }, gates);
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
        // Chess/Goon host and non-Remote row doors still wait for their game hosts.
    }

    // WPF MainWindow.Lobby.cs:243-271 and MainWindow.RemoteControl.cs:499-512.
    internal Func<string, bool> OpenClaimUrl = url => Platform.ExternalOpener.Open(url);
    private async void BtnLobbyRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not LobbyCardView view) return;
        var row = view.Row;
        if (row.Game != LobbyGame.Remote || !row.CanJoin || string.IsNullOrEmpty(row.Key)) return;
        var gates = ReadGates();
        if (!gates.SignedIn || !gates.CanJoin(row.Game))
        {
            if (TopLevel.GetTopLevel(this) is MainShellWindow shell) _ = shell.OpenUnifiedLoginDialog();
            return;
        }
        button.SetCurrentValue(IsEnabledProperty, false);
        try
        {
            var url = await Discovery.ClaimRemoteAsync(row.Key);
            if (!string.IsNullOrEmpty(url)) OpenClaimUrl(url);
        }
        catch { Serilog.Log.Warning("[Lobby] Remote claim failed"); }
        finally { button.SetCurrentValue(IsEnabledProperty, view.RemoteJoinEnabled); }
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
        Row = row;
        Art = ArtFor(row.Game);
        Title = LobbyPageRules.Title(row, Loc.Get);
        Line = LobbyPageRules.Line(Loc.Get(LobbyPageRules.LabelKey(row.Game)), LobbySummary.For(row, Loc.Get));
        Friend = row.Friend;
        HasButton = LobbyPageRules.HasButton(row);
        Locked = LobbyPageRules.Locked(row, gates);
        ButtonText = Loc.Get(row.CanJoin ? "lobby_btn_join" : "lobby_btn_watch");
        ButtonOpacity = LobbyPageRules.ButtonOpacity(Locked);
    }
    // WPF Views/Tabs/LobbyRowView.cs:87-113: one cached, text-free feature thumbnail per game.
    private static readonly Dictionary<LobbyGame, Bitmap?> ArtCache = new();
    internal static string ArtPath(LobbyGame game) => game switch
    {
        LobbyGame.Chess => "piecebypiece.png", LobbyGame.Goon => "goon_game_tile.png",
        LobbyGame.Race => "race.png", _ => "lobby_remote.png"
    };
    private static Bitmap? ArtFor(LobbyGame game)
    {
        if (ArtCache.TryGetValue(game, out var art)) return art;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/" + ArtPath(game)));
            art = Bitmap.DecodeToWidth(stream, 96);
        }
        catch { art = null; }
        return ArtCache[game] = art;
    }
    public IImage? Art { get; }
    // WPF LobbyRowView.cs:44-57 and SectionTabStrip.Depth.cs:52: Social float band.
    public IBrush CardShadow => SharedCardShadow;
    private static readonly IBrush SharedCardShadow = MakeCardShadow();
    private static IBrush MakeCardShadow()
    {
        var c = Color.FromUInt32(DepthLaw.ShadowColor(NavStripPaint.Accent(NavSections.Social), DepthLaw.FloatAlpha));
        return new LinearGradientBrush { StartPoint = RelativePoint.TopLeft, EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(c, 0), new(Color.FromArgb(0, c.R, c.G, c.B), 1) } };
    }
    public LobbyRow Row { get; }
    public bool RemoteJoinEnabled => Row.Game == LobbyGame.Remote && Row.CanJoin;
    public string Title { get; }
    public string Line { get; }
    public bool Friend { get; }
    public bool HasButton { get; }
    public bool Locked { get; }
    public string ButtonText { get; }
    public double ButtonOpacity { get; }
}
