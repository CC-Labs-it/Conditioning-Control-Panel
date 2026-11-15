using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserDiscoveryTests
{
    [Theory]
    [InlineData("https://hypnotube.com/video/123", true)]
    [InlineData("http://hypnotube.com/video/123", true)]
    [InlineData("https://evil.example/video/123", false)]
    [InlineData("https://evil.example/?url=https://hypnotube.com/video/123", false)]
    [InlineData("https://hypnotube.com/?url=/video/123", false)]
    [InlineData("https://hypnotube.com/videos/123", false)]
    [InlineData("ftp://hypnotube.com/video/123", false)]
    public void GateUsesHostAndPath(string url, bool expected) => Assert.Equal(expected, SettingsTabView.IsDiscoveryPage(url));

    [Theory]
    [InlineData("text CCP:https://example.com/test.json,", "https://example.com/test.json")]
    [InlineData("ccp:https://example.com/a.json.)]", "https://example.com/a.json")]
    [InlineData("no reference", null)]
    public void ExtractsReference(string text, string? expected) => Assert.Equal(expected, SettingsTabView.FindBrowserCcpRef(text));

    [Fact]
    public Task BindBadgeAndCancelOnNextNavigation() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            var badge = tab.FindControl<Border>("DeeperBrowserBadge")!;
            var pending = new TaskCompletionSource<string?>();
            web.ScriptInvoker = _ => Task.FromResult<string?>("ccp:https://example.com/test.json,");
            tab.DiscoveryDelay = _ => Task.CompletedTask;
            tab.DiscoveryFetch = (url, _) => { Assert.Equal("https://example.com/test.json", url); return Task.FromResult<Enhancement?>(Sample()); };
            var page = new Uri("https://hypnotube.com/video/123");
            await tab.DiscoverBrowserAsync(page, true);
            Assert.True(badge.IsVisible);
            Assert.Equal("Sample\nhttps://hypnotube.com/video/123", badge.Tag);
            web.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = page });
            Assert.False(badge.IsVisible);
            Assert.Null(badge.Tag);
            web.ScriptInvoker = _ => pending.Task;
            var stale = tab.DiscoverBrowserAsync(page, true);
            Assert.False(badge.IsVisible);
            web.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = new Uri("https://example.com/") });
            pending.SetResult("ccp:https://example.com/test.json");
            await stale;
            Assert.False(badge.IsVisible);
            Assert.Null(badge.Tag);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public Task NavigationCancelsPendingScrape() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<WebHost>("BrowserWebHost")!;
            var pending = new TaskCompletionSource<string?>();
            int fetches = 0;
            web.ScriptInvoker = _ => pending.Task;
            tab.DiscoveryDelay = _ => Task.CompletedTask;
            tab.DiscoveryFetch = (_, _) => { fetches++; return Task.FromResult<Enhancement?>(Sample()); };
            var page = new Uri("https://hypnotube.com/video/123");
            var stale = tab.DiscoverBrowserAsync(page, true);
            web.OnNavigationStarted(null, new WebViewNavigationStartingEventArgs { Request = page });
            pending.SetResult("ccp:https://example.com/test.json");
            await stale;
            Assert.Equal(0, fetches);
            Assert.False(tab.FindControl<Border>("DeeperBrowserBadge")!.IsVisible);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public Task WebcamPromptRequiresCapabilityAndStoppedTrackerAndResetsOnUnbind() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            int prompts = 0, enabled = 0;
            bool running = false;
            tab.DiscoveryTrackerRunning = () => running;
            tab.DiscoveryPrompt = () => { prompts++; return Task.FromResult(true); };
            tab.DiscoveryEnableWebcam = () => enabled++;
            var ordinary = Sample();
            var webcam = Sample();
            webcam.Metadata.AutoTags.Add(EnhancementAutoTagger.TagWebcam);
            const string url = "https://hypnotube.com/video/123";
            await tab.PromptBrowserWebcamAsync(url, ordinary, CancellationToken.None);
            running = true;
            await tab.PromptBrowserWebcamAsync(url, webcam, CancellationToken.None);
            Assert.Equal(0, prompts);
            running = false;
            await tab.PromptBrowserWebcamAsync(url, webcam, CancellationToken.None);
            await tab.PromptBrowserWebcamAsync(url, webcam, CancellationToken.None);
            Assert.Equal(1, prompts);
            Assert.Equal(1, enabled);
            tab.StopBrowserDiscovery();
            await tab.PromptBrowserWebcamAsync(url, webcam, CancellationToken.None);
            Assert.Equal(2, prompts);
        }
        finally { shell.Close(); }
    });

    private static Enhancement Sample() => new() { Metadata = new EnhancementMetadata { Name = "Sample", Creator = "test" } };
}
