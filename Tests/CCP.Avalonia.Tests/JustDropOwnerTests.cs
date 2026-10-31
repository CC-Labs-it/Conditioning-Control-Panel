using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

    private static Task WithWindows(Action<GameWindow, Window, System.Collections.Generic.List<Window>> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var set = NativeWindowOwner.SetForTests;
            var visible = NativeWindowOwner.NativeVisibleForTests;
            var show = NativeWindowOwner.ShowForTests;
            var shown = new System.Collections.Generic.List<Window>();
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
                NativeWindowOwner.SetForTests = set;
                NativeWindowOwner.NativeVisibleForTests = visible;
                NativeWindowOwner.ShowForTests = show;
            }
            return Task.CompletedTask;
        });
}
