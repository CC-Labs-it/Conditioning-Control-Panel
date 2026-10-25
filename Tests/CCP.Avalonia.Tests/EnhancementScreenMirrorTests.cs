using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EnhancementPlayerWindow.xaml.cs:2001-2553: page fullscreen clones displays when Dual Monitor is enabled
/// and multiple screens are active, and leaving fullscreen puts extend back.</summary>
public sealed class EnhancementScreenMirrorTests
{
    private sealed class FakeTopology : IDisplayTopology
    {
        public int ActiveScreenCount => 2;
        public List<bool> Calls { get; } = new();
        public int Result { get; set; }
        public Action<bool>? OnApply { get; set; }
        public int Apply(bool clone) { Calls.Add(clone); OnApply?.Invoke(clone); return Result; }
    }

    private static Screen MakeScreen(int x, bool primary)
    {
        // Same Avalonia 12.1 internals as MultiMonitorResultTests.MakeScreen.
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(x + 1), "headless-test") })!;
        var bounds = new PixelRect(x, 0, 1920, 1080);
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, primary);
        return screen;
    }

    private static Task Run(bool dualMonitor, Action<EnhancementPlayerWindow, WebHost, FakeTopology, DisplayMirror> check) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .With(AppFonts.Options(systemEmoji: false))
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var (offline, dual, target) = (s.OfflineMode, s.DualMonitorEnabled, s.GlobalTargetMonitor);
            var oldQuery = ScreenList.QueryOverride;
            s.OfflineMode = false;
            s.DualMonitorEnabled = dualMonitor;
            s.GlobalTargetMonitor = MonitorTarget.FollowGlobal;
            var screens = new[] { MakeScreen(0, true), MakeScreen(1920, false) };
            ScreenList.QueryOverride = _ => screens;
            var topo = new FakeTopology();
            var mirror = new DisplayMirror(topo);
            ScreenMirrorHead.Override = mirror;
            var shell = new EnhancementPlayerWindow(null, null);
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                check(shell, shell.FindControl<WebHost>("VideoBrowser")!, topo, mirror);
            }
            finally
            {
                shell.Close();
                ScreenMirrorHead.Override = null;
                ScreenList.QueryOverride = oldQuery;
                (s.OfflineMode, s.DualMonitorEnabled, s.GlobalTargetMonitor) = (offline, dual, target);
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });

    private static Window? Fullscreen(EnhancementPlayerWindow player) =>
        typeof(EnhancementPlayerWindow).GetProperty("VideoFullscreenWindow",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(player) as Window;

    [Theory]
    [InlineData("page")]
    [InlineData("escape")]
    [InlineData("f11")]
    [InlineData("outside")]
    [InlineData("player")]
    [InlineData("panic")]
    [InlineData("audio")]
    public Task EveryExitRestoresExtendAndReturnsTheSameHost(string exit) => Run(true, (player, host, topo, mirror) =>
    {
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        var window = Fullscreen(player);
        Assert.NotNull(window);
        Assert.Same(host, window.Content);
        Assert.True(mirror.IsMirroring);
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage); // duplicate signal must not clone twice
        Assert.Equal(new[] { true }, topo.Calls);
        switch (exit)
        {
            case "audio":
                typeof(EnhancementPlayerWindow).GetMethod("ShowMediaPaneFor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(player, new object[] { "audio" });
                break;
            case "page": host.OnWebMessage(MainShellWindow.BrowserFullscreenOffMessage); break;
            case "escape":
            case "f11": window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent,
                Key = exit == "escape" ? Key.Escape : Key.F11 }); break;
            case "outside": window.Close(); break;
            case "player": player.Close(); break;
            case "panic":
                Assert.Single(PanicSurfaces.All, s => s.Id == "deeper-player-fullscreen").Stop(null);
                break;
        }
        Assert.Null(Fullscreen(player));
        Assert.False(mirror.IsMirroring);
        Assert.Equal(new[] { true, false }, topo.Calls);
        Assert.Contains(host, ((Grid)player.FindControl<Border>("VideoPane")!.Child!).Children);
    });

    [Fact]
    public Task NavigationKeepsTheHostAndTheClone() => Run(true, (player, host, topo, mirror) =>
    {
        // WPF :1842-1853 (BUG-J9PPPJT274): a video rolling into the next page stays fullscreen.
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        var window = Fullscreen(player);
        host.OnNavigationCompleted(new Uri("http://127.0.0.1:9/video"));
        Assert.Same(window, Fullscreen(player));
        Assert.Same(host, window!.Content);
        Assert.True(mirror.IsMirroring);
        Assert.Equal(new[] { true }, topo.Calls);
    });

    [Fact]
    public Task HostDropsTopmostWhileAnotherWindowHasFocus() => Run(true, (player, host, topo, mirror) =>
    {
        // WPF :2163-2182 (#905): Topmost is rented, so a dialog raised over the video can surface.
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        var window = Fullscreen(player)!;
        Assert.True(window.Topmost);
        // Headless never moves activation between windows; raise it the way the platform does.
        static void Raise(Window w, string name) => typeof(WindowBase).GetMethod(name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(w, null);
        Raise(window, "HandleDeactivated");
        Assert.False(window.Topmost);
        Raise(window, "HandleActivated");
        Assert.True(window.Topmost);
    });

    [Fact]
    public Task SingleScreenSelectionStillFullscreensWithoutCloning() => Run(false, (player, host, topo, mirror) =>
    {
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.NotNull(Fullscreen(player));
        host.OnWebMessage(MainShellWindow.BrowserExitFullscreenMessage);
        Assert.Null(Fullscreen(player));
        Assert.Empty(topo.Calls);
    });
    [Fact]
    public Task CloneFailureStillAllowsFullscreenAndNeverRestoresAnUnchangedTopology() => Run(true, (player, host, topo, mirror) =>
    {
        topo.Result = 50;
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.NotNull(Fullscreen(player));
        Assert.False(mirror.IsMirroring);
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOffMessage);
        Assert.Equal(new[] { true }, topo.Calls);
    });

    [Fact]
    public Task OnePhysicalScreenNeverRequestsClone() => Run(true, (player, host, topo, mirror) =>
    {
        ScreenList.QueryOverride = _ => new[] { MakeScreen(0, true) };
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.NotNull(Fullscreen(player));
        Assert.Empty(topo.Calls);
    });

    [Fact]
    public Task FullscreenLossDuringTopologyTransitionDoesNotCancelForcedHost() => Run(true, (player, host, topo, mirror) =>
    {
        // Proves only a signal delivered DURING the transition, as WPF's unsubscribe does (:2094/:2292);
        // a real web message is asynchronous and may land after it, which headless does not model.

        topo.OnApply = clone =>
        {
            if (clone) host.OnWebMessage(MainShellWindow.BrowserFullscreenOffMessage);
        };
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.NotNull(Fullscreen(player));
        Assert.True(mirror.IsMirroring);
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOffMessage);
        Assert.Null(Fullscreen(player));
        Assert.Equal(new[] { true, false }, topo.Calls);
    });

}
