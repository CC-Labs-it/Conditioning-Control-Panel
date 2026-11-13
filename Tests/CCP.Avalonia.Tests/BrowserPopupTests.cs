using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class BrowserPopupTests
{
    private static Task Run(Action<SettingsTabView, WebHost> check) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia().WithInterFont()
                    .With(AppFonts.Options(systemEmoji: false))
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var page = new SettingsTabView();
            check(page, page.FindControl<WebHost>("BrowserWebHost")!);
            return Task.CompletedTask;
        });

    [Fact]
    public Task PopupUsesTheExistingHostAndItsLivePartnerDocument() => Run((page, host) =>
    {
        Assert.True(host.RouteBrowserPopups);
        host.Source = new Uri("https://bambicloud.com/");
        host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
        host.DocumentUrlReader = () => "https://hypnotube.com/watch";
        var before = host.NavigationRequests;
        Assert.True(host.OnBrowserPopup(new Uri("https://doubleclick.net/ads/popup")));
        Assert.Equal(before + 1, host.NavigationRequests);
        Assert.Equal("https://doubleclick.net/ads/popup", host.Source!.AbsoluteUri);
    });

    [Fact]
    public Task AdPopupIsHandledWithoutChangingThePage() => Run((page, host) =>
    {
        host.Source = new Uri("https://hypnotube.com/");
        host.OnNavigationCompleted(new Uri("https://bambicloud.com/watch"));
        host.DocumentUrlReader = () => "https://bambicloud.com/watch";
        var before = host.NavigationRequests;
        Assert.True(host.OnBrowserPopup(new Uri("https://doubleclick.net/ads/popup")));
        Assert.Equal(before, host.NavigationRequests);
        Assert.Equal("https://hypnotube.com/", host.Source!.AbsoluteUri);
    });

    [Theory]
    [InlineData("https://hypnotube.com/watch", "https://bambicloud.com/loading", false)]
    [InlineData("https://bambicloud.com/watch", "https://hypnotube.com/loading", true)]
    public Task PopupUsesNativeDocumentBeforeNavigationCompletes(string completed, string current, bool allowed) => Run((page, host) =>
    {
        host.OnNavigationCompleted(new Uri(completed));
        host.Source = new Uri(completed); // Also stale after a page-initiated redirect.
        host.DocumentUrlReader = () => current;
        var before = host.NavigationRequests;
        Assert.True(host.OnBrowserPopup(new Uri("https://doubleclick.net/ads/popup")));
        Assert.Equal(before + (allowed ? 1 : 0), host.NavigationRequests);
        Assert.Equal(allowed ? "https://doubleclick.net/ads/popup" : completed, host.Source!.AbsoluteUri);
        Assert.Equal(completed, host.CurrentUrl!.AbsoluteUri); // No completion was reported.
    });

    [Fact]
    public Task OrdinaryPopupNavigatesInPlaceWhileOtherEmbedsDoNotOptIn() => Run((page, host) =>
    {
        Assert.True(host.OnBrowserPopup(new Uri("https://example.org/watch")));
        Assert.Equal("https://example.org/watch", host.Source!.AbsoluteUri);
        var other = new WebHost();
        Assert.False(other.OnBrowserPopup(new Uri("https://example.org/watch")));
        Assert.Null(other.Source);
        Assert.Equal(0, other.NavigationRequests);
    });

}
