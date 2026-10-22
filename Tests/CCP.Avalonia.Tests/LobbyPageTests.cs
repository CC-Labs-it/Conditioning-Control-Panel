using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.UI;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LobbyPageTests
{
    private static T Named<T>(Control parent, string name) where T : Control => parent.FindControl<T>(name)!;

    [Fact]
    public void EmptyLobbyHasNoFabricatedRowsAndNarrowButtonsSwitchColumns() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        Assert.Empty(Named<ItemsControl>(page, "AvailableSubjectsList").Items);
        Assert.Equal(Loc.Get("lobby_signed_out"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
        page.SetNarrow(true);
        Assert.True(Named<StackPanel>(page, "LobbyTabStrip").IsVisible);
        Named<Button>(page, "TabFriends").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(2, page.SelectedColumn);
        Assert.False(Named<Border>(page, "LobbyOpenColumn").IsVisible);
        Assert.True(Named<Border>(page, "LobbyFriendsSection").IsVisible);
        Assert.Equal(3, Grid.GetColumnSpan(Named<Border>(page, "LobbyFriendsSection")));
        page.SetNarrow(false);
        foreach (var (name, index) in new[] { ("LobbyOpenColumn", 0), ("LobbyPlayingSection", 1), ("LobbyFriendsSection", 2) })
        {
            var column = Named<Border>(page, name);
            Assert.True(column.IsVisible);
            Assert.Equal(index, Grid.GetColumn(column));
            Assert.Equal(1, Grid.GetColumnSpan(column));
        }
        Assert.Equal(0.6, Named<Button>(page, "TabOpen").Opacity);
        Assert.Equal(1, Named<Button>(page, "TabFriends").Opacity);
    });

    [Fact]
    public void EveryLobbyPillHasATemplateAndDrawsAtANonZeroSize() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        var window = new Window { Width = 900, Height = 700, Content = page };
        window.Show();
        try
        {
            page.SetNarrow(true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var names = new[] { "BtnHostChess", "BtnHostGoon", "BtnBecomeASubject", "TabOpen", "TabPlaying", "TabFriends", "BtnEmptyHostChess", "BtnEmptyHostGoon" };
            foreach (var name in names)
            {
                var pill = Named<Button>(page, name);
                Assert.True(pill.Template is not null, name + " has no Template");
                Assert.True(pill.Bounds.Width > 0 && pill.Bounds.Height > 0, name + " has zero size " + pill.Bounds);
            }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void SnapshotPaintsAllThreeListsCountsErrorsAndRemoteGate() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        var open = new LobbyRow { Game = LobbyGame.Remote, Key = "r", HostName = "Remote", RemoteTier = "light" };
        var playing = new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Playing, HostName = "A", OpponentName = "B", InitialMs = 300000 };
        var friend = open with { Key = "friend", Friend = true };
        page.PaintSnapshot(new LobbySnapshot(new[] { open }, new[] { playing }, new[] { friend }, true), LobbyGates.From(true, false, false), true);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyOpenCount").Text);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyPlayingCount").Text);
        Assert.Equal("1", Named<TextBlock>(page, "LobbyFriendsCount").Text);
        Assert.Equal(Loc.GetF("lobby_open_count", 2), Named<TextBlock>(page, "TxtLobbyCount").Text);
        Assert.False(Named<Border>(page, "AvailableSubjectsEmptyPanel").IsVisible);
        Assert.True(Named<Border>(page, "AvailableSubjectsErrorPanel").IsVisible);
        Assert.True(Named<TextBlock>(page, "TxtBecomeASubjectSubtitle").IsVisible);
        Assert.False(page.FindControl<Control>("ChessHostLock")!.IsVisible);
        Assert.Equal(1, Named<Button>(page, "BtnHostChess").Opacity);
        Assert.True(page.FindControl<Control>("GoonHostLock")!.IsVisible);
        Assert.Equal(0.7, Named<Button>(page, "BtnHostGoon").Opacity);
        var row = Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "LobbyPlayingList").Items.Single());
        Assert.Equal("A vs B", row.Title);
        Assert.False(row.HasButton);
        Assert.Contains("5+0", row.Line);
        Assert.True(Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "LobbyFriendsList").Items.Single()).Friend);
    });

    [Fact]
    public void ShowingTheTabAgainRedrawsSignInGateAndLanguage() => Run(() =>
    {
        var page = new AvailableSubjectsTabView();
        page.ReadGates = () => LobbyGates.From(false, false, false);
        page.IsVisible = false;
        page.IsVisible = true;
        Assert.Equal(Loc.Get("lobby_signed_out"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
        page.IsVisible = false;
        page.ReadGates = () => LobbyGates.From(true, false, false);
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            page.IsVisible = true;
            Assert.Equal(Loc.Get("lobby_empty_open"), Named<TextBlock>(page, "TxtLobbyEmpty").Text);
            Assert.True(Named<TextBlock>(page, "TxtBecomeASubjectSubtitle").IsVisible);
            Assert.Equal(Loc.Get("lobby_list_open") + "  0", Named<Button>(page, "TabOpen").Content);
        }
        finally { LocalizationManager.Instance.SetLanguage("en"); }
    });

    [Fact]
    public void SignedOutRemoteHostButtonOpensSignInInsteadOfRemote() => Run(() =>
    {
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
            page.ReadGates = () => LobbyGates.From(false, false, false);
            shell.ShowTab("availablesubjects");
            Named<Button>(page, "BtnBecomeASubject").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(shell.OwnedWindows, w => w is LoginDialog);
            Assert.True(page.IsVisible);
        }
        finally
        {
            foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
            shell.Close();
        }
    });

    [Fact]
    public void RemoteHostButtonChecksGateThenOpensConsentPageWithoutStartingSession() => Run(() =>
    {
        var premium = CoreEntitlement.HasPremiumProvider;
        var free = CoreEntitlement.IsFreeTodayProvider;
        var refusal = CoreEntitlement.ShowDeniedHandler;
        var denied = 0;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var page = Named<AvailableSubjectsTabView>(shell, "AvailableSubjectsTab");
            page.ReadGates = () => LobbyGates.From(true, false, false);
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreEntitlement.ShowDeniedHandler = _ => denied++;
            shell.ShowTab("availablesubjects");
            Named<Button>(page, "BtnBecomeASubject").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, denied);
            Assert.True(page.IsVisible);
            CoreEntitlement.IsFreeTodayProvider = key => key == "remote";
            Named<Button>(page, "BtnBecomeASubject").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Named<RemoteControlTabView>(shell, "RemoteControlTab").IsVisible);
            Assert.False(page.IsVisible);
            Assert.False(Named<CheckBox>(Named<RemoteControlTabView>(shell, "RemoteControlTab"), "ChkRemoteControlEnabled").IsChecked == true);
        }
        finally
        {
            foreach (var child in shell.OwnedWindows.ToArray()) child.Close();
            shell.Close();
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.IsFreeTodayProvider = free;
            CoreEntitlement.ShowDeniedHandler = refusal;
        }
    });

    private sealed class FakeTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public bool Running;
        public event EventHandler? Tick { add { } remove { } }
        public void Start() => Running = true;
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
    }

    [Fact]
    public void VisibleTabLeasesTheLobbyPaintsItsRoundsAndReleasesOnHide() => Run(() =>
    {
        var timer = new FakeTimer();
        IReadOnlyList<RemoteSeat> seats = new[] { new RemoteSeat("u1", "Kitten", 3, "light", Array.Empty<string>(), false) };
        var lobby = new LobbyPoller(() => timer)
        {
            SignedIn = () => true,
            FetchChess = _ => Task.FromResult(PbpLobbyReply.Empty),
            FetchGoon = () => Task.FromResult(OpenTablesReply.Empty),
            FetchRemote = () => Task.FromResult((seats, true)),
        };
        var page = new AvailableSubjectsTabView { ReadLobby = () => lobby, ReadGates = () => LobbyGates.From(true, false, false) };
        try
        {
            page.IsVisible = false;
            page.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.IsWatching);
            Assert.True(timer.Running);
            var row = Assert.IsType<LobbyCardView>(Named<ItemsControl>(page, "AvailableSubjectsList").Items.Single());
            Assert.Equal("Kitten", row.Title);
            Assert.True(row.CanPress);
            Assert.Equal(1, NavBadges.Get(NavSections.Social));
            page.IsVisible = false;
            Assert.False(page.IsWatching);
            Assert.False(timer.Running);
        }
        finally { NavBadges.Set(NavSections.Social, 0); }
    });

    [Fact]
    public void RemoteRowJoinClaimsAndOpensTheSessionChessRowStaysDisabled() => Run(() =>
    {
        var previous = global::ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell;
        string? opened = null, claimed = null;
        global::ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = u => { opened = u; return true; };
        var page = new AvailableSubjectsTabView
        {
            ReadLobby = () => null,
            ReadGates = () => LobbyGates.From(true, false, false),
            ClaimRemote = id => { claimed = id; return Task.FromResult<string?>("http://127.0.0.1:9/s#pin"); },
        };
        var window = new Window { Width = 1200, Height = 700, Content = page };
        window.Show();
        try
        {
            var remote = new LobbyRow { Game = LobbyGame.Remote, Key = "u1", HostName = "Kitten", RemoteTier = "light" };
            var chess = new LobbyRow { Game = LobbyGame.Chess, Key = "p_1", HostName = "Rook", InitialMs = 300000 };
            page.PaintSnapshot(new LobbySnapshot(new[] { remote, chess }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true), LobbyGates.From(true, false, false));
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var buttons = Named<ItemsControl>(page, "AvailableSubjectsList").GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(2, buttons.Count);
            Assert.False(buttons[1].IsEnabled);
            buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("u1", claimed);
            Assert.Equal("http://127.0.0.1:9/s#pin", opened);
        }
        finally
        {
            window.Close();
            global::ConditioningControlPanel.Avalonia.Platform.ExternalOpener.Shell = previous;
        }
    });

    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        body();
        Dispatcher.UIThread.RunJobs();
    });
}
