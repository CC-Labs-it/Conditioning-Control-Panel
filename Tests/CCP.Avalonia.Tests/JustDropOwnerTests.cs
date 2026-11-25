using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class JustDropOwnerTests
{
    // WPF ChaosWebViewHost.cs:1106-1110: a visible, iconic window is already visible.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public Task DeliberateMinimizeSurvivesOwnerHideOrMinimize(bool hide, bool playerFirst) =>
        WithWindows((player, owner, shown) =>
        {
            // Even a hidden native handle must not make an intentionally minimized player pop up.
            NativeWindowOwner.NativeVisibleForTests = _ => false;
            if (playerFirst) player.WindowState = WindowState.Minimized;
            if (hide) owner.Hide();
            else owner.WindowState = WindowState.Minimized;
            if (!playerFirst) player.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Minimized, player.WindowState);
            Assert.False(player.JustDropOwnerLinked);
            Assert.Empty(shown);
        });

    [Fact]
    public Task DeferredRepairShowsOnlyANativelyHiddenPlayerWithoutChangingItsState() =>
        WithWindows((player, owner, shown) =>
        {
            player.WindowState = WindowState.Maximized;
            NativeWindowOwner.NativeVisibleForTests = _ => true;
            owner.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(shown);
            NativeWindowOwner.NativeVisibleForTests = _ => false;
            owner.Show();
            owner.Hide();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(player, Assert.Single(shown));
            Assert.Equal(WindowState.Maximized, player.WindowState);
        });

    [Theory]
    [InlineData("XID", 42, false)]
    [InlineData("NSWindow", 42, false)]
    [InlineData("HWND", 0, false)]
    [InlineData("HWND", 42, true)]
    public Task BackendGateRunsBeforeTheNativeOwnershipOperation(string descriptor, long handle, bool supported) =>
        WithWindows((player, owner, _) =>
        {
            int operations = 0;
            NativeWindowOwner.HandleForTests = _ => new PlatformHandle(new IntPtr(handle), descriptor);
            NativeWindowOwner.SetForTests = (_, _) => { operations++; return true; };
            Assert.Equal(supported, NativeWindowOwner.Set(player, owner));
            Assert.Equal(supported, NativeWindowOwner.Set(player, null));
            Assert.Equal(supported ? 2 : 0, operations);
        });

    [Theory]
    [InlineData(0x18, 0, 1, true)]
    [InlineData(0x18, 1, 1, false)]
    [InlineData(0x18, 0, 0, false)]
    [InlineData(0x18, 0, 2, false)]
    [InlineData(0x17, 0, 1, false)]
    public void OnlyParentClosingHideIsVetoed(uint message, int wParam, int lParam, bool expected) =>
        Assert.Equal(expected, GameWindow.IsOwnerCascadeHide(message, new IntPtr(wParam), new IntPtr(lParam)));

    [Fact]
    public Task CascadeHookUnlinksAndQueuesOneResync() => WithWindows((player, owner, _) =>
    {
        int unlinks = 0, resyncs = 0;
        NativeWindowOwner.SetForTests = (_, target) => { if (target is null) unlinks++; return true; };
        player.RenderVisibilityResyncForTests = () => resyncs++;
        bool handled = false;
        player.VetoOwnerCascade(IntPtr.Zero, 0x18, new IntPtr(1), new IntPtr(1), ref handled);
        Assert.False(handled);
        Assert.True(player.JustDropOwnerLinked);
        Assert.Equal(0, unlinks);
        player.VetoOwnerCascade(IntPtr.Zero, 0x18, IntPtr.Zero, new IntPtr(1), ref handled);
        Assert.True(handled);
        Assert.False(player.JustDropOwnerLinked);
        Assert.Equal(1, unlinks);
        Assert.Equal(0, resyncs);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, resyncs);
    });

    [Theory]
    [InlineData("dtrh", true)]
    [InlineData("piecebypiece", true)]
    [InlineData("breakoutdemo", false)]
    public Task EligibleGameAttachesAndTracksOwner(string id, bool eligible) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            var getHandle = NativeWindowOwner.HandleForTests;
            var set = NativeWindowOwner.SetForTests;
            NativeWindowOwner.HandleForTests = _ => new PlatformHandle(new IntPtr(42), "HWND");
            NativeWindowOwner.SetForTests = (_, _) => true;
            var owner = new Window();
            var player = new GameWindow(GameWindow.Games[id]);
            try
            {
                owner.Show(); player.Show();
                player.AttachEligibleGameOwner(owner);
                Assert.Equal(eligible, player.JustDropOwnerLinked);
                owner.WindowState = WindowState.Minimized;
                Assert.False(player.JustDropOwnerLinked);
                owner.WindowState = WindowState.Normal;
                Assert.Equal(eligible, player.JustDropOwnerLinked);
            }
            finally
            {
                player.Close(); owner.Close(); Dispatcher.UIThread.RunJobs();
                NativeWindowOwner.HandleForTests = getHandle;
                NativeWindowOwner.SetForTests = set;
            }
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData("dtrh")]
    [InlineData("piecebypiece")]
    public Task X11OwnerRestacksOnActivationAndRestoreWithoutMinimizingGame(string id) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            var getHandle = NativeWindowOwner.HandleForTests;
            var restack = GameWindow.RestackGameAboveOwner;
            NativeWindowOwner.HandleForTests = _ => new PlatformHandle(new IntPtr(42), "XID");
            var owner = new Window();
            var player = new GameWindow(GameWindow.Games[id]);
            int calls = 0;
            GameWindow.RestackGameAboveOwner = (game, shell) =>
            {
                Assert.Same(player, game);
                Assert.Same(owner, shell);
                calls++;
                return true;
            };
            try
            {
                owner.Show(); player.Show();
                player.AttachEligibleGameOwner(owner);
                Dispatcher.UIThread.RunJobs();
                Assert.True(calls > 0);
                calls = 0;
                player.Activate(); owner.Activate();
                Dispatcher.UIThread.RunJobs();
                Assert.True(calls > 0);
                owner.WindowState = WindowState.Minimized;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Normal, player.WindowState);
                calls = 0;
                owner.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                Assert.True(calls > 0);
                player.WindowState = WindowState.Minimized;
                calls = 0;
                owner.WindowState = WindowState.Minimized;
                owner.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(WindowState.Minimized, player.WindowState);
                Assert.Equal(0, calls);
                player.Close();
                owner.WindowState = WindowState.Minimized;
                owner.WindowState = WindowState.Normal;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, calls);
            }
            finally
            {
                player.Close(); owner.Close(); Dispatcher.UIThread.RunJobs();
                NativeWindowOwner.HandleForTests = getHandle;
                GameWindow.RestackGameAboveOwner = restack;
            }
            return Task.CompletedTask;
        });

    [Fact]
    public void X11RestackOnlyMovesAGameBelowItsPanel()
    {
        var game = new IntPtr(1);
        var panel = new IntPtr(2);
        var other = new IntPtr(3);
        int moves = 0;
        bool Move() { moves++; return true; }
        // An unrelated foreground app between panel and game must remain below the game.
        Assert.True(X11Overlay.RestackAboveIfBelow(new[] { panel, other, game }, game, panel, Move));
        Assert.Equal(0, moves);
        Assert.True(X11Overlay.RestackAboveIfBelow(new[] { game, other, panel }, game, panel, Move));
        Assert.Equal(1, moves);
        Assert.False(X11Overlay.RestackAboveIfBelow(new[] { game, other }, game, panel, Move));
        Assert.False(X11Overlay.RestackAboveIfBelow(new[] { panel, other }, game, panel, Move));
        Assert.Equal(1, moves);
    }

    [Fact]
    public Task X11WatchCorrectsAnAlreadyActivePanelRaiseAndStopsOnClose() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var getHandle = NativeWindowOwner.HandleForTests;
            var restack = GameWindow.RestackGameAboveOwner;
            NativeWindowOwner.HandleForTests = _ => new PlatformHandle(new IntPtr(42), "XID");
            var owner = new Window();
            var player = new GameWindow(GameWindow.Games[GameWindow.DtrhId]);
            var gameId = new IntPtr(1); var panelId = new IntPtr(2); var otherId = new IntPtr(3);
            var stacking = new[] { panelId, otherId, gameId };
            var corrected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int moves = 0, checks = 0;
            bool raised = false;
            GameWindow.RestackGameAboveOwner = (_, _) =>
            {
                checks++;
                if (!raised) return true;
                return X11Overlay.RestackAboveIfBelow(stacking, gameId, panelId, () =>
                {
                    moves++;
                    stacking = new[] { otherId, panelId, gameId };
                    corrected.TrySetResult(true);
                    return true;
                });
            };
            try
            {
                owner.Show(); player.Show();
                player.AttachEligibleGameOwner(owner);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(0, moves);
                // Change stacking without activation or state events, as for an already-active raise.
                stacking = new[] { gameId, otherId, panelId };
                raised = true;
                Assert.Same(corrected.Task, await Task.WhenAny(corrected.Task, Task.Delay(5000)));
                Assert.Equal(1, moves);
                player.Close();
                Dispatcher.UIThread.RunJobs();
                int afterClose = checks;
                await Task.Delay(600);
                Assert.Equal(afterClose, checks);
            }
            finally
            {
                player.Close(); owner.Close(); Dispatcher.UIThread.RunJobs();
                NativeWindowOwner.HandleForTests = getHandle;
                GameWindow.RestackGameAboveOwner = restack;
            }
        });

    private static Task WithWindows(Action<GameWindow, Window, System.Collections.Generic.List<Window>> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var getHandle = NativeWindowOwner.HandleForTests;
            var set = NativeWindowOwner.SetForTests;
            var visible = NativeWindowOwner.NativeVisibleForTests;
            var show = NativeWindowOwner.ShowForTests;
            var shown = new System.Collections.Generic.List<Window>();
            NativeWindowOwner.HandleForTests = _ => new PlatformHandle(new IntPtr(42), "HWND");
            NativeWindowOwner.SetForTests = (_, _) => true;
            NativeWindowOwner.NativeVisibleForTests = _ => true;
            NativeWindowOwner.ShowForTests = shown.Add;
            var owner = new Window();
            var player = new GameWindow(GameWindow.Games[GameWindow.JustDropId]);
            try
            {
                owner.Show(); player.Show();
                player.AttachJustDropOwner(owner);
                Assert.True(player.JustDropOwnerLinked);
                body(player, owner, shown);
            }
            finally
            {
                player.Close(); owner.Close(); Dispatcher.UIThread.RunJobs();
                NativeWindowOwner.HandleForTests = getHandle;
                NativeWindowOwner.SetForTests = set;
                NativeWindowOwner.NativeVisibleForTests = visible;
                NativeWindowOwner.ShowForTests = show;
            }
            return Task.CompletedTask;
        });
}
