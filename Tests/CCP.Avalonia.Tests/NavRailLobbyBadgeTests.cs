using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class NavRailLobbyBadgeTests
{
    private sealed class Timer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public bool Running;
        public int Starts, Stops;
        public void Start() { Running = true; Starts++; }
        public void Stop() { Running = false; Stops++; }
        public void OnUiThread(Action action) => action();
        public void Fire() { if (Running) Tick?.Invoke(this, EventArgs.Empty); }
    }

    [Fact]
    public void BadgeAndLeaseFollowPanelAndShareThePoll()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var timer = new Timer();
            var seats = 3;
            using var poll = new LobbyPoll(timer, () => true, () => Array.Empty<Friend>(),
                () => Task.FromResult(new PbpLobbyReply(Enumerable.Range(0, seats).Select(i => new PbpOpenSeat("p_" + i, "Host", 0, 0, 0)).ToArray(), Array.Empty<PbpPlayingGame>(), true)),
                () => Task.FromResult(OpenTablesReply.Empty),
                () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), false)), account: () => "account");
            NavBadges.ResetForTests();
            var shell = new MainShellWindow { LobbyBadgeForTest = poll };
            try
            {
                shell.Show(); Dispatcher.UIThread.RunJobs();
                var social = shell.NavSectionRowsForTests.Single(r => r.Section == NavSections.Social);
                Assert.True(social.Badge!.IsVisible);
                Assert.Equal("3", social.BadgeText!.Text);
                Assert.Equal(1, timer.Starts);
                seats = 0; timer.Fire(); Dispatcher.UIThread.RunJobs();
                Assert.False(social.Badge.IsVisible);
                // The launcher dropdown uses the same poll and does not start another timer.
                var launcher = new LauncherWindow { LobbyForTest = poll };
                try
                {
                    launcher.Show();
                    launcher.FindControl<Button>("LobbyChip")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(launcher.FindControl<Popup>("LobbyDrop")!.IsOpen);
                    Assert.Equal(1, timer.Starts);
                    launcher.Hide();
                    Assert.True(timer.Running);
                }
                finally { launcher.Close(); }
                shell.WindowState = WindowState.Minimized;
                Assert.False(timer.Running);
                shell.WindowState = WindowState.Normal;
                Assert.True(timer.Running);
                shell.Hide();
                Assert.False(timer.Running);
                shell.Show();
                Assert.True(timer.Running);
                shell.Close();
                Assert.False(timer.Running);
                NavBadges.Set(NavSections.Social, 7);
                seats = 3; poll.RefreshAsync().GetAwaiter().GetResult();
                Assert.Equal(7, NavBadges.Get(NavSections.Social));
            }
            finally { shell.Close(); NavBadges.ResetForTests(); Dispatcher.UIThread.RunJobs(); }
        });
    }
}
