using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Browser.cs:2816-2848, 2995-3136: page fullscreen from the embedded Home browser moves the
/// page into a borderless topmost window, runs it at 100 % zoom, clones the screens when "Show content on" covers
/// more than one, and every exit (page, Esc, close, panic, engine stop) puts all of it back.</summary>
public sealed class BrowserEmbeddedFullscreenTests
{
    private sealed class FakeTopology : IDisplayTopology
    {
        public int ActiveScreenCount => 2;
        public List<bool> Calls { get; } = new();
        public int Apply(bool clone) { Calls.Add(clone); return 0; }
    }

    private static Screen MakeScreen(int x, bool primary)
    {
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(x + 1), "headless-test") })!;
        var bounds = new PixelRect(x, 0, 1920, 1080);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, primary);
        return screen;
    }

    private static Task WithShell(bool dualMonitor, Action<MainShellWindow, SettingsTabView, WebHost, FakeTopology> check) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .With(AppFonts.Options(systemEmoji: false))
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var old = (s.OfflineMode, s.DualMonitorEnabled, s.GlobalTargetMonitor, ScreenList.QueryOverride, ScreenMirrorHead.Override);
            s.OfflineMode = false;
            s.DualMonitorEnabled = dualMonitor;
            s.GlobalTargetMonitor = ConditioningControlPanel.Services.UI.MonitorTarget.FollowGlobal;
            var screens = new[] { MakeScreen(0, true), MakeScreen(1920, false) };
            ScreenList.QueryOverride = _ => screens;
            var topo = new FakeTopology();
            ScreenMirrorHead.Override = new DisplayMirror(topo);
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                var page = shell.Named<SettingsTabView>("SettingsTab")!;
                var host = page.FindControl<WebHost>("BrowserWebHost")!;
                host.ScriptInvoker = _ => Task.FromResult<string?>(null);
                host.NavigationInvoker = _ => true;
                host.Navigate(new Uri("https://127.0.0.1/video/example-123.html"));
                page.SyncBrowserPrompt();
                check(shell, page, host, topo);
            }
            finally
            {
                shell.Close();
                Dispatcher.UIThread.RunJobs();
                (s.OfflineMode, s.DualMonitorEnabled, s.GlobalTargetMonitor, ScreenList.QueryOverride, ScreenMirrorHead.Override) = old;
            }
            return Task.CompletedTask;
        });

    private static void AssertBackInCard(MainShellWindow shell, SettingsTabView page, WebHost host, Window window)
    {
        Assert.Null(shell.BrowserTakeoverWindow);
        Assert.Contains(host, page.BrowserContainer.Children);
        Assert.Null(window.Content);
        Assert.False(window.IsVisible);
        Assert.False(window.Topmost);
        Assert.True(host.IsVisible);
        Assert.False(page.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
    }

    [Fact]
    public Task EmbeddedPageFullscreenTakesTheScreenAtFullZoomAndPageExitReturnsIt() => WithShell(false, (shell, page, host, topo) =>
    {
        host.SetBrowserZoom(0.5);
        host.OnNativeFullscreenEntered();
        var window = shell.BrowserTakeoverWindow!;
        Assert.Null(shell.BrowserPopoutWindow);
        Assert.Same(host, window.Content);
        Assert.True(window.IsVisible);
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.Equal(WindowDecorations.None, window.WindowDecorations);
        Assert.True(window.Topmost);
        Assert.False(window.ShowInTaskbar);
        Assert.DoesNotContain(host, page.BrowserContainer.Children);
        Assert.True(page.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
        Assert.Equal(1.0, host.BrowserZoom);
        Assert.Empty(topo.Calls);   // one screen picked: never cloned

        // The pop-out button and the card prompt focus the fullscreen window instead of popping out.
        page.FindControl<Button>("BtnPopOutBrowser")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Null(shell.BrowserPopoutWindow);
        Assert.Same(window, shell.BrowserTakeoverWindow);

        host.OnWebMessage("ccp_browser_fullscreen_off");
        AssertBackInCard(shell, page, host, window);
        Assert.Equal(0.5, host.BrowserZoom);
    });

    [Fact]
    public Task EscapeAndClosingTheWindowBothReturnThePage() => WithShell(false, (shell, page, host, _) =>
    {
        host.OnNativeFullscreenEntered();
        var window = shell.BrowserTakeoverWindow!;
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        AssertBackInCard(shell, page, host, window);

        host.OnNativeFullscreenEntered();
        var second = shell.BrowserTakeoverWindow!;
        Assert.NotSame(window, second);
        second.Close();
        AssertBackInCard(shell, page, host, second);
    });

    [Fact]
    public Task MirrorsOnlyWhenTheGlobalPickCoversMoreThanOneScreen() => WithShell(true, (shell, page, host, topo) =>
    {
        host.OnNativeFullscreenEntered();
        var window = shell.BrowserTakeoverWindow!;
        Assert.Equal(new[] { true }, topo.Calls);
        host.OnNativeFullscreenExited();
        Assert.Equal(new[] { true, false }, topo.Calls);
        AssertBackInCard(shell, page, host, window);
    });

    [Fact]
    public Task PopoutFullscreenAlsoSetsFullZoomAndMirrors() => WithShell(true, (shell, page, host, topo) =>
    {
        host.SetBrowserZoom(0.75);
        page.FindControl<Button>("BtnPopOutBrowser")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var popout = shell.BrowserPopoutWindow!;
        host.OnNativeFullscreenEntered();
        Assert.Null(shell.BrowserTakeoverWindow);
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        Assert.Equal(1.0, host.BrowserZoom);
        Assert.Equal(new[] { true }, topo.Calls);
        host.OnNativeFullscreenExited();
        Assert.Equal(0.75, host.BrowserZoom);
        Assert.Equal(new[] { true, false }, topo.Calls);
        Assert.Same(host, popout.Content);
    });

    [Fact]
    public Task PanicEndsEmbeddedFullscreenAndMirroringEvenUnderStrictLockAndLockdown() => WithShell(true, (shell, page, host, topo) =>
    {
        var strict = CoreSettings.Current.StrictLockEnabled;
        var previousLockdown = LockdownService.Current;
        using var lockdown = new LockdownService();
        try
        {
            CoreSettings.Current.StrictLockEnabled = true;
            LockdownService.Current = lockdown;
            lockdown.Activate(TimeSpan.FromMinutes(30));
            host.OnNativeFullscreenEntered();
            var window = shell.BrowserTakeoverWindow!;
            Assert.True(window.IsVisible);
            PanicSurfaces.StopAll("embedded browser fullscreen test", shell);
            AssertBackInCard(shell, page, host, window);
            Assert.Equal(new[] { true, false }, topo.Calls);
            Assert.True(lockdown.IsActive);
            Assert.True(CoreSettings.Current.StrictLockEnabled);
        }
        finally
        {
            lockdown.Deactivate();
            LockdownService.Current = previousLockdown;
            CoreSettings.Current.StrictLockEnabled = strict;
        }
    });

    [Fact]
    public Task EngineStopEndsEmbeddedFullscreen() => WithShell(false, (shell, page, host, _) =>
    {
        host.OnNativeFullscreenEntered();
        var window = shell.BrowserTakeoverWindow!;
        shell.OnEngineStopped();
        AssertBackInCard(shell, page, host, window);
    });

    // WPF RemoteBrowserStopRule.HideBrowser (Browser.cs:2821): the card refuses page fullscreen, except for the
    // controller's own video, which is meant to play over it.
    [Fact]
    public Task RemoteCardRefusesFullscreenUnlessTheControllersVideoIsPlaying() => WithShell(false, (shell, page, host, _) =>
    {
        var overlay = shell.Named<Border>("RemoteControlOverlay")!;
        overlay.IsVisible = true;
        try
        {
            host.OnNativeFullscreenEntered();
            Assert.Null(shell.BrowserTakeoverWindow);
            Assert.Contains(host, page.BrowserContainer.Children);

            Assert.Null(shell.PlayHypnotubeFromRemote("https://127.0.0.1/video/example-123.html"));
            host.OnNativeFullscreenEntered();
            var window = shell.BrowserTakeoverWindow!;
            Assert.Same(host, window.Content);
            Assert.Equal(WindowState.FullScreen, window.WindowState);

            // The controller's stop ends it within the same call.
            shell.StopBrowserVideoFromRemote();
            AssertBackInCard(shell, page, host, window);
        }
        finally { overlay.IsVisible = false; }
    });
    // WPF MainWindow.Browser.cs:906-916, 1199: a user exit releases the takeover and the video keeps playing.
    [Theory]
    [InlineData("dblclick")]
    [InlineData("escape")]
    [InlineData("page-escape")]
    [InlineData("close")]
    public Task WebKitDeniedTakeoverUsesFullscreenHostAndRestoresPageOnExit(string exit) => WithShell(false, (shell, page, host, _) =>
    {
        var media = ConditioningControlPanel.Services.Browser.BrowserMediaSession.Shared;
        try
        {
            media.ReplaceSession(ConditioningControlPanel.Services.Browser.BrowserMediaSession.MediaOwner.Autonomy, takeover: true);
            media.OnMediaPlaying(60);
            var scripts = new List<string>();
            host.TakeoverFullscreenFallbackOverride = true;
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(script == WebHostTakeover.FullscreenFallbackScript ? "true" : null); };
            shell.TakeoverFocusForTest = _ => { };
            shell.TakeoverFocusDelay = () => Task.CompletedTask;
            shell.ArmTakeoverGestureRetry(host);
            shell.OnBrowserMediaMessage("{\"type\":\"fsAsk\"}");
            shell.OnBrowserMediaMessage("{\"type\":\"fsDenied\",\"activation\":false}");
            Dispatcher.UIThread.RunJobs();
            var window = shell.BrowserTakeoverWindow;
            Assert.NotNull(window);
            Assert.Single(scripts, s => s == WebHostTakeover.FullscreenFallbackScript);
            Assert.True(media.IsTakeover);
            if (exit == "dblclick") host.OnWebMessage("ccp_browser_fullscreen_off");
            // GameWindow.JustDrop.cs:157-160: the focused page relays Esc/F11 itself (TakeoverFullscreenFallback.test.mjs runs it).
            else if (exit == "page-escape")
            {
                Assert.Contains("'Escape'", WebHostTakeover.FullscreenFallbackScript);
                Assert.Contains("'F11'", WebHostTakeover.FullscreenFallbackScript);
                Assert.Contains("ccp_browser_fullscreen_off", WebHostTakeover.FullscreenFallbackScript);
                host.OnWebMessage("ccp_browser_fullscreen_off");
            }
            else if (exit == "escape") window!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            else window!.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(shell.BrowserTakeoverWindow);
            Assert.Contains(host, page.BrowserContainer.Children);
            Assert.Contains(scripts, s => s.Contains("__ccpTakeoverFallbackExit"));
            Assert.False(media.IsTakeover);
            Assert.True(media.IsPlaying);
            Assert.Equal(ConditioningControlPanel.Services.Browser.BrowserMediaSession.MediaOwner.User, media.Owner);
        }
        finally { media.OnMediaStopped("test"); }
    });

    [Fact]
    public Task NativeFullscreenExitLeavesTakeoverToThePage() => WithShell(false, (shell, _, host, _) =>
    {
        var media = ConditioningControlPanel.Services.Browser.BrowserMediaSession.Shared;
        try
        {
            media.ReplaceSession(ConditioningControlPanel.Services.Browser.BrowserMediaSession.MediaOwner.Autonomy, takeover: true);
            host.OnNativeFullscreenEntered();
            Assert.NotNull(shell.BrowserTakeoverWindow);
            host.OnWebMessage("ccp_browser_fullscreen_off");
            Assert.Null(shell.BrowserTakeoverWindow);
            Assert.True(media.IsTakeover); // the page's own fsExit reports a real fullscreen exit
        }
        finally { media.OnMediaStopped("test"); }
    });

    [Fact]
    public Task WebKitStopDuringPageSetupRestoresPageAndNeverEnters() => WithShell(false, (shell, page, host, _) =>
    {
        var scripts = new List<string>();
        var setup = new TaskCompletionSource<string?>();
        host.TakeoverFullscreenFallbackOverride = true;
        host.ScriptInvoker = script => { scripts.Add(script); return script == WebHostTakeover.FullscreenFallbackScript ? setup.Task : Task.FromResult<string?>(null); };
        shell.TakeoverFocusForTest = _ => { };
        shell.TakeoverFocusDelay = () => Task.CompletedTask;
        shell.ArmTakeoverGestureRetry(host);
        shell.OnBrowserMediaMessage("{\"type\":\"fsAsk\"}");
        shell.OnBrowserMediaMessage("{\"type\":\"fsDenied\",\"activation\":false}");
        shell.OnBrowserMediaMessage("{\"type\":\"fsDenied\",\"activation\":false}");
        Assert.Single(scripts, s => s == WebHostTakeover.FullscreenFallbackScript);
        shell.EndWebVideoTakeover();
        setup.SetResult("true");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(shell.BrowserTakeoverWindow);
        Assert.Contains(host, page.BrowserContainer.Children);
        Assert.Contains(WebHostTakeover.FullscreenFallbackCleanupScript, scripts);
    });

    [Fact]
    public Task WebKitNavigationDuringFocusDelayCancelsRetry() => WithShell(false, (shell, _, host, _) =>
    {
        var scripts = new List<string>();
        var delay = new TaskCompletionSource();
        host.TakeoverFullscreenFallbackOverride = true;
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("true"); };
        shell.TakeoverFocusForTest = _ => { };
        shell.TakeoverFocusDelay = () => delay.Task;
        shell.ArmTakeoverGestureRetry(host);
        shell.OnBrowserMediaMessage("{\"type\":\"fsAsk\"}");
        shell.OnBrowserMediaMessage("{\"type\":\"fsDenied\",\"activation\":false}");
        host.NewScrolllerEngine()(true);
        var navigation = new WebViewNavigationStartingEventArgs { Request = new Uri("https://127.0.0.1/next") };
        host.OnNavigationStarted(null, navigation);
        Assert.False(navigation.Cancel);
        delay.SetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(WebHostTakeover.FullscreenFallbackScript, scripts);
        Assert.Null(shell.BrowserTakeoverWindow);
    });
}
