using System;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserDiscoveryTests
{
    [Fact]
    public void ReferenceRequiresVideoPageOnAllowedHost()
    {
        Assert.Equal("https://example.org/a.json", SettingsTabView.BrowserDiscoveryReference("https://hypnotube.com/video/123", "text ccp:https://example.org/a.json)."));
        Assert.Null(SettingsTabView.BrowserDiscoveryReference("https://evil.test/video/123?h=hypnotube.com", "ccp:https://example.org/a"));
        Assert.Null(SettingsTabView.BrowserDiscoveryReference("https://hypnotube.com/", "ccp:https://example.org/a"));
    }

    [Fact]
    public void DiscoveryBindsBadgeAndStopCancelsAndUnbinds() => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            var badge = tab.FindControl<Border>("DeeperBrowserBadge")!;
            web.ScriptInvoker = _ => Task.FromResult<string?>("\"ccp:https://example.org/test.json\"");
            tab.BrowserDiscoveryDelay = _ => Task.CompletedTask;
            CancellationToken token = default;
            tab.BrowserDiscoveryFetch = (url, ct) =>
            {
                Assert.Equal("https://example.org/test.json", url);
                token = ct;
                return Task.FromResult<Enhancement?>(new Enhancement { MediaSource = "https://hypnotube.com/video/123", Metadata = new EnhancementMetadata { Name = "Discovery", Creator = "test" } });
            };
            tab.DiscoverBrowserEnhancementAsync(new Uri("https://hypnotube.com/video/123")).GetAwaiter().GetResult();
            Assert.True(badge.IsVisible);
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            Assert.Contains("Discovery", badge.Tag!.ToString());
            PanicSurfaces.All.Single(surface => surface.Id == "browser-discovery").Stop(shell);
            Assert.True(token.IsCancellationRequested);
            Assert.False(badge.IsVisible);
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
            var called = false;
            tab.BrowserDiscoveryFetch = (_, _) => { called = true; return Task.FromResult<Enhancement?>(null); };
            web.OnNavigationCompleted(new Uri("https://hypnotube.com/video/123"));
            Assert.False(called);
            Assert.False(badge.IsVisible);
        }
        finally { shell.Close(); }
    });
}
