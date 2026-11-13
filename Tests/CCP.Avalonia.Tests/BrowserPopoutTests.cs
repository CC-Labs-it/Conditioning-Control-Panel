using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserPopoutTests
{
    private static Task WithShell(Action<MainShellWindow, SettingsTabView, WebHost> check) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .With(AppFonts.Options(systemEmoji: false))
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var offline = CoreSettings.Current.OfflineMode;
            CoreSettings.Current.OfflineMode = false;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                shell.ShowTab("settings");
                Dispatcher.UIThread.RunJobs();
                var page = shell.Named<SettingsTabView>("SettingsTab")!;
                check(shell, page, page.FindControl<WebHost>("BrowserWebHost")!);
            }
            finally
            {
                shell.Close();
                CoreSettings.Current.OfflineMode = offline;
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });

    private static void Click(Control root, string name) =>
        root.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task PopoutMovesTheSameHostFocusesItAndReturnsItOnClose() => WithShell((shell, page, host) =>
    {
        host.Navigate(new Uri("http://127.0.0.1:9/video"));
        host.NewScrolllerEngine()(false); // moving a held/refused engine must not weaken its gate
        var requests = host.NavigationRequests;
        Assert.True(page.FindControl<Button>("BtnPopOutBrowser")!.IsEnabled);
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        Assert.Equal(Loc.Get("title_browser_window"), popout.Title);
        Assert.Null(popout.Owner); // unowned as WPF: survives minimising the shell
        Assert.Same(host, popout.Content);
        Assert.Equal(1024, popout.Width);
        Assert.Equal(768, popout.Height);
        Assert.Equal(400, popout.MinWidth);
        Assert.Equal(300, popout.MinHeight);
        Assert.DoesNotContain(host, page.FindControl<Grid>("BrowserContainer")!.Children);
        Assert.True(host.IsVisible);
        Assert.True(page.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
        Assert.Equal(IconText.BareLoc("btn_focus"), ((TextBlock)page.FindControl<Button>("BtnPopOutBrowser")!.Content!).Text);
        popout.WindowState = WindowState.Minimized;
        Click(page, "BtnPopOutBrowser");
        Assert.Equal(WindowState.Normal, popout.WindowState); // Focus un-minimises, WPF :329-330
        page.OnBrowserPromptClick();
        Assert.Same(popout, shell.BrowserPopoutWindow);
        Assert.Equal(requests, host.NavigationRequests);

        popout.Close();
        Assert.Null(popout.Content);
        Assert.Contains(host, page.FindControl<Grid>("BrowserContainer")!.Children);
        Assert.False(page.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
        Assert.Equal(Loc.Get("btn_pop_out"), ((TextBlock)page.FindControl<Button>("BtnPopOutBrowser")!.Content!).Text);
        Assert.Equal(requests, host.NavigationRequests);
        Assert.False(host.ScrolllerGate);
    });

    [Fact]
    public Task OfflinePopoutDoesNotCreateAWindowOrNavigateAndFirstOnlinePopoutLoadsHome() => WithShell((shell, page, host) =>
    {
        var requests = host.NavigationRequests;
        CoreSettings.Current.OfflineMode = true;
        Click(page, "BtnPopOutBrowser");
        Assert.Null(shell.BrowserPopoutWindow);
        Assert.Equal(requests, host.NavigationRequests);
        CoreSettings.Current.OfflineMode = false;
        Click(page, "BtnPopOutBrowser");
        Assert.NotNull(shell.BrowserPopoutWindow);
        Assert.Equal(requests + 1, host.NavigationRequests);
        Assert.Equal(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked == true
            ? "hypnotube.com" : "bambicloud.com", host.Source!.Host);
    });

    [Fact]
    public Task LinkNavigationKeepsUsingThePoppedOutHost() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        var requests = host.NavigationRequests;
        Assert.True(shell.NavigateToUrlInBrowser("http://127.0.0.1:9/next"));
        Assert.Same(host, popout.Content);
        Assert.Equal(requests + 1, host.NavigationRequests);
        Assert.Equal("/next", host.Source!.AbsolutePath);
        Assert.Same(popout, shell.BrowserPopoutWindow);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task EmbeddedLinkRestoresAHiddenOrMinimizedShell(bool hidden) => WithShell((shell, page, host) =>
    {
        if (hidden) shell.Hide();
        else shell.WindowState = WindowState.Minimized;
        Assert.True(shell.NavigateToUrlInBrowser("http://127.0.0.1:9/visible"));
        Assert.True(shell.IsVisible);
        Assert.Equal(WindowState.Normal, shell.WindowState);
        Assert.Equal("/visible", host.Source!.AbsolutePath);
    });

    [Fact]
    public Task PanicLeavesTheUsersOwnPageAndPopoutAlone() => WithShell((shell, page, host) =>
    {
        // WPF MainWindow.Browser.cs:3146-3150, 3228-3230: panic never stops the page the user browses themselves.
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        var source = host.Source;
        PanicSurfaces.StopAll("browser test", shell);
        Assert.True(popout.IsVisible);
        Assert.Same(host, popout.Content);
        Assert.Equal(source, host.Source);
    });

    [Fact]
    public Task ClosingShellClosesItsPopoutAndUnloadsThePage() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        shell.Close();
        Assert.False(popout.IsVisible);
        Assert.Equal("about:blank", host.Source!.AbsoluteUri);
    });
    [Fact]
    public Task NativeFullscreenRestoresPopoutOnPageExitAndFocusUsesSameWindow() => WithShell((shell, page, host) =>
    {
        var scripts = new System.Collections.Generic.List<string>();
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        var decorations = popout.WindowDecorations;
        host.OnNativeFullscreenEntered();
        Assert.Contains(scripts, s => s.Contains("ccp_browser_fullscreen_off"));
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        Assert.Same(host, popout.Content);
        Click(page, "BtnPopOutBrowser");
        Assert.Same(popout, shell.BrowserPopoutWindow);
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        host.OnWebMessage("ccp_browser_fullscreen_off");
        Assert.Equal(WindowState.Normal, popout.WindowState);
        Assert.Equal(decorations, popout.WindowDecorations);
        Assert.Same(host, popout.Content);
        Assert.Contains(scripts, s => s.Contains("document.exitFullscreen") && s.Contains("removeEventListener"));
        host.OnNativeFullscreenEntered();
        popout.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Escape });
        Assert.Equal(WindowState.Normal, popout.WindowState);
    });

    [Fact]
    public Task PanicExitsFullscreenEvenUnderStrictLock() => WithShell((shell, page, host) =>
    {
        var strict = CoreSettings.Current.StrictLockEnabled;
        var previousLockdown = ConditioningControlPanel.Services.LockdownService.Current;
        using var lockdown = new ConditioningControlPanel.Services.LockdownService();
        try
        {
            CoreSettings.Current.StrictLockEnabled = true;
            ConditioningControlPanel.Services.LockdownService.Current = lockdown;
            lockdown.Activate(TimeSpan.FromMinutes(30));
            var panicEnabled = CoreSettings.Current.PanicKeyEnabled;
            Click(page, "BtnPopOutBrowser");
            var popout = shell.BrowserPopoutWindow!;
            host.OnNativeFullscreenEntered();
            Assert.Equal(WindowState.FullScreen, popout.WindowState);
            PanicSurfaces.StopAll("browser fullscreen test", shell);
            Assert.Equal(WindowState.Normal, popout.WindowState);
            Assert.True(CoreSettings.Current.StrictLockEnabled);
            Assert.True(lockdown.IsActive);
            Assert.Equal(panicEnabled, CoreSettings.Current.PanicKeyEnabled);
            Assert.Same(host, popout.Content);
        }
        finally
        {
            lockdown.Deactivate();
            ConditioningControlPanel.Services.LockdownService.Current = previousLockdown;
            CoreSettings.Current.StrictLockEnabled = strict;
        }
    });

    [Fact]
    public Task ClosingPopoutAndShellRemovesFullscreenHandlers() => WithShell((shell, page, host) =>
    {
        var scripts = new System.Collections.Generic.List<string>();
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        host.OnNativeFullscreenEntered();
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        popout.Close();
        Assert.False(popout.Topmost);
        Assert.Contains(host, page.FindControl<Grid>("BrowserContainer")!.Children);
        Click(page, "BtnPopOutBrowser");
        var next = shell.BrowserPopoutWindow!;
        shell.Close();
        scripts.Clear();
        host.OnNavigationCompleted(new Uri("https://example.com/closed"));
        host.OnNativeFullscreenEntered();
        Assert.DoesNotContain(scripts, s => s.Contains("ccp_browser_fullscreen_off"));
        foreach (var eventName in new[] { "NavigationCompleted", "NavigationBeginning", "NativeFullscreenEntered", "NativeFullscreenExited" })
        {
            var handlers = (Delegate?)typeof(WebHost).GetField(eventName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host);
            Assert.DoesNotContain(handlers?.GetInvocationList() ?? Array.Empty<Delegate>(), h => ReferenceEquals(h.Target, shell));
        }
        Assert.False(next.IsVisible);
        Assert.NotEqual(WindowState.FullScreen, next.WindowState);
    });

    [Fact]
    public Task NativeExitRestoresPopoutEvenBeforePageScriptCompletes() => WithShell((shell, page, host) =>
    {
        var script = new TaskCompletionSource<string?>();
        var cleanups = 0;
        host.ScriptInvoker = js =>
        {
            if (js.Contains("var f=window.__ccpBrowserFsExit")) { cleanups++; return Task.FromResult<string?>(null); }
            return script.Task;
        };
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        host.OnNativeFullscreenEntered();
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        host.OnNativeFullscreenExited();
        Assert.Equal(WindowState.Normal, popout.WindowState);
        Assert.Same(host, popout.Content);
        Assert.Equal(1, cleanups);
        script.SetResult(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, cleanups); // a late install is retired after the first cleanup
    });

    [Fact]
    public Task AcceptedNavigationRestoresFullscreenButRefusedNavigationDoesNot() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        host.NewScrolllerEngine()(true);
        var url = new Uri("http://127.0.0.1:9/next");
        host.AllowNavigation = _ => false;
        host.OnNativeFullscreenEntered();
        var refused = new WebViewNavigationStartingEventArgs { Request = url };
        host.OnNavigationStarted(null, refused);
        Assert.True(refused.Cancel);
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        host.AllowNavigation = _ => true;
        var accepted = new WebViewNavigationStartingEventArgs { Request = url };
        host.OnNavigationStarted(null, accepted);
        Assert.False(accepted.Cancel);
        Assert.Equal(WindowState.Normal, popout.WindowState);
        Assert.Same(host, popout.Content);
    });

    [Fact]
    public Task NavigationCompletionRestoresFullscreenIfStartWasMissed() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        host.OnNativeFullscreenEntered();
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        // With no recorded navigation start, native exit is the evidence to restore the window.
        host.OnNativeFullscreenExited();
        Assert.Equal(WindowState.Normal, popout.WindowState);
        host.OnNavigationCompleted(new Uri("http://127.0.0.1:9/next"));
        Assert.Equal(WindowState.Normal, popout.WindowState);
        Assert.Same(host, popout.Content);
    });

    [Fact]
    public Task FullscreenEnteredWhileNavigationIsLoadingSurvivesItsCompletion() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        host.NewScrolllerEngine()(true);
        var url = new Uri("http://127.0.0.1:9/loading");
        var started = new WebViewNavigationStartingEventArgs { Request = url };
        host.OnNavigationStarted(null, started);
        Assert.False(started.Cancel);
        host.OnNativeFullscreenEntered();
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        host.OnNavigationCompleted(url);
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        Assert.Same(host, popout.Content);
    });

    [Fact]
    public Task ANewFullscreenSessionSurvivesAnEarlierFullscreenNavigationCompletion() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        host.NewScrolllerEngine()(true);
        var url = new Uri("http://127.0.0.1:9/next");
        host.OnNativeFullscreenEntered();
        var started = new WebViewNavigationStartingEventArgs { Request = url };
        host.OnNavigationStarted(null, started);
        Assert.False(started.Cancel);
        Assert.Equal(WindowState.Normal, popout.WindowState);
        host.OnNativeFullscreenEntered();
        host.OnNavigationCompleted(url);
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        Assert.Same(host, popout.Content);
    });

    [Fact]
    public Task UntrackedNavigationCompletionDoesNotCloseFullscreen() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.BrowserPopoutWindow!;
        host.OnNativeFullscreenEntered();
        host.OnNavigationCompleted(new Uri("http://127.0.0.1:9/next"));
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        Assert.Same(host, popout.Content);
    });

}
