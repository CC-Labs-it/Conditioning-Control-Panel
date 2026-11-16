using System;
using System.IO;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
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
            web.ScriptInvoker = _ => Task.FromResult<string?>("[\"ccp:https://example.org/test.json\"]");
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
            Assert.True(tab.BrowserDiscoveryHost.IsRunning);
            Assert.Contains("Discovery", badge.Tag!.ToString());
            PanicSurfaces.All.Single(surface => surface.Id == "browser-discovery").Stop(shell);
            Assert.True(token.IsCancellationRequested);
            Assert.False(badge.IsVisible);
            Assert.Null(tab.BrowserDiscoveryHost.LoadedEnhancement);
            var called = false;
            tab.BrowserDiscoveryFetch = (_, _) => { called = true; return Task.FromResult<Enhancement?>(null); };
            web.OnNavigationCompleted(new Uri("https://hypnotube.com/video/123"));
            Assert.False(called);
            Assert.False(badge.IsVisible);
        }
        finally { shell.Close(); }
    });

    // WebKitGTK returns the script's JSON as-is; WebView2 wraps it in one more JSON string literal.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescriptionStartingWithQuoteBindsOnBothEngineShapes(bool webView2) => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            var json = System.Text.Json.JsonSerializer.Serialize(new[] { "\"Sleepy\" trance ccp:https://example.org/quote.json" });
            var raw = webView2 ? System.Text.Json.JsonSerializer.Serialize(json) : json;
            web.ScriptInvoker = _ => Task.FromResult<string?>(raw);
            tab.BrowserDiscoveryDelay = _ => Task.CompletedTask;
            string? fetched = null;
            tab.BrowserDiscoveryFetch = (url, _) =>
            {
                fetched = url;
                return Task.FromResult<Enhancement?>(new Enhancement { MediaSource = "https://hypnotube.com/video/9", Metadata = new EnhancementMetadata { Name = "Quote", Creator = "test" } });
            };
            tab.DiscoverBrowserEnhancementAsync(new Uri("https://hypnotube.com/video/9")).GetAwaiter().GetResult();
            Assert.Equal("https://example.org/quote.json", fetched);
            Assert.True(tab.BrowserDiscoveryHost.IsRunning);
        }
        finally { shell.Close(); CoreEngine.Stop(); }
    });

    // WPF runs discovery on App.DeeperHost (App.xaml.cs:3520) and library matches on their own host
    // (BrowserEnhancementBridge.cs:21): neither undoes the other, and the toggle only re-evaluates the library.
    [Fact]
    public void DiscoveryKeepsItsOwnHostAcrossLibraryMatchAndToggle() => AvaloniaTestDispatcher.Run(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreSettings.Current.BrowserEnhanceIfPossible;
        CoreSettings.Current.BrowserEnhanceIfPossible = true;
        var shell = new MainShellWindow();
        string? path = null;
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            var badge = tab.FindControl<Border>("DeeperBrowserBadge")!;
            var checkbox = tab.FindControl<CheckBox>("ToggleEnhanceIfPossible")!;
            var status = tab.FindControl<TextBlock>("TxtEnhanceMatchStatus")!;
            const string url = "https://hypnotube.com/video/ccp-discovery-unique";
            path = Path.Combine(shell.BrowserEnhancementLibrary!.LibraryFolder, "discovery-host-test-unique.ccpenh.json");
            File.WriteAllText(path, "{\"$schema\":\"ccp-enhancement/v1\",\"version\":1,\"media_type\":\"video\",\"media_source\":\"" + url + "\",\"metadata\":{\"name\":\"Library match\",\"creator\":\"test\"}}");
            web.ScriptInvoker = _ => Task.FromResult<string?>("[\"ccp:https://example.org/test.json\"]");
            tab.BrowserDiscoveryDelay = _ => Task.CompletedTask;
            tab.BrowserDiscoveryFetch = (_, _) => Task.FromResult<Enhancement?>(new Enhancement { MediaSource = url, Metadata = new EnhancementMetadata { Name = "Discovered", Creator = "test" } });
            web.OnNavigationCompleted(new Uri(url));
            Assert.Equal("Library match", tab.BrowserEnhanceHost.LoadedEnhancement?.Metadata?.Name);
            tab.DiscoverBrowserEnhancementAsync(new Uri(url)).GetAwaiter().GetResult();
            Assert.Equal("Library match", tab.BrowserEnhanceHost.LoadedEnhancement?.Metadata?.Name);
            Assert.Equal(string.Format(Loc.Get("browser_enhance_match_fmt"), "Library match"), status.Text);
            Assert.Equal("Discovered", tab.BrowserDiscoveryHost.LoadedEnhancement?.Metadata?.Name);
            checkbox.IsChecked = false;
            Assert.Null(tab.BrowserEnhanceHost.LoadedEnhancement);
            Assert.True(tab.BrowserDiscoveryHost.IsRunning);
            Assert.True(badge.IsVisible);
            checkbox.IsChecked = true;
            Assert.True(tab.BrowserDiscoveryHost.IsRunning);
            Assert.True(badge.IsVisible);
            web.OnNavigationStarted(null, new global::Avalonia.Controls.WebViewNavigationStartingEventArgs { Request = new Uri("https://hypnotube.com/") });
            Assert.Null(tab.BrowserDiscoveryHost.LoadedEnhancement);
            Assert.False(badge.IsVisible);
        }
        finally
        {
            shell.Close();
            if (path != null) File.Delete(path);
            CoreSettings.Current.BrowserEnhanceIfPossible = previous;
            CoreEngine.Stop();
        }
    });
}
