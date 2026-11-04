using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LauncherLobbyTests
{
    private sealed class Timer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public bool Running;
        public void Start() => Running = true;
        public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
        public void Fire() { if (Running) Tick?.Invoke(this, EventArgs.Empty); }
    }

    [Fact]
    public async Task SharedPollMergesSourcesAndStopsAfterLastLease()
    {
        var timer = new Timer();
        var calls = 0;
        using var poll = new LobbyPoll(timer, () => "account", () => Array.Empty<Friend>(),
            () => { calls++; return Task.FromResult(new PbpLobbyReply(new[] { new PbpOpenSeat("p_1", "Chess", 0, 0, 0) }, Array.Empty<PbpPlayingGame>(), true)); },
            () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)new[] { new RemoteSeat("r", "Remote", 1, "light", Array.Empty<string>(), false) }, true)),
            () => Task.FromResult(new RaceOpenReply(new[] { new RaceOpenRoom("room", "track", "Race", 2, 1, 1, false, null, false) }, true)));
        using var a = poll.Watch();
        using var b = poll.Watch();
        await poll.RefreshAsync();
        Assert.Equal(3, poll.Snapshot.OpenCount);
        Assert.Equal(new[] { LobbyGame.Chess, LobbyGame.Remote, LobbyGame.Race }, poll.Snapshot.Joinable.Select(r => r.Game).OrderBy(g => g).ToArray());
        a.Dispose();
        Assert.True(timer.Running);
        b.Dispose();
        Assert.False(timer.Running);
        var before = calls;
        timer.Fire();
        Assert.Equal(before, calls);
    }

    [Fact]
    public void VisibilityAndDropdownOwnTheirSubscriptionsAndWatch()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var timer = new Timer();
            var calls = 0;
            var seats = 1;
            using var poll = new LobbyPoll(timer, () => "account", () => Array.Empty<Friend>(),
                () => { calls++; return Task.FromResult(new PbpLobbyReply(Enumerable.Range(0, seats).Select(i => new PbpOpenSeat("p_" + i, "Host", 0, 0, 0)).ToArray(), Array.Empty<PbpPlayingGame>(), true)); },
                () => Task.FromResult(OpenTablesReply.Empty),
                () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), false)),
                () => Task.FromResult(RaceOpenReply.Empty));
            var window = new LauncherWindow { LobbyForTest = poll };
            try
            {
                window.Show();
                Assert.Equal(1, calls);
                Assert.True(window.LobbyPolling);
                var changed = typeof(LobbyPoll).GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                Assert.Single(((Delegate)changed.GetValue(poll)!).GetInvocationList());
                Assert.Equal(TimeSpan.FromSeconds(60), window.LobbyPollInterval);
                window.RefreshLobbyTick();
                Assert.Equal(2, calls);
                Assert.Contains("1", window.FindControl<TextBlock>("LobbyChipText")!.Text);
                NavBadges.Set(NavSections.Social, 0);
                window.PaintLobby(poll.Snapshot);
                Assert.Equal(1, window.FindControl<Button>("LobbyChip")!.Opacity);
                var chip = window.FindControl<Button>("LobbyChip")!;
                chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(window.FindControl<Popup>("LobbyDrop")!.IsOpen);
                Assert.Equal(0.60, chip.Opacity);
                Assert.True(timer.Running);
                Assert.Equal(3, calls);
                timer.Fire();
                Assert.Equal(4, calls);
                window.Hide();
                Assert.False(timer.Running);
                Assert.False(window.LobbyPolling);
                Assert.Null(changed.GetValue(poll));
                window.RefreshLobbyTick();
                Assert.Equal(4, calls);
                Assert.False(window.FindControl<Popup>("LobbyDrop")!.IsOpen);
                seats = 2;
                _ = poll.RefreshAsync();
                Assert.Equal(5, calls);
                Assert.Contains("1", window.FindControl<TextBlock>("LobbyChipText")!.Text);
                window.Show();
                Assert.Equal(6, calls);
                Assert.Single(((Delegate)changed.GetValue(poll)!).GetInvocationList());
                Assert.Contains("2", window.FindControl<TextBlock>("LobbyChipText")!.Text);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });
    }

    [Fact]
    public void UnavailableJoinDestinationIsDisabled()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var window = new LauncherWindow();
            try
            {
                var row = new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Open, Key = "p_1", HostName = "Host" };
                window.PaintLobby(new LobbySnapshot(new[] { row }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true));
                var grid = Assert.IsType<Grid>(window.FindControl<StackPanel>("LobbyDropRows")!.Children[0]);
                Assert.False(Assert.IsType<Button>(grid.Children[1]).IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void DropdownShowsFriendsFirstLimitsRowsAndDisablesUnavailableJoins()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var window = new LauncherWindow();
            try
            {
                var friend = new LobbyRow { Game = LobbyGame.Goon, State = LobbyRowState.Open, Key = "friend", HostName = "Friend", Friend = true };
                var others = Enumerable.Range(0, 7).Select(i => new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Open, Key = "p_" + i, HostName = "Host " + i }).ToArray();
                window.PaintLobby(new LobbySnapshot(others, Array.Empty<LobbyRow>(), new[] { friend }, true));
                Assert.Equal(Loc.GetF("launcher_lobby_open", 8), window.FindControl<TextBlock>("LobbyChipText")!.Text);
                var panel = window.FindControl<StackPanel>("LobbyDropRows")!;
                Assert.Equal(7, panel.Children.Count);
                var first = Assert.IsType<Grid>(panel.Children[0]);
                Assert.Equal("Friend", Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(first.Children[0]).Children[0]).Text);
                Assert.NotNull(Assert.IsType<Image>(Assert.IsType<Border>(first.Children[2]).Child).Source);
                Assert.NotNull(Assert.IsType<Border>(first.Children[3]).Background);
                var lift = Assert.IsType<global::Avalonia.Media.TranslateTransform>(first.RenderTransform);
                CoreSettings.Current.MotionLevel = MotionLevel.Full;
                first.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, first, new Pointer(1, PointerType.Mouse, true), window, default, 0, PointerPointProperties.None, KeyModifiers.None));
                Assert.Equal(-2, lift.GetBaseValue(global::Avalonia.Media.TranslateTransform.YProperty).Value);
                first.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, first, new Pointer(1, PointerType.Mouse, true), window, default, 0, PointerPointProperties.None, KeyModifiers.None));
                Assert.Equal(0, lift.GetBaseValue(global::Avalonia.Media.TranslateTransform.YProperty).Value);
                Assert.NotNull(Assert.IsType<Button>(first.Children[1]).Background);
                window.PaintLobby(LobbySnapshot.Empty);
                Assert.Equal(Loc.Get("lobby_signed_out"), Assert.IsType<TextBlock>(panel.Children[0]).Text);
            }
            finally { window.Close(); }
        });
    }
}
