using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LobbyNavigationTests
{
    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        body();
    });

    private static void Seed(LobbyService lobby)
    {
        lobby.BaseUrl = () => "http://localhost";
        lobby.SignedIn = () => true;
        lobby.FetchChess = () => Task.FromResult(new PbpLobbyReply(
            new[] { new PbpOpenSeat("p_1", "Chess host", 300000, 0, 0) }, Array.Empty<PbpPlayingGame>(), true));
        lobby.FetchGoon = () => Task.FromResult(OpenTablesReply.Empty);
        lobby.FetchRace = () => Task.FromResult(RaceOpenReply.Empty);
        lobby.FetchDirectory = _ => Task.FromResult<IReadOnlyList<RemoteSeat>>(Array.Empty<RemoteSeat>());
        lobby.ReadFriends = () => Array.Empty<ConditioningControlPanel.Services.Friends.Friend>();
    }

    [Fact]
    public void LauncherShowsLiveCountSharesLeaseAndStopsOnHide() => Run(() =>
    {
        NavBadges.ResetForTests();
        var shell = new MainShellWindow();
        Seed(shell.Lobby);
        int calls = 0;
        shell.Lobby.FetchChess = () => { calls++; return Task.FromResult(new PbpLobbyReply(
            Enumerable.Range(0, 7).Select(i => new PbpOpenSeat("p_" + i, "Host " + i, 300000, 0, 0)).ToArray(),
            Array.Empty<PbpPlayingGame>(), true)); };
        LauncherWindow? launcher = null;
        try
        {
            shell.Show();
            launcher = LauncherWindow.OpenFor(shell);
            var chip = launcher.FindControl<Button>("LobbyChip")!;
            var popup = launcher.FindControl<Popup>("LobbyDrop")!;
            var rows = launcher.FindControl<StackPanel>("LobbyDropRows")!;
            Assert.True(calls >= 2);
            Assert.Equal(Loc.GetF("launcher_lobby_open", 7), chip.Content);
            Assert.Equal(1, chip.Opacity);
            var timer = (DispatcherTimer)typeof(LauncherWindow).GetField("_lobbyRefresh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(launcher)!;
            Assert.True(timer.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(60), timer.Interval);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(popup.IsOpen);
            Assert.Equal(2, shell.Lobby.Watchers);
            Assert.Equal(7, rows.Children.Count); // six rows and Open Lobby
            Assert.All(rows.Children.OfType<Grid>(), row => Assert.False(row.Children.OfType<Button>().Single().IsEnabled));
            Assert.Equal(7, NavBadges.Seen(NavSections.Social));
            Assert.Equal(0.60, chip.Opacity, 2);
            popup.IsOpen = false;
            Assert.Equal(1, shell.Lobby.Watchers);
            shell.Lobby.FetchChess = () => Task.FromResult(new PbpLobbyReply(
                Enumerable.Range(0, 8).Select(i => new PbpOpenSeat("p_" + i, "Host", 300000, 0, 0)).ToArray(), Array.Empty<PbpPlayingGame>(), true));
            shell.Lobby.RefreshAsync().GetAwaiter().GetResult();
            Assert.Equal(1, chip.Opacity);
            Assert.Equal(Loc.GetF("launcher_lobby_open", 8), chip.Content);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, shell.Lobby.Watchers);
            launcher.Hide();
            Assert.False(popup.IsOpen);
            Assert.Equal(1, shell.Lobby.Watchers);
            Assert.False(timer.IsEnabled);
            shell.Lobby.SignedIn = () => false;
            shell.Lobby.RefreshAsync().GetAwaiter().GetResult();
            Assert.Equal(Loc.GetF("launcher_lobby_open", 8), chip.Content);
            launcher.Show();
            Assert.Equal(Loc.Get("launcher_lobby"), chip.Content);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Loc.Get("lobby_signed_out"), Assert.IsType<TextBlock>(rows.Children.First()).Text);
        }
        finally { launcher?.Close(); shell.Close(); NavBadges.ResetForTests(); }
    });

    [Fact]
    public void LauncherOpenLobbyClosesDropAndNavigates() => Run(() =>
    {
        var shell = new MainShellWindow();
        Seed(shell.Lobby);
        LauncherWindow? launcher = null;
        try
        {
            launcher = LauncherWindow.OpenFor(shell);
            var chip = launcher.FindControl<Button>("LobbyChip")!;
            var popup = launcher.FindControl<Popup>("LobbyDrop")!;
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(popup.IsOpen);
            var rows = launcher.FindControl<StackPanel>("LobbyDropRows")!;
            Assert.Equal(Loc.Get("launcher_lobby_open_page"), Assert.IsType<Button>(rows.Children.Last()).Content);
            Assert.IsType<Button>(rows.Children.Last()).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            launcher.FinishExit();
            Assert.False(popup.IsOpen);
            Assert.Equal("availablesubjects", shell.CurrentTab);
            Assert.True(shell.IsVisible);
        }
        finally { launcher?.Close(); shell.Close(); NavBadges.ResetForTests(); }
    });

    [Fact]
    public void ShellSharesLeasesPaintsAndStopsOnLeaveHideAndClose() => Run(() =>
    {
        var shell = new MainShellWindow();
        Seed(shell.Lobby);
        try
        {
            shell.Show();
            Assert.Equal(1, shell.Lobby.Watchers);
            Assert.True(shell.Lobby.Polling);
            Assert.Equal(1, NavBadges.Get(NavSections.Social));
            shell.ShowTab("availablesubjects");
            Assert.Equal(2, shell.Lobby.Watchers);
            var page = shell.Named<AvailableSubjectsTabView>("AvailableSubjectsTab")!;
            Assert.Equal("1", page.FindControl<TextBlock>("LobbyOpenCount")!.Text);
            Assert.Equal("Chess host", Assert.IsType<LobbyCardView>(page.FindControl<ItemsControl>("AvailableSubjectsList")!.Items.Single()).Title);
            shell.ShowTab("settings");
            Assert.Equal(1, shell.Lobby.Watchers);
            shell.Hide();
            Assert.Equal(0, shell.Lobby.Watchers);
            Assert.False(shell.Lobby.Polling);
            shell.Show();
            Assert.Equal(1, shell.Lobby.Watchers);
            shell.WindowState = WindowState.Minimized;
            Assert.Equal(0, shell.Lobby.Watchers);
            shell.WindowState = WindowState.Normal;
            Assert.Equal(1, shell.Lobby.Watchers);
        }
        finally { shell.Close(); NavBadges.ResetForTests(); }
        Assert.Equal(0, shell.Lobby.Watchers);
        Assert.False(shell.Lobby.Polling);
    });

    [Fact]
    public void ACompletedRoundRepaintsTheOpenPageAndSocialBadge() => Run(() =>
    {
        var shell = new MainShellWindow();
        Seed(shell.Lobby);
        try
        {
            shell.Show();
            var pending = new TaskCompletionSource<IReadOnlyList<RemoteSeat>>();
            shell.Lobby.FetchChess = () => Task.FromResult(new PbpLobbyReply(
                Array.Empty<PbpOpenSeat>(), Array.Empty<PbpPlayingGame>(), true));
            shell.Lobby.FetchDirectory = _ => pending.Task;
            shell.ShowTab("availablesubjects");
            pending.SetResult(new[]
            {
                new RemoteSeat("one", "One", 1, "light", Array.Empty<string>(), false),
                new RemoteSeat("two", "Two", 1, "light", Array.Empty<string>(), false),
            });
            Dispatcher.UIThread.RunJobs();
            var page = shell.Named<AvailableSubjectsTabView>("AvailableSubjectsTab")!;
            Assert.Equal("2", page.FindControl<TextBlock>("LobbyOpenCount")!.Text);
            Assert.Equal(2, NavBadges.Get(NavSections.Social));
        }
        finally { shell.Close(); NavBadges.ResetForTests(); }
    });

    [Fact]
    public void NavigationCancelsDirectoryEvenWithBadgeLeaseAndRepeatEntryReacquiresTab() => Run(() =>
    {
        var shell = new MainShellWindow();
        Seed(shell.Lobby);
        var pending = new TaskCompletionSource<IReadOnlyList<RemoteSeat>>();
        CancellationToken token = default;
        shell.Lobby.FetchDirectory = ct => { token = ct; return pending.Task; };
        try
        {
            shell.Show();
            Assert.True(token.CanBeCanceled);
            shell.ShowTab("availablesubjects");
            Assert.True(token.IsCancellationRequested);
            Assert.Equal(2, shell.Lobby.Watchers);
            shell.ShowTab("availablesubjects");
            Assert.Equal(2, shell.Lobby.Watchers);
            shell.ShowTab("settings");
            Assert.Equal(1, shell.Lobby.Watchers);
        }
        finally { pending.SetCanceled(); shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public void PollCoalescesAndBacksOffAfterFailureThenResetsOnSuccess() => Run(() =>
    {
        using var lobby = new LobbyService();
        Seed(lobby);
        var pending = new TaskCompletionSource<PbpLobbyReply>();
        int calls = 0;
        lobby.FetchChess = () => { calls++; return pending.Task; };
        lobby.FetchDirectory = _ => Task.FromException<IReadOnlyList<RemoteSeat>>(new HttpRequestException());
        using var a = lobby.Watch();
        using var b = lobby.Watch();
        Assert.Equal(1, calls);
        Assert.True(lobby.Polling);
        pending.SetResult(PbpLobbyReply.Empty);
        Dispatcher.UIThread.RunJobs();
        Assert.False(lobby.LastRoundOk);
        Assert.Equal(TimeSpan.FromSeconds(20), lobby.Interval);
        lobby.FetchChess = () => Task.FromResult(new PbpLobbyReply(Array.Empty<PbpOpenSeat>(), Array.Empty<PbpPlayingGame>(), true));
        lobby.RefreshAsync().GetAwaiter().GetResult();
        Assert.True(lobby.LastRoundOk);
        Assert.Equal(TimeSpan.FromSeconds(10), lobby.Interval);
        a.Dispose();
        Assert.True(lobby.Polling);
        b.Dispose();
        Assert.False(lobby.Polling);
    });

    [Fact]
    public void CanceledDirectoryResultCannotReplaceLastSeatsAndSignedOutMakesNoRequests() => Run(() =>
    {
        using var lobby = new LobbyService();
        Seed(lobby);
        lobby.FetchDirectory = _ => Task.FromResult<IReadOnlyList<RemoteSeat>>(
            new[] { new RemoteSeat("old", "Old", 1, "light", Array.Empty<string>(), false) });
        lobby.RefreshAsync().GetAwaiter().GetResult();
        var pending = new TaskCompletionSource<IReadOnlyList<RemoteSeat>>();
        lobby.FetchDirectory = _ => pending.Task;
        var round = lobby.RefreshAsync();
        lobby.CancelDirectory();
        pending.SetResult(new[] { new RemoteSeat("new", "New", 1, "light", Array.Empty<string>(), false) });
        Dispatcher.UIThread.RunJobs();
        Assert.True(round.IsCompleted);
        Assert.Contains(lobby.Snapshot.Open, r => r.Key == "old");
        Assert.DoesNotContain(lobby.Snapshot.Open, r => r.Key == "new");
        int calls = 0;
        lobby.SignedIn = () => false;
        lobby.FetchChess = () => { calls++; return Task.FromResult(PbpLobbyReply.Empty); };
        lobby.RefreshAsync().GetAwaiter().GetResult();
        Assert.Equal(0, calls);
        Assert.False(lobby.Snapshot.SignedIn);
        Assert.True(lobby.Snapshot.IsEmpty);
    });

    [Fact]
    public void SignedOutRoundClearsSnapshotWithoutCallingTheFeeds() => Run(() =>
    {
        using var lobby = new LobbyService();
        Seed(lobby);
        lobby.RefreshAsync().GetAwaiter().GetResult();
        Assert.False(lobby.Snapshot.IsEmpty);
        int calls = 0;
        lobby.FetchChess = () => { calls++; return Task.FromResult(PbpLobbyReply.Empty); };
        lobby.SignedIn = () => false;
        lobby.RefreshAsync().GetAwaiter().GetResult();
        Assert.Equal(0, calls);
        Assert.False(lobby.Snapshot.SignedIn);
        Assert.True(lobby.Snapshot.IsEmpty);
    });

    [Fact]
    public void DirectoryFetchUsesAccountHeadersAndPaintsRemoteEntries() => Run(() =>
    {
        using var lobby = new LobbyService();
        Seed(lobby);
        var handler = new DirectoryHandler();
        using var actual = new LobbyService
        {
            SignedIn = () => true,
            Identity = () => ("caller", "token"),
            BaseUrl = () => "http://localhost",
            DirectoryHttp = new HttpClient(handler),
            FetchChess = lobby.FetchChess, FetchGoon = lobby.FetchGoon,
            FetchRace = lobby.FetchRace, ReadFriends = lobby.ReadFriends,
        };
        actual.RefreshAsync().GetAwaiter().GetResult();
        Assert.True(handler.Called);
        var row = Assert.Single(actual.Snapshot.Open, r => r.Game == LobbyGame.Remote);
        Assert.Equal("Subject", row.HostName);
        Assert.Equal("u1", row.Key);
        Assert.Equal("light", row.RemoteTier);
        Assert.Equal("tag", Assert.Single(row.Tags));
    });

    private sealed class DirectoryHandler : HttpMessageHandler
    {
        internal bool Called;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/v2/directory/list", request.RequestUri!.AbsolutePath);
            Assert.Equal("caller", request.Headers.GetValues("X-Caller-Unified-Id").Single());
            Assert.Equal("token", request.Headers.GetValues("X-Auth-Token").Single());
            Called = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"entries\":[{\"unified_id\":\"u1\",\"display_name\":\"Subject\",\"tags\":[\"tag\"]}]}") });
        }
    }
}
