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
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification,
            settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
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
            var delay = new TaskCompletionSource();
            shell.BrowserAutoPlayDelay = () => delay.Task;
            CompanionLinkLauncher.Navigate = url => shell.NavigateToUrlInBrowser(url, autoPlayFullscreen: true);
            const string url = "https://127.0.0.1/video";
            await CompanionLinkLauncher.OpenAsync(url);
            Assert.Equal(new Uri(url), host.Source);
            host.OnNavigationCompleted(new Uri(url));
            Assert.DoesNotContain(MainShellWindow.VideoAutoPlayScript, scripts);
            delay.SetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(scripts, s => s == MainShellWindow.VideoAutoPlayScript);
            host.OnNavigationCompleted(new Uri(url));
            Assert.Single(scripts, s => s == MainShellWindow.VideoAutoPlayScript);
        }
        finally
        {
            CompanionLinkLauncher.Navigate = previous;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task FailureReplacementAndOtherNavigationsDoNotStartVideo() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
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
            var delay = new TaskCompletionSource();
            var waits = 0;
            shell.BrowserAutoPlayDelay = () => { waits++; return delay.Task; };
            var video = new Uri("https://127.0.0.1/video");
            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            host.OnNavigationCompleted(video, success: false);
            host.OnNavigationCompleted(video);
            Assert.Equal(0, waits);
            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            host.OnNavigationCompleted(video);
            Assert.Equal(1, waits);
            Assert.True(shell.NavigateToUrlInBrowser("https://127.0.0.1/other"));
            delay.SetResult();
            Dispatcher.UIThread.RunJobs();
            host.OnNavigationCompleted(new Uri("https://127.0.0.1/other"));
            Assert.True(shell.NavigateToUrlInBrowser("https://bambicloud.com/playlist/1", true));
            host.OnNavigationCompleted(new Uri("https://bambicloud.com/playlist/1"));
            Assert.Equal(1, waits);
            Assert.DoesNotContain(MainShellWindow.VideoAutoPlayScript, scripts);
            return Task.CompletedTask;
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task PanicCancelsPendingAndDelayedWatchLinksAndAllowsANewOne() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
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
            var delay = new TaskCompletionSource();
            var waits = 0;
            shell.BrowserAutoPlayDelay = () => { waits++; return delay.Task; };
            var video = new Uri("https://127.0.0.1/video");
            PanicSurfaces.All = new[] { Assert.Single(surfaces, s => s.Id == "browser-media") };

            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            host.OnNavigationCompleted(video);
            Assert.Equal(1, waits);
            PanicSurfaces.StopAll("test", shell);
            Assert.Single(scripts, s => s == MainShellWindow.PanicBrowserMediaScript);
            delay.SetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(MainShellWindow.VideoAutoPlayScript, scripts);

            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            PanicSurfaces.StopAll("test", shell);
            host.OnNavigationCompleted(video);
            Assert.Equal(1, waits);
            Assert.Equal(2, scripts.Count(s => s == MainShellWindow.PanicBrowserMediaScript));
            Assert.DoesNotContain(MainShellWindow.VideoAutoPlayScript, scripts);

            Assert.True(shell.NavigateToUrlInBrowser(video.ToString(), true));
            host.OnNavigationCompleted(video);
            Assert.Equal(2, waits);
            Assert.Single(scripts, s => s == MainShellWindow.VideoAutoPlayScript);
            return Task.CompletedTask;
        }
        finally
        {
            PanicSurfaces.All = surfaces;
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });

}
