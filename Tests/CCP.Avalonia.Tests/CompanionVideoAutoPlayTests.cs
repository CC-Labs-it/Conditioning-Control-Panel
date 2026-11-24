using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionVideoAutoPlayTests
{
    [Fact]
    public Task WatchLinkRunsOnceAfterSuccessfulNavigation() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var previousSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification,
            settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
        settings.Current.ProtectBrowserVideoPlayback = false;
        var previous = CompanionLinkLauncher.Navigate;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var watch = Stopwatch.StartNew();
            await shell.BrowserAutoPlayDelay();
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(1400));
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.NavigationInvoker = _ => true;
            var delay = new TaskCompletionSource();
            shell.BrowserAutoPlayDelay = () => delay.Task;
            // The real companion path: the watch chip waits for the Scrolller gate, then arms autoplay.
            CompanionLinkLauncher.Navigate = shell.NavigateCompanionUrlAsync;
            host.NewScrolllerEngine()(true);
            const string url = "https://127.0.0.1/video";
            await CompanionLinkLauncher.OpenAsync(url);
            Assert.True(LockCardWindow.WebVideoClaimed);
            Assert.True(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance.BrowserMediaBlocksScheduledVideo);
            Assert.Equal(new Uri(url), host.Source);
            host.OnNavigationCompleted(new Uri(url));
            Assert.DoesNotContain(WebHostTakeover.VideoAutoPlayScript, scripts);
            delay.SetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(scripts, s => s == WebHostTakeover.VideoAutoPlayScript);
            host.OnNavigationCompleted(new Uri(url));
            Assert.Single(scripts, s => s == WebHostTakeover.VideoAutoPlayScript);
        }
        finally
        {
            CoreSettings.ServiceProvider = previousSettings;
            CompanionLinkLauncher.Navigate = previous;
            LockCardWindow.EndWebVideo();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task FailureReplacementAndOtherNavigationsDoNotStartVideo() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previousSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification,
            settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.NavigationInvoker = _ => true;
            var delay = new TaskCompletionSource();
            var waits = 0;
            shell.BrowserAutoPlayDelay = () => { waits++; return delay.Task; };
            var video = new Uri("https://127.0.0.1/video");
            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            host.OnNavigationCompleted(video, success: false); Assert.False(LockCardWindow.WebVideoClaimed);
            host.OnNavigationCompleted(video);
            Assert.Equal(0, waits);
            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            host.OnNavigationCompleted(video);
            Assert.Equal(1, waits);
            Assert.True(shell.NavigateToUrlInBrowser("https://127.0.0.1/other")); Assert.False(LockCardWindow.WebVideoClaimed);
            delay.SetResult();
            Dispatcher.UIThread.RunJobs();
            host.OnNavigationCompleted(new Uri("https://127.0.0.1/other"));
            Assert.DoesNotContain(WebHostTakeover.VideoAutoPlayScript, scripts);
            return Task.CompletedTask;
        }
        finally
        {
            CoreSettings.ServiceProvider = previousSettings;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task PanicCancelsPendingAndDelayedWatchLinksAndAllowsANewOne() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var previousSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification,
            settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
        var shell = new MainShellWindow();
        var surfaces = PanicSurfaces.All;
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.NavigationInvoker = _ => true;
            var delay = new TaskCompletionSource();
            var waits = 0;
            shell.BrowserAutoPlayDelay = () => { waits++; return delay.Task; };
            var video = new Uri("https://127.0.0.1/video");
            PanicSurfaces.All = new[] { Assert.Single(surfaces, s => s.Id == "browser-media") };

            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true)); Assert.True(LockCardWindow.WebVideoClaimed);
            host.OnNavigationCompleted(video);
            Assert.Equal(1, waits);
            PanicSurfaces.StopAll("test", shell); Assert.False(LockCardWindow.WebVideoClaimed);
            Assert.Single(scripts, s => s == WebHostMedia.PanicPauseScript);
            delay.SetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(WebHostTakeover.VideoAutoPlayScript, scripts);

            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            PanicSurfaces.StopAll("test", shell);
            host.OnNavigationCompleted(video);
            Assert.Equal(1, waits);
            Assert.Equal(2, scripts.Count(s => s == WebHostMedia.PanicPauseScript));
            Assert.DoesNotContain(WebHostTakeover.VideoAutoPlayScript, scripts);

            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            host.OnNavigationCompleted(video);
            Assert.Equal(2, waits);
            Assert.Single(scripts, s => s == WebHostTakeover.VideoAutoPlayScript);
            return Task.CompletedTask;
        }
        finally
        {
            CoreSettings.ServiceProvider = previousSettings;
            PanicSurfaces.All = surfaces;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task PlaylistClaimsPlaysAndPanicCancels() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification,
            settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.NavigationInvoker = _ => true;
            host.NewScrolllerEngine()(true);
            var delay = new TaskCompletionSource();
            shell.BrowserAutoPlayDelay = () => delay.Task;
            // Loopback is allowed by the test sandbox while preserving the playlist URL match.
            var url = new Uri("https://127.0.0.1/bambicloud.com/playlist/1");
            Assert.True(await shell.NavigateCompanionUrlAsync(url.ToString()));
            Assert.True(LockCardWindow.WebVideoClaimed);
            host.OnNavigationCompleted(url);
            delay.SetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(scripts, script => script.Contains("ccpPlaylistNoButton"));
            var reporter = scripts.Last(script => script.Contains("ccpRemixMedia"));
            var id = System.Text.RegularExpressions.Regex.Match(reporter, "const id = \"([^\"]+)\"").Groups[1].Value;
            Assert.NotEmpty(id);
            host.OnWebMessage(System.Text.Json.JsonSerializer.Serialize(new { type = "ccpRemixMedia", id, active = true, interrupting = true }));
            Assert.True(LockCardWindow.WebVideoClaimed);
            host.OnWebMessage(System.Text.Json.JsonSerializer.Serialize(new { type = "ccpRemixMedia", id, active = false, interrupting = false }));
            Assert.False(LockCardWindow.WebVideoClaimed);

            delay = new TaskCompletionSource();
            Assert.True(await shell.NavigateCompanionUrlAsync(url.ToString()));
            host.OnNavigationCompleted(url);
            PanicSurfaces.All.Single(s => s.Id == "browser-media").Stop(shell);
            Assert.False(LockCardWindow.WebVideoClaimed);
            Assert.Contains(WebHostMedia.PanicPauseScript, scripts);
            delay.SetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(scripts, script => script.Contains("ccpPlaylistNoButton"));
        }
        finally
        {
            CoreSettings.ServiceProvider = oldSettings;
            LockCardWindow.EndWebVideo();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task MissingPlaylistButtonReleasesOnlyItsPendingClaim() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var oldSettings = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification,
            settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.NavigationInvoker = _ => true;
            host.NewScrolllerEngine()(true);
            shell.BrowserAutoPlayDelay = () => Task.CompletedTask;
            var url = new Uri("https://127.0.0.1/bambicloud.com/playlist/1");
            async Task<string> Start()
            {
                Assert.True(await shell.NavigateCompanionUrlAsync(url.ToString()));
                Assert.True(LockCardWindow.WebVideoClaimed);
                host.OnNavigationCompleted(url);
                Dispatcher.UIThread.RunJobs();
                var script = scripts.Last(script => script.Contains("ccpPlaylistNoButton"));
                var token = System.Text.RegularExpressions.Regex.Match(script, "token: \"([^\"]+)\"").Groups[1].Value;
                Assert.NotEmpty(token);
                return token;
            }
            string Message(string token) => System.Text.Json.JsonSerializer.Serialize(new { type = "ccpPlaylistNoButton", token });

            var first = await Start();
            host.OnWebMessage("{");
            host.OnWebMessage("[]");
            host.OnWebMessage(Message("wrong"));
            Assert.True(LockCardWindow.WebVideoClaimed);
            host.OnWebMessage(System.Text.Json.JsonSerializer.Serialize(Message(first)));
            Assert.False(LockCardWindow.WebVideoClaimed);

            var stale = await Start();
            var current = await Start();
            host.OnWebMessage(Message(stale));
            Assert.True(LockCardWindow.WebVideoClaimed);
            LockCardWindow.NoteWebVideo(true);
            host.OnWebMessage(Message(current));
            Assert.True(LockCardWindow.WebVideoClaimed);
            LockCardWindow.EndWebVideo();

            var beforePanic = await Start();
            PanicSurfaces.All.Single(s => s.Id == "browser-media").Stop(shell);
            Assert.False(LockCardWindow.WebVideoClaimed);
            var afterPanic = await Start();
            host.OnWebMessage(Message(beforePanic));
            Assert.True(LockCardWindow.WebVideoClaimed);
            host.OnWebMessage(Message(afterPanic));
            Assert.False(LockCardWindow.WebVideoClaimed);
        }
        finally
        {
            CoreSettings.ServiceProvider = oldSettings;
            LockCardWindow.EndWebVideo();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

}
