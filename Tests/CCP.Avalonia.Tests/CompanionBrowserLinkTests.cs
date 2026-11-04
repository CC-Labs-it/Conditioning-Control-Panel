using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class CompanionBrowserLinkTests
{
    private static async Task WithShell(Func<MainShellWindow, WebHost, Task> run)
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldShell = CompanionLinkLauncher.Shell; var oldOpen = AppUpdater.OpenUrl;
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.AvatarEnabled, s.OfflineMode) = (true, true, 1, false, false);
        var shell = new MainShellWindow(); CompanionLinkLauncher.Shell = () => shell;
        try
        {
            shell.Show();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            await run(shell, host);
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs(); CompanionLinkLauncher.Shell = oldShell;
            AppUpdater.OpenUrl = oldOpen; CoreSettings.ServiceProvider = oldSettings;
        }
    }
    [Fact]
    public Task MissingEngineFallsBackExternallyAndReleasesTheClaim() => AvaloniaTestDispatcher.RunAsync(() => WithShell(async (shell, host) =>
    {
        Assert.False(host.HasEngine);
        var opened = new List<string>();
        AppUpdater.OpenUrl = (_, url) => { opened.Add(url); return Task.CompletedTask; };
        await CompanionLinkLauncher.OpenAsync("https://127.0.0.1/video");
        Assert.Equal(new[] { "https://127.0.0.1/video" }, opened);
        Assert.False(shell.AvatarBrowserSessionActive);
        await CompanionLinkLauncher.OpenAsync("http://127.0.0.1/video");
        Assert.Single(opened); // WPF permits only HTTPS as an external fallback
        Assert.False(shell.AvatarBrowserSessionActive);
        host.NavigationInvoker = _ => throw new InvalidOperationException();
        await CompanionLinkLauncher.OpenAsync("https://127.0.0.1/failure");
        Assert.Equal("https://127.0.0.1/failure", opened.Last());
        Assert.False(shell.AvatarBrowserSessionActive);
    }));
    [Fact]
    public Task AvatarClaimGatesInteractionAndPanicPausesAndReleasesIt() => AvaloniaTestDispatcher.RunAsync(() => WithShell(async (shell, host) =>
    {
        var navigated = new List<Uri>(); var scripts = new List<string>();
        host.NavigationInvoker = uri => { navigated.Add(uri); return true; };
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        await CompanionLinkLauncher.OpenAsync("https://127.0.0.1/video");
        Assert.Equal("https://127.0.0.1/video", Assert.Single(navigated).AbsoluteUri);
        Assert.True(shell.AvatarBrowserSessionActive); Assert.True(shell.AvatarBrowserTakeover);
        Assert.True(shell.Autonomy.IsBusy!());
        shell.CheckBrowserHeartbeat(DateTime.UtcNow.AddSeconds(59));
        Assert.True(shell.AvatarBrowserSessionActive);
        Assert.Contains(PanicSurfaces.All, s => s.Id == "browser-media");
        PanicSurfaces.All.Single(s => s.Id == "browser-media").Stop(shell);
        Assert.False(shell.AvatarBrowserSessionActive);
        Assert.Contains(scripts, script => script.Contains("_ccpTakeoverStopped=true") && script.Contains("v.pause()"));
        host.OnNavigationCompleted(new Uri("https://127.0.0.1/video"));
        Assert.DoesNotContain(scripts, script => script.Contains("ccpAvatarMedia"));
        await CompanionLinkLauncher.OpenAsync("https://127.0.0.1/timeout");
        shell.CheckBrowserHeartbeat(DateTime.UtcNow.AddSeconds(61));
        Assert.False(shell.AvatarBrowserSessionActive);
        shell.ClaimAvatarBrowserSession(); var navGen = (int)typeof(MainShellWindow).GetField("_browserAutoplayGeneration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell)!; host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{navGen},\"state\":\"playing\"}}");
        Assert.True(shell.NavigateToUrlInBrowser("https://127.0.0.1/new")); Assert.False(shell.AvatarBrowserSessionActive);
    }));
    [Fact]
    public Task PanicRetiresTheClaimAndCancelsThePendingAutoplay() => AvaloniaTestDispatcher.RunAsync(() => WithShell(async (shell, host) =>
    {
        var scripts = new List<string>(); host.NavigationInvoker = _ => true;
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.BrowserAutoplayDelay = () => delay.Task;
        shell.ClaimAvatarBrowserSession(); var url = new Uri("https://127.0.0.1/video");
        Assert.True(shell.NavigateToUrlInBrowser(url.AbsoluteUri, autoPlayFullscreen: true));
        host.OnNavigationCompleted(url);
        PanicSurfaces.All.Single(s => s.Id == "browser-media").Stop(shell);
        delay.SetResult(); await Task.Yield();
        Assert.False(shell.AvatarBrowserSessionActive);
        Assert.Contains(scripts, x => x.Contains("v.pause()"));
        Assert.DoesNotContain(scripts, x => x.Contains("ccpAvatarMedia"));
    }));
    [Fact]
    public Task UnplayedClaimExpiresAfterTheWpfLoadWindow() => AvaloniaTestDispatcher.RunAsync(() => WithShell((shell, host) =>
    {
        shell.ClaimAvatarBrowserSession();
        shell.CheckBrowserHeartbeat(DateTime.UtcNow.AddSeconds(59));
        Assert.True(shell.AvatarBrowserSessionActive);
        shell.CheckBrowserHeartbeat(DateTime.UtcNow.AddSeconds(61));
        Assert.False(shell.AvatarBrowserSessionActive);
        return Task.CompletedTask;
    }));
    [Fact]
    public Task ReleasingAClaimCancelsItsDelayedAutoplay() => AvaloniaTestDispatcher.RunAsync(() => WithShell(async (shell, host) =>
    {
        var scripts = new List<string>();
        host.NavigationInvoker = _ => true;
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.BrowserAutoplayDelay = () => delay.Task;
        shell.ClaimAvatarBrowserSession();
        var url = new Uri("https://127.0.0.1/video");
        Assert.True(shell.NavigateToUrlInBrowser(url.AbsoluteUri, autoPlayFullscreen: true));
        var field = typeof(MainShellWindow).GetField("_browserAutoplayGeneration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var pending = shell.StartBrowserAutoplay(host, url, (int)field.GetValue(shell)!);
        shell.EndAvatarBrowserSession();
        delay.SetResult();
        await pending;
        Assert.DoesNotContain(scripts, x => x.Contains("ccpAvatarMedia"));
    }));
    [Fact]
    public Task InvalidAutoplayAddressIsRefused() => AvaloniaTestDispatcher.RunAsync(() => WithShell((shell, host) =>
    {
        Assert.False(shell.NavigateToUrlInBrowser("not a URL", autoPlayFullscreen: true));
        return Task.CompletedTask;
    }));
    [Fact]
    public Task CompletedNavigationRequestsVideoOrPlaylistPlaybackAndTracksItsLifecycle() => AvaloniaTestDispatcher.RunAsync(() => WithShell(async (shell, host) =>
    {
        var scripts = new List<string>();
        host.NavigationInvoker = _ => true;
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        shell.BrowserAutoplayDelay = () => Task.CompletedTask;
        var video = new Uri("https://127.0.0.1/video");
        await CompanionLinkLauncher.OpenAsync(video.AbsoluteUri);
        Assert.DoesNotContain(scripts, script => script.Contains("ccpAvatarMedia"));
        host.OnNavigationCompleted(new Uri("https://127.0.0.1/previous"));
        Assert.DoesNotContain(scripts, script => script.Contains("ccpAvatarMedia"));
        host.OnNavigationCompleted(video);
        var script = Assert.Single(scripts, x => x.Contains("ccpAvatarMedia"));
        Assert.Contains("document.querySelector('video')", script);
        Assert.Contains("v.play()", script); Assert.Contains("duration:v?.duration", script);
        Assert.Contains("requestFullscreen", script);
        var gen = int.Parse(Regex.Match(script, @"const gen=(\d+)").Groups[1].Value);
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{gen},\"state\":\"playing\"}}");
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{gen},\"state\":\"fsExit\"}}");
        Assert.True(shell.AvatarBrowserSessionActive); Assert.False(shell.AvatarBrowserTakeover);
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{gen},\"state\":\"ended\"}}");
        Assert.False(shell.AvatarBrowserSessionActive);
        scripts.Clear();
        var playlist = new Uri("https://127.0.0.1/playlist/test");
        // The production branch chooses the BambiCloud action, without making a network request.
        var bambi = new Uri("https://bambicloud.com/playlist/test");
        Assert.True(MainShellWindow.IsBrowserPlaylist(bambi)); Assert.False(MainShellWindow.IsBrowserPlaylist(playlist));
        shell.ClaimAvatarBrowserSession();
        var field = typeof(MainShellWindow).GetField("_browserAutoplayGeneration", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await shell.StartBrowserAutoplay(host, bambi, (int)field.GetValue(shell)!);
        var playlistScript = Assert.Single(scripts, x => x.Contains("ccpAvatarMedia"));
        Assert.Contains("button.play-action", playlistScript);
        Assert.Contains("b.click()", playlistScript); Assert.Contains("duration:a?.duration", playlistScript); Assert.Contains("send('paused',a)", playlistScript);
        scripts.Clear();
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.BrowserAutoplayDelay = () => canceled.Task; await CompanionLinkLauncher.OpenAsync(video.AbsoluteUri);
        host.OnNavigationCompleted(video); shell.StopAvatarBrowserSession();
        canceled.SetResult(); await Task.Yield();
        Assert.DoesNotContain(scripts, x => x.Contains("ccpAvatarMedia"));
        shell.BrowserAutoplayDelay = () => Task.CompletedTask;
        await CompanionLinkLauncher.OpenAsync(video.AbsoluteUri); host.OnNavigationCompleted(video);
        var loopGen = (int)field.GetValue(shell)!;
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{loopGen - 1},\"state\":\"playing\",\"duration\":10}}");
        var hard = typeof(MainShellWindow).GetField("_avatarBrowserHardStop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Null(hard.GetValue(shell)); // a stale page cannot arm the new claim
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{loopGen},\"state\":\"playing\",\"duration\":10}}");
        var timer = Assert.IsType<DispatcherTimer>(hard.GetValue(shell));
        Assert.Equal(TimeSpan.FromSeconds(25), timer.Interval);
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{loopGen},\"state\":\"progress\",\"duration\":10}}");
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ScriptInvoker = text => { if (text.Contains("_ccpTakeoverStopped=true")) stopped.TrySetResult(); return Task.FromResult<string?>("ok"); };
        timer.Interval = TimeSpan.FromMilliseconds(1);
        Assert.Same(stopped.Task, await Task.WhenAny(stopped.Task, Task.Delay(5000))); Assert.False(shell.AvatarBrowserSessionActive);
        shell.ClaimAvatarBrowserSession(); var navGen = (int)field.GetValue(shell)!; host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{navGen},\"state\":\"navigated\"}}"); Assert.True(shell.AvatarBrowserSessionActive);
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{navGen},\"state\":\"playing\"}}"); host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{navGen},\"state\":\"navigated\"}}"); Assert.False(shell.AvatarBrowserSessionActive);
    }));
    [Fact]
    public Task SustainedPauseReleasesAndResumeTracksPlayback() => AvaloniaTestDispatcher.RunAsync(() => WithShell(async (shell, host) =>
    {
        var scripts = new List<string>(); host.NavigationInvoker = _ => true;
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>("ok"); };
        shell.BrowserAutoplayDelay = () => Task.CompletedTask;
        await CompanionLinkLauncher.OpenAsync("https://127.0.0.1/video");
        host.OnNavigationCompleted(new Uri("https://127.0.0.1/video"));
        var script = Assert.Single(scripts, s => s.Contains("ccpAvatarMedia"));
        Assert.Contains("2000", script); Assert.Contains("send('paused')", script);
        var gen = int.Parse(Regex.Match(script, @"const gen=(\d+)").Groups[1].Value);
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{gen},\"state\":\"playing\"}}");
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{gen},\"state\":\"paused\"}}");
        Assert.False(shell.AvatarBrowserSessionActive);
        host.OnWebMessage($"{{\"type\":\"ccpAvatarMedia\",\"generation\":{gen},\"state\":\"playing\"}}");
        Assert.True(shell.AvatarBrowserSessionActive); Assert.False(shell.AvatarBrowserTakeover);
    }));
}
