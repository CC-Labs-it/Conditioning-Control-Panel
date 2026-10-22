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
        var requests = host.NavigationRequests;
        Assert.True(page.FindControl<Button>("BtnPopOutBrowser")!.IsEnabled);
        Click(page, "BtnPopOutBrowser");
        var popout = Assert.Single(shell.OwnedWindows.Where(w => w.Title == Loc.Get("title_browser_window")));
        Assert.Same(host, popout.Content);
        Assert.Equal(1024, popout.Width);
        Assert.Equal(768, popout.Height);
        Assert.Equal(400, popout.MinWidth);
        Assert.Equal(300, popout.MinHeight);
        Assert.DoesNotContain(host, page.FindControl<Grid>("BrowserContainer")!.Children);
        Assert.True(host.IsVisible);
        Assert.True(page.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
        Assert.Equal(IconText.BareLoc("btn_focus"), ((TextBlock)page.FindControl<Button>("BtnPopOutBrowser")!.Content!).Text);
        Click(page, "BtnPopOutBrowser");
        page.OnBrowserPromptClick();
        Assert.Single(shell.OwnedWindows.Where(w => w.Title == Loc.Get("title_browser_window")));
        Assert.Equal(requests, host.NavigationRequests);

        popout.Close();
        Assert.Null(popout.Content);
        Assert.Contains(host, page.FindControl<Grid>("BrowserContainer")!.Children);
        Assert.False(page.FindControl<TextBlock>("BrowserLoadingText")!.IsVisible);
        Assert.Equal(Loc.Get("btn_pop_out"), ((TextBlock)page.FindControl<Button>("BtnPopOutBrowser")!.Content!).Text);
        Assert.Equal(requests, host.NavigationRequests);
    });

    [Fact]
    public Task OfflinePopoutDoesNotCreateAWindowOrNavigateAndFirstOnlinePopoutLoadsHome() => WithShell((shell, page, host) =>
    {
        var requests = host.NavigationRequests;
        CoreSettings.Current.OfflineMode = true;
        Click(page, "BtnPopOutBrowser");
        Assert.Empty(shell.OwnedWindows.Where(w => w.Title == Loc.Get("title_browser_window")));
        Assert.Equal(requests, host.NavigationRequests);
        CoreSettings.Current.OfflineMode = false;
        Click(page, "BtnPopOutBrowser");
        Assert.Single(shell.OwnedWindows.Where(w => w.Title == Loc.Get("title_browser_window")));
        Assert.Equal(requests + 1, host.NavigationRequests);
        Assert.Equal(page.FindControl<RadioButton>("RbHypnoTube")!.IsChecked == true
            ? "hypnotube.com" : "bambicloud.com", host.Source!.Host);
    });

    [Fact]
    public Task LinkNavigationKeepsUsingThePoppedOutHost() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.OwnedWindows.Single(w => w.Title == Loc.Get("title_browser_window"));
        var requests = host.NavigationRequests;
        Assert.True(shell.NavigateToUrlInBrowser("http://127.0.0.1:9/next"));
        Assert.Same(host, popout.Content);
        Assert.Equal(requests + 1, host.NavigationRequests);
        Assert.Equal("/next", host.Source!.AbsolutePath);
        Assert.Single(shell.OwnedWindows.Where(w => w.Title == Loc.Get("title_browser_window")));
    });

    [Fact]
    public Task PanicClosesPopoutAndUnloadsMediaEvenWhenOffline() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.OwnedWindows.Single(w => w.Title == Loc.Get("title_browser_window"));
        CoreSettings.Current.OfflineMode = true;
        var surface = PanicSurfaces.All.Single(s => s.Id == "browser");
        surface.Stop(shell);
        Assert.False(popout.IsVisible);
        Assert.Equal("about:blank", host.Source!.AbsoluteUri);
        Assert.Contains(host, page.FindControl<Grid>("BrowserContainer")!.Children);
        surface.Stop(shell); // safe when already stopped
        Assert.Equal("about:blank", host.Source.AbsoluteUri);
    });

    [Fact]
    public Task ClosingShellClosesItsPopoutAndUnloadsThePage() => WithShell((shell, page, host) =>
    {
        Click(page, "BtnPopOutBrowser");
        var popout = shell.OwnedWindows.Single(w => w.Title == Loc.Get("title_browser_window"));
        shell.Close();
        Assert.False(popout.IsVisible);
        Assert.Equal("about:blank", host.Source!.AbsoluteUri);
    });
}
