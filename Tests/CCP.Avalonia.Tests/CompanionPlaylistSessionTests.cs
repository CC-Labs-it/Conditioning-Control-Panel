using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

public sealed class CompanionPlaylistSessionTests
{
    [Fact]
    public Task WatchLinkProtectsTakeoverAndReleasesForEndFailureNavigationPanicAndTimeout() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.OfflineMode = false;
        settings.Current.ProtectBrowserVideoPlayback = true;
        (settings.Current.Welcomed, settings.Current.HasAcceptedAgeVerification,
            settings.Current.WhatMovedCardShown, settings.Current.AvatarEnabled) = (true, true, 1, false);
        var previousNavigate = CompanionLinkLauncher.Navigate;
        var shell = new MainShellWindow();
        var app = global::Avalonia.Application.Current!; var lifetimeField = typeof(global::Avalonia.Application).GetField("_applicationLifetime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var lifetime = lifetimeField.GetValue(app);
        lifetimeField.SetValue(app, new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime { MainWindow = shell });
        var now = DateTime.UtcNow;
        shell.PlaylistClock = () => now;
        shell.BrowserAutoPlayDelay = () => Task.CompletedTask;
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            CompanionLinkLauncher.Navigate = url => shell.NavigateToUrlInBrowser(url, true);
            var url = new Uri("https://127.0.0.1/bambicloud.com/playlist/1");
            Assert.False(shell.Autonomy.IsBusy());
            // The test sandbox refuses a real host. A refused request must release its claim.
            Assert.False(shell.NavigateToUrlInBrowser("https://bambicloud.com/playlist/1", true));
            Assert.False(shell.Autonomy.IsBusy());

            async Task<string> Open()
            {
                await CompanionLinkLauncher.OpenAsync(url.ToString());
                Assert.True(shell.Autonomy.IsBusy()); // claimed before the page loads
                host.OnNavigationCompleted(url);
                var script = Assert.Single(scripts, s => s.Contains("button.play-action"));
                scripts.Clear();
                var match = Regex.Match(script, "const token = '([0-9a-f]{32})';");
                Assert.True(match.Success);
                return match.Groups[1].Value;
            }
            void Send(string token, string state) => host.OnWebMessage($"ccp-playlist:{token}:{state}");

            await CompanionLinkLauncher.OpenAsync(url.ToString());
            Assert.True(shell.Autonomy.IsBusy());
            host.OnNavigationCompleted(url, success: false);
            Assert.False(shell.Autonomy.IsBusy());

            await CompanionLinkLauncher.OpenAsync(url.ToString());
            shell.StopBrowserMediaForPanic();
            host.OnNavigationCompleted(url);
            var passive = Assert.Single(scripts, s => s.Contains("'yes' === 'yes'"));
            Send(Regex.Match(passive, "const token = '([0-9a-f]{32})';").Groups[1].Value, "playing");
            Assert.True(shell.Autonomy.IsBusy());
            shell.StopBrowserMediaForPanic();
            scripts.Clear();
            var first = await Open();
            Send(first, "playing"); Send(first, "failed");
            Assert.True(shell.Autonomy.IsBusy());
            Assert.True(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance.BrowserMediaActive);
            Assert.True(BubbleCountHost.Instance.OtherInteractionActive);
            Send("stale", "ended");
            Assert.True(shell.Autonomy.IsBusy());
            Send(first, "ended"); Assert.False(shell.Autonomy.IsBusy());
            Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance.BrowserMediaActive);
            Assert.False(BubbleCountHost.Instance.OtherInteractionActive);
            Send(first, "playing"); // WPF opens a user session if audio resumes
            Assert.True(shell.Autonomy.IsBusy());
            host.OnNavigationCompleted(new Uri("https://127.0.0.1/bambicloud.com/other"));
            Assert.False(shell.Autonomy.IsBusy());
            Send(first, "playing");
            Assert.False(shell.Autonomy.IsBusy());

            var second = await Open();
            Send(first, "ended");
            Assert.True(shell.Autonomy.IsBusy());
            Send(second, "failed");
            Assert.False(shell.Autonomy.IsBusy());
            Send(second, "playing"); Assert.True(shell.Autonomy.IsBusy()); Send(second, "ended");

            var third = await Open();
            shell.StopBrowserMediaForPanic();
            Assert.False(shell.Autonomy.IsBusy());
            Send(third, "playing");
            Assert.False(shell.Autonomy.IsBusy());
            var resumed = Regex.Match(Assert.Single(scripts, s => s.Contains("'yes' === 'yes'")), "const token = '([0-9a-f]{32})';").Groups[1].Value;
            Send(resumed, "playing"); Assert.True(shell.Autonomy.IsBusy());
            Send(resumed, "ended"); Assert.False(shell.Autonomy.IsBusy());
            scripts.Clear();

            await CompanionLinkLauncher.OpenAsync(url.ToString());
            now = now.AddSeconds(59);
            shell.CheckPlaylistTimeout();
            Assert.True(shell.Autonomy.IsBusy());
            now = now.AddSeconds(1);
            shell.CheckPlaylistTimeout();
            Assert.False(shell.Autonomy.IsBusy());
            scripts.Clear();

            var fourth = await Open();
            Send(fourth, "playing");
            now = now.AddSeconds(19);
            Send(fourth, "progress");
            now = now.AddSeconds(19);
            shell.CheckPlaylistTimeout();
            Assert.True(shell.Autonomy.IsBusy());
            now = now.AddSeconds(1);
            shell.CheckPlaylistTimeout();
            Assert.False(shell.Autonomy.IsBusy());
            var resumedAfterTimeout = Regex.Match(Assert.Single(scripts, s => s.Contains("'yes' === 'yes'")), "const token = '([0-9a-f]{32})';").Groups[1].Value;
            Send(resumedAfterTimeout, "playing");
            Assert.True(shell.Autonomy.IsBusy());
            Send(resumedAfterTimeout, "ended");
            Assert.False(shell.Autonomy.IsBusy());
            scripts.Clear();

            // WPF's queue claim is best effort. If it was busy, playback protection still applies.
            var schedulerBusy = shell.Autonomy.IsBusy;
            shell.Autonomy.IsBusy = () => true;
            var fifth = await Open();
            shell.Autonomy.IsBusy = schedulerBusy;
            Assert.False(shell.Autonomy.IsBusy());
            Send(fifth, "playing");
            Assert.True(shell.Autonomy.IsBusy());
            settings.Current.ProtectBrowserVideoPlayback = false;
            Assert.False(shell.Autonomy.IsBusy());
            settings.Current.ProtectBrowserVideoPlayback = true;
            Assert.True(shell.Autonomy.IsBusy());
            Send(fifth, "navigated");
            Assert.False(shell.Autonomy.IsBusy());
        }
        finally
        {
            CompanionLinkLauncher.Navigate = previousNavigate;
            lifetimeField.SetValue(app, lifetime);
            shell.Close();
            Dispatcher.UIThread.RunJobs();
        }
    });
}
