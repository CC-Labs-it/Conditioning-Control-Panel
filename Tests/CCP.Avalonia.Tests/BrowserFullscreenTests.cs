using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.Browser.cs:2727-3055: page fullscreen moves the browser into a chrome-less topmost window (or
/// turns the user's pop-out fullscreen in place), Esc/F11 and the page's exit message restore it, session stop ends it.</summary>
public sealed class BrowserFullscreenTests
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
                var host = page.FindControl<WebHost>("BrowserWebHost")!;
                host.ScriptOverride = _ => Task.FromResult<string?>(PageHoldsFullscreen);   // WebView2 shape: a JSON literal
                check(shell, page, host);
            }
            finally
            {
                PageHoldsFullscreen = "true";
                CoreEngine.StoppedHook = null;
                shell.Close();
                CoreSettings.Current.OfflineMode = offline;
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });

    private static string? PageHoldsFullscreen = "true";

    private static Grid Container(SettingsTabView page) => page.FindControl<Grid>("BrowserContainer")!;

    [Fact]
    public Task PageFullscreenMovesTheCardHostToAChromelessTopmostWindowAndEscapeReturnsIt() => WithShell((shell, page, host) =>
    {
        host.Navigate(new Uri("http://127.0.0.1:9/video"));
        var requests = host.NavigationRequests;
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);   // the page's fullscreenchange, via the WebHost channel

        var fs = shell.BrowserPopoutWindow!;
        Assert.True(shell.IsBrowserFullscreenEmbedded);
        Assert.Same(host, fs.Content);
        Assert.DoesNotContain(host, Container(page).Children);
        Assert.Equal(WindowState.FullScreen, fs.WindowState);
        Assert.Equal(WindowDecorations.None, fs.WindowDecorations);
        Assert.True(fs.Topmost);
        Assert.False(fs.ShowInTaskbar);
        Assert.Equal(Loc.Get("label_browser_in_fullscreen"), page.FindControl<TextBlock>("BrowserLoadingText")!.Text);
        Assert.Equal(Loc.Get("btn_pop_out"), ((TextBlock)page.FindControl<Button>("BtnPopOutBrowser")!.Content!).Text);

        fs.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(shell.IsBrowserFullscreen);
        Assert.False(fs.IsVisible);
        Assert.Null(shell.BrowserPopoutWindow);
        Assert.Contains(host, Container(page).Children);
        Assert.Equal(requests, host.NavigationRequests);   // the page rode along, no reload
    });

    [Fact]
    public Task PopoutGoesFullscreenInPlaceAndThePageExitMessageRestoresIt() => WithShell((shell, page, host) =>
    {
        page.FindControl<Button>("BtnPopOutBrowser")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var popout = shell.BrowserPopoutWindow!;
        popout.Width = 900;
        popout.Height = 600;
        var decorations = popout.WindowDecorations;
        host.OnWebMessage("\"" + MainShellWindow.BrowserFullscreenOnMessage + "\"");   // JSON-quoted, as WebView2 may post it

        Assert.True(shell.IsBrowserFullscreen);
        Assert.False(shell.IsBrowserFullscreenEmbedded);
        Assert.Same(popout, shell.BrowserPopoutWindow);
        Assert.Equal(WindowState.FullScreen, popout.WindowState);
        Assert.Equal(WindowDecorations.None, popout.WindowDecorations);
        Assert.True(popout.Topmost);

        host.OnWebMessage(MainShellWindow.BrowserExitFullscreenMessage);
        Assert.False(shell.IsBrowserFullscreen);
        Assert.True(popout.IsVisible);
        Assert.Same(host, popout.Content);
        Assert.Equal(WindowState.Normal, popout.WindowState);
        Assert.Equal(decorations, popout.WindowDecorations);
        Assert.True(popout.CanResize);
        Assert.False(popout.Topmost);
        Assert.Equal(900, popout.Width);
        Assert.Equal(600, popout.Height);

        // F11 out of a second round leaves the user's pop-out escape-free: Esc afterwards is not ours any more.
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        popout.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F11 });
        Assert.False(shell.IsBrowserFullscreen);
        var esc = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        popout.RaiseEvent(esc);
        Assert.False(esc.Handled);
    });

    [Fact]
    public Task SessionStopExitsBrowserFullscreen() => WithShell((shell, page, host) =>
    {
        // WPF StartStop.cs:529 (#952): a session ending behind a topmost fullscreen browser hides its dialog.
        CoreEngine.StoppedHook = shell.OnEngineStopped;
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.True(shell.IsBrowserFullscreen);
        CoreEngine.Stop();
        Assert.False(shell.IsBrowserFullscreen);
        Assert.Null(shell.BrowserPopoutWindow);
        Assert.Contains(host, Container(page).Children);
    });

    [Fact]
    public Task AFakedFullscreenMessageIsIgnoredAndRealFullscreenHoldsTheLeashGate() => WithShell((shell, page, host) =>
    {
        // Any page script can post the string; WPF only reacts to the engine's ContainsFullScreenElementChanged.
        PageHoldsFullscreen = "false";
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.False(shell.IsBrowserFullscreen);
        Assert.Null(shell.BrowserPopoutWindow);
        Assert.False(shell.ReadLeashWorld(true).FullscreenEffect);

        PageHoldsFullscreen = "true";
        host.OnWebMessage(MainShellWindow.BrowserFullscreenOnMessage);
        Assert.True(shell.IsBrowserFullscreen);
        Assert.True(shell.ReadLeashWorld(true).FullscreenEffect);   // WPF Leash.cs:176
    });
}
