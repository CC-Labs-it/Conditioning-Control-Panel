using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF LauncherWindow.Lobby.cs: the chip, its dropdown and the shared feed.</summary>
public sealed class LauncherLobbyTests
{
    private sealed class Timer : IUiTimer
    {
        public TimeSpan Interval { get; set; }
        public event EventHandler? Tick;
        public bool Running;
        public void Start() => Running = true; public void Stop() => Running = false;
        public void OnUiThread(Action action) => action();
        public void Fire() { if (Running) Tick?.Invoke(this, EventArgs.Empty); }
    }

    private static LobbyPoll Poll(Timer timer, Func<PbpLobbyReply> chess) =>
        new(timer, () => true, () => Array.Empty<Friend>(), () => Task.FromResult(chess()), () => Task.FromResult(OpenTablesReply.Empty),
            () => Task.FromResult(((IReadOnlyList<RemoteSeat>)Array.Empty<RemoteSeat>(), false)), account: () => "account");

    private static PbpLobbyReply Seats(int n) =>
        new(Enumerable.Range(0, n).Select(i => new PbpOpenSeat("p_" + i, "Host", 0, 0, 0)).ToArray(), Array.Empty<PbpPlayingGame>(), true);

    private static void Ui(Action<LauncherWindow> body, LobbyPoll? poll = null) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new LauncherWindow { LobbyForTest = poll };
        try { body(window); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); NavBadges.ResetForTests(); }
    });

    private static Grid Row(LauncherWindow w, int i = 0) => Assert.IsType<Grid>(w.FindControl<StackPanel>("LobbyDropRows")!.Children[i]);

    [Fact]
    public void VisibilityAndDropdownOwnTheirSubscriptionsAndWatch()
    {
        var timer = new Timer();
        var (calls, seats) = (0, 1);
        using var poll = Poll(timer, () => { calls++; return Seats(seats); });
        Ui(window =>
        {
            window.Show();
            Assert.True(calls == 1 && window.LobbyPolling && window.LobbyPollInterval == TimeSpan.FromSeconds(60));
            var changed = typeof(LobbyPoll).GetField("Changed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Assert.Single(((Delegate)changed.GetValue(poll)!).GetInvocationList());
            window.RefreshLobbyTick();
            Assert.Equal(2, calls);
            NavBadges.Set(NavSections.Social, 0);
            window.PaintLobby(poll.Snapshot);
            var chip = window.FindControl<Button>("LobbyChip")!;
            Assert.Equal(1, chip.Opacity);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.FindControl<Popup>("LobbyDrop")!.IsOpen);
            Assert.Equal(0.60, chip.Opacity); // seen: the chip dims until more open
            Assert.True(timer.Running && calls == 3); // the dropdown's 10 s lease
            timer.Fire();
            Assert.Equal(4, calls);
            window.Hide();
            Assert.False(timer.Running || window.LobbyPolling || window.FindControl<Popup>("LobbyDrop")!.IsOpen);
            Assert.Null(changed.GetValue(poll));
            window.RefreshLobbyTick();
            seats = 2;
            _ = poll.RefreshAsync();
            Assert.Equal(5, calls);
            Assert.Contains("1", window.FindControl<TextBlock>("LobbyChipText")!.Text);
            window.Show();
            Assert.Equal(6, calls);
            Assert.Contains("2", window.FindControl<TextBlock>("LobbyChipText")!.Text);
        }, poll);
    }

    [Fact]
    public void GoonJoinIsOffAndRemoteJoinClaimsAndOpensTheSession() => Ui(window =>
    {
        var settings = CoreSettings.Current;
        var (oldId, oldOffline, oldSecret) = (settings.UnifiedId, settings.OfflineMode, CoreSecrets.RetrieveProvider);
        try
        {
            settings.UnifiedId = "account"; settings.OfflineMode = false;
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "token" : null;
            var (claims, opened) = (new List<string>(), new List<string>());
            string? answer = null;
            window.ClaimRemote = key => { claims.Add(key); return Task.FromResult(answer); };
            window.OpenSession = url => { opened.Add(url); return true; };
            Button Join(LobbyGame game, string key)
            {
                var row = new LobbyRow { Game = game, State = LobbyRowState.Open, Key = key, HostName = "Host" };
                window.PaintLobby(new LobbySnapshot(new[] { row }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true));
                return Assert.IsType<Button>(Row(window).Children[1]);
            }
            Assert.False(Join(LobbyGame.Goon, "goon").IsEnabled);
            var remote = Join(LobbyGame.Remote, "subject");
            void Click() { remote.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
            Click();
            Assert.Equal(new[] { "subject" }, claims);
            Assert.Empty(opened); // a lost race (409) or a refusal opens nothing
            answer = "https://remote.example/s#pin";
            Click();
            Assert.Equal(new[] { answer }, opened);
        }
        finally { (settings.UnifiedId, settings.OfflineMode, CoreSecrets.RetrieveProvider) = (oldId, oldOffline, oldSecret); }
    });

    [Fact]
    public void DropdownShowsSixFriendFirstRowsAndHoverLiftsLikeWpf() => Ui(window =>
    {
        var friend = new LobbyRow { Game = LobbyGame.Goon, State = LobbyRowState.Open, Key = "friend", HostName = "Friend", Friend = true };
        var others = Enumerable.Range(0, 7).Select(i => new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Open, Key = "p_" + i, HostName = "Host " + i }).ToArray();
        window.PaintLobby(new LobbySnapshot(others, Array.Empty<LobbyRow>(), new[] { friend }, true));
        Assert.Equal(Loc.GetF("launcher_lobby_open", 8), window.FindControl<TextBlock>("LobbyChipText")!.Text);
        Assert.Equal(7, window.FindControl<StackPanel>("LobbyDropRows")!.Children.Count); // six rows and Open lobby
        var first = Row(window);
        Assert.Equal("Friend", Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(first.Children[0]).Children[0]).Text);
        Assert.NotNull(Assert.IsType<Image>(Assert.IsType<Border>(first.Children[2]).Child).Source);
        Assert.NotNull(Assert.IsType<Border>(first.Children[3]).Background);
        var lift = Assert.IsType<ScaleTransform>(first.RenderTransform);
        void Hover(RoutedEvent e) => first.RaiseEvent(new PointerEventArgs(e, first, new Pointer(1, PointerType.Mouse, true), window, default, 0, PointerPointProperties.None, KeyModifiers.None));
        var oldMotion = CoreSettings.Current.MotionLevel;
        try
        {
            // WPF MotionFx.HoverLift: with transitions off it snaps straight to 1.02 and back.
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            Hover(InputElement.PointerEnteredEvent);
            Assert.Null(lift.Transitions);
            Assert.Equal((1.02, 1.02), (lift.ScaleX, lift.ScaleY));
            Hover(InputElement.PointerExitedEvent);
            Assert.Equal((1.0, 1.0), (lift.ScaleX, lift.ScaleY));
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            Hover(InputElement.PointerEnteredEvent);
            Assert.Equal(AmbientFxCanvas.Env.AllowTransitions, lift.Transitions != null);
            Assert.Equal(1.02, lift.GetBaseValue(ScaleTransform.ScaleXProperty).Value);
        }
        finally { CoreSettings.Current.MotionLevel = oldMotion; }
        window.PaintLobby(LobbySnapshot.Empty);
        Assert.Equal(Loc.Get("lobby_signed_out"), Assert.IsType<TextBlock>(window.FindControl<StackPanel>("LobbyDropRows")!.Children[0]).Text);
    });
}
