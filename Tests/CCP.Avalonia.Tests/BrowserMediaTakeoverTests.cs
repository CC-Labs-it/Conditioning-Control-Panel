using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserMediaTakeoverTests
{
    [Fact]
    public void NavigationAndMediaReportsDeferScheduledTakeoverUntilPlaybackStops()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var oldSettings = CoreSettings.ServiceProvider;
            var oldPremium = CoreEntitlement.HasPremiumProvider;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.AvatarEnabled) = (true, true, 1, false);
            s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
            s.AutonomyRandomTriggerEnabled = true;
            s.AutonomyIdleTriggerEnabled = false;
            s.AutonomyRandomIntervalMinutes = 1;
            s.AutonomyCooldownSeconds = 0;
            s.AutonomyAnnouncementChance = 0;
            s.AutonomyCanTriggerFlash = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
                var tab = shell.Named<SettingsTabView>("SettingsTab")!;
                var host = tab.FindControl<WebHost>("BrowserWebHost")!;
                var scripts = new List<string>();
                host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
                host.OnNavigationCompleted(new Uri("https://example.com/"));
                Assert.Contains(scripts, script => script == SettingsTabView.BrowserMediaReporter);

                var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                tab.BrowserMediaClock = () => now;
                shell.Autonomy.Clock = () => now;
                shell.Autonomy.CanPerform = a => a == AutonomyActionType.Flash;
                var performed = 0;
                shell.Autonomy.Perform = (_, _) => performed++;
                Assert.True(shell.Autonomy.Start(withTimer: false));
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
                Assert.True(tab.BrowserMediaPlaying);
                Assert.True(shell.Autonomy.IsBusy());
                // WPF user playback claims the queue even when the separate protection switch is off.
                s.ProtectBrowserVideoPlayback = false;
                Assert.True(shell.Autonomy.IsBusy());
                now = shell.Autonomy.NextRandomFire.AddSeconds(1);
                shell.Autonomy.Tick();
                Assert.Equal(0, performed);
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\",\"reason\":\"ended\"}");
                Assert.False(shell.Autonomy.IsBusy());
                now = shell.Autonomy.NextRandomFire.AddSeconds(1);
                shell.Autonomy.Tick();
                Assert.Equal(1, performed);
            }
            finally
            {
                shell.Autonomy.Stop();
                shell.Close();
                Dispatcher.UIThread.RunJobs();
                CoreEntitlement.HasPremiumProvider = oldPremium;
                CoreSettings.ServiceProvider = oldSettings;
            }
        });
    }

    // WPF BrowserMediaService.cs:248-257 claims the queue for user playback even with
    // protection off. VideoService.cs:2948-2959 and BubbleCountService.cs:424-431 defer.
    private static void WithDesktopBrowser(Action<WebHost> check)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var app = Application.Current!;
            // SetupWithoutStarting freezes the public setter; replace and restore the backing field
            // only for this headless test so Current follows a real desktop lifetime.
            var lifetimeField = typeof(Application).GetField("_applicationLifetime",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var oldLifetime = lifetimeField.GetValue(app);
            var settings = CoreSettings.Current;
            var oldProtect = settings.ProtectBrowserVideoPlayback;
            var lifetime = new ClassicDesktopStyleApplicationLifetime();
            var shell = new MainShellWindow();
            WebHost? host = null;
            try
            {
                lifetimeField.SetValue(app, lifetime);
                lifetime.MainWindow = shell;
                settings.ProtectBrowserVideoPlayback = false;
                var tab = shell.Named<SettingsTabView>("SettingsTab")!;
                host = tab.FindControl<WebHost>("BrowserWebHost")!;
                check(host);
            }
            finally
            {
                host?.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
                shell.Close();
                lifetimeField.SetValue(app, oldLifetime);
                settings.ProtectBrowserVideoPlayback = oldProtect;
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    public void BrowserPlaybackReachesScheduledVideoHost() => WithDesktopBrowser(host =>
    {
        var video = MandatoryVideoOverlay.Instance;
        Assert.False(video.BrowserMediaActive);
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
        Assert.True(video.BrowserMediaActive);
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
        Assert.False(video.BrowserMediaActive);
    });

    [Fact]
    public void BrowserPlaybackQueuesBubbleCountUntilItStops() => WithDesktopBrowser(host =>
    {
        var clock = new BubbleCountQueueTests.Clock();
        var bubbleHost = BubbleCountHost.Instance;
        var game = new BubbleCountScheduler(bubbleHost, clock, () => new[] { "/none/clip.mp4" });
        try
        {
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            game.Trigger(forceTest: true);
            Assert.True(game.IsQueued);
            Assert.False(game.IsBusy);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
            clock.Advance(BubbleCountScheduler.QueuePoll);
            Dispatcher.UIThread.RunJobs();
            Assert.False(game.IsQueued);
            Assert.True(game.IsBusy);
        }
        finally { game.ForceCleanup(); }
    });

    // WPF BrowserMediaService.cs:248-257 makes a single best-effort claim; a failed
    // claim is not retried on another playing report after the other interaction ends.
    [Theory]
    [InlineData("card")]
    [InlineData("video")]
    [InlineData("bubble")]
    public void PlaybackStartedDuringAnotherInteractionDoesNotReclaimItsSlot(string occupied) => WithDesktopBrowser(host =>
    {
        var clock = new BubbleCountQueueTests.Clock();
        var oldVideo = CoreEngine.Video;
        var oldBubble = CoreEngine.BubbleCount;
        var video = new MandatoryVideoScheduler(MandatoryVideoOverlay.Instance, clock, () => new[] { "/none/clip.mp4" });
        var bubble = new BubbleCountScheduler(BubbleCountHost.Instance, clock, () => new[] { "/none/clip.mp4" });
        var shell = MainShellWindow.Current!;
        var settings = CoreSettings.Current;
        try
        {
            CoreEngine.Video = video;
            CoreEngine.BubbleCount = bubble;
            if (occupied == "card") LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);
            if (occupied == "video") Assert.True(video.Trigger()); // Includes pre-roll before a window opens.
            if (occupied == "bubble") { bubble.Trigger(forceTest: true); Assert.True(bubble.IsBusy); }
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            Assert.False(shell.BrowserMediaOwnsSlot);
            LockCardWindow.ForceCloseAll();
            video.ForceCleanup();
            bubble.ForceCleanup();
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            Assert.False(shell.BrowserMediaOwnsSlot);
            Assert.False(BubbleCountHost.Instance.OtherInteractionActive);
            Assert.False(MandatoryVideoOverlay.Instance.BrowserMediaActive);
            Assert.False(shell.Autonomy.IsBusy());
            bubble.Trigger(forceTest: true);
            Assert.True(bubble.IsBusy); // The next game starts despite the still-playing browser.
            bubble.ForceCleanup();
            settings.ProtectBrowserVideoPlayback = true;
            Assert.True(MandatoryVideoOverlay.Instance.BrowserMediaActive);
            Assert.True(shell.Autonomy.IsBusy());
            Assert.False(BubbleCountHost.Instance.OtherInteractionActive);
            settings.ProtectBrowserVideoPlayback = false;
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            Assert.True(shell.BrowserMediaOwnsSlot); // A new session gets a new attempt.
        }
        finally
        {
            LockCardWindow.ForceCloseAll();
            video.ForceCleanup();
            bubble.ForceCleanup();
            CoreEngine.Video = oldVideo;
            CoreEngine.BubbleCount = oldBubble;
        }
    });

    [Fact]
    public void ProtectionDefersInterruptionsAfterAnUnsuccessfulClaim() => WithDesktopBrowser(host =>
    {
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            LockCardWindow.ForceCloseAll();
            Assert.False(MainShellWindow.Current!.BrowserMediaOwnsSlot);
            CoreSettings.Current.ProtectBrowserVideoPlayback = true;
            Assert.True(MandatoryVideoOverlay.Instance.BrowserMediaActive);
            Assert.True(MainShellWindow.Current!.Autonomy.IsBusy());
            Assert.False(BubbleCountHost.Instance.OtherInteractionActive);
        }
        finally { LockCardWindow.ForceCloseAll(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EngineStopReleasesClaimWithoutEndingPlayback(bool protect) => WithDesktopBrowser(host =>
    {
        var shell = MainShellWindow.Current!;
        var tab = shell.Named<SettingsTabView>("SettingsTab")!;
        var clock = new BubbleCountQueueTests.Clock();
        var game = new BubbleCountScheduler(BubbleCountHost.Instance, clock, () => new[] { "/none/clip.mp4" });
        var oldHook = CoreEngine.StoppedHook;
        Action? staleRecovery = null;
        var scripts = new List<string>();
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
        tab.ScheduleBrowserMediaRecovery = (action, delay) =>
        {
            staleRecovery = action;
            return clock.CreateTimer(_ => action(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
        };
        CoreSettings.Current.ProtectBrowserVideoPlayback = protect;
        CoreEngine.StoppedHook = shell.OnEngineStopped;
        try
        {
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            Assert.True(tab.BrowserMediaOwnsSlot);
            var firstRecovery = staleRecovery!;
            MainShellWindow.StopEngine();
            Assert.True(tab.BrowserMediaPlaying);
            Assert.False(tab.BrowserMediaOwnsSlot);
            Assert.Equal(protect, shell.Autonomy.IsBusy());
            Assert.Equal(protect, MandatoryVideoOverlay.Instance.BrowserMediaActive);
            game.Trigger(forceTest: true);
            Assert.True(game.IsBusy);
            game.ForceCleanup();
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            Assert.False(tab.BrowserMediaOwnsSlot); // Still the same user session.
            firstRecovery(); // A callback already queued before Stop must not end playback.
            Assert.True(tab.BrowserMediaPlaying);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            Assert.True(tab.BrowserMediaOwnsSlot);
            firstRecovery(); // Nor may the old claim end this new session.
            Assert.True(tab.BrowserMediaPlaying);
            Assert.True(tab.BrowserMediaOwnsSlot);
            Assert.Empty(scripts);
        }
        finally
        {
            game.ForceCleanup();
            CoreEngine.StoppedHook = oldHook;
            host.ScriptInvoker = null;
        }
    });

    [Fact]
    public void UnknownDurationRecoversClaimDespiteOngoingHeartbeats() => WithDesktopBrowser(host =>
    {
        var tab = MainShellWindow.Current!.Named<SettingsTabView>("SettingsTab")!;
        var clock = new BubbleCountQueueTests.Clock();
        var scripts = new List<string>();
        tab.BrowserMediaClock = () => clock.GetUtcNow().UtcDateTime;
        tab.ScheduleBrowserMediaRecovery = (action, delay) =>
            clock.CreateTimer(_ => action(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
        try
        {
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":0}");
            for (var i = 0; i < 362; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\",\"dur\":0}");
            }
            clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\",\"dur\":0}");
            Assert.True(tab.BrowserMediaPlaying);
            Assert.True(tab.BrowserMediaOwnsSlot);
            Assert.Empty(scripts);
            clock.Advance(TimeSpan.FromTicks(1)); // 3600 s fallback + 30 s queue buffer.
            Assert.False(tab.BrowserMediaPlaying);
            Assert.False(tab.BrowserMediaOwnsSlot);
            Assert.Equal(new[] { SettingsTabView.BrowserMediaRecoveryScript }, scripts);
        }
        finally { host.ScriptInvoker = null; }
    });

    [Fact]
    public void OldRecoveryCallbackCannotEndANewSession() => WithDesktopBrowser(host =>
    {
        var tab = MainShellWindow.Current!.Named<SettingsTabView>("SettingsTab")!;
        var clock = new BubbleCountQueueTests.Clock();
        var callbacks = new List<Action>();
        var scripts = new List<string>();
        tab.ScheduleBrowserMediaRecovery = (action, delay) =>
        {
            callbacks.Add(action);
            return clock.CreateTimer(_ => action(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
        };
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
        try
        {
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            callbacks[0]();
            Assert.True(tab.BrowserMediaPlaying);
            Assert.True(tab.BrowserMediaOwnsSlot);
            Assert.Empty(scripts);
        }
        finally { host.ScriptInvoker = null; }
    });

    [Fact]
    public void OnlyPlayingReportsRearmRecovery() => WithDesktopBrowser(host =>
    {
        var tab = MainShellWindow.Current!.Named<SettingsTabView>("SettingsTab")!;
        var clock = new BubbleCountQueueTests.Clock();
        var arms = 0;
        tab.ScheduleBrowserMediaRecovery = (action, delay) =>
        {
            arms++;
            return clock.CreateTimer(_ => action(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
        };
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":600}");
        Assert.Equal(1, arms);
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\",\"dur\":800}");
        Assert.Equal(1, arms);
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":0}");
        Assert.Equal(2, arms);
    });

    [Fact]
    public void RecoveryDelayUsesWpfDurationBuffers() => WithDesktopBrowser(host =>
    {
        var tab = MainShellWindow.Current!.Named<SettingsTabView>("SettingsTab")!;
        var clock = new BubbleCountQueueTests.Clock();
        var delays = new List<TimeSpan>();
        tab.ScheduleBrowserMediaRecovery = (action, delay) =>
        {
            delays.Add(delay);
            return clock.CreateTimer(_ => action(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
        };
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":600}");
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":10}");
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}");
        host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":0}");
        Assert.Equal(new[] { TimeSpan.FromSeconds(645), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(3630) }, delays);
    });

    [Fact]
    public void RecoveryUsesReportedDurationAndOnlyPlayingRearmsIt() => WithDesktopBrowser(host =>
    {
        var tab = MainShellWindow.Current!.Named<SettingsTabView>("SettingsTab")!;
        var clock = new BubbleCountQueueTests.Clock();
        var scripts = new List<string>();
        tab.BrowserMediaClock = () => clock.GetUtcNow().UtcDateTime;
        tab.ScheduleBrowserMediaRecovery = (action, delay) =>
            clock.CreateTimer(_ => action(), null, delay, System.Threading.Timeout.InfiniteTimeSpan);
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
        try
        {
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":600}");
            for (var i = 0; i < 10; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\"}");
            }
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":0}"); // Keep 600 s and rearm.
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\",\"dur\":800}"); // Update but do not rearm a user session.
            for (var i = 0; i < 64; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\"}");
            }
            clock.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
            Assert.True(tab.BrowserMediaOwnsSlot);
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.False(tab.BrowserMediaPlaying);
            Assert.Single(scripts);
            scripts.Clear();
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":10}");
            for (var i = 0; i < 29; i++)
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\"}");
            }
            clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
            Assert.True(tab.BrowserMediaOwnsSlot); // WPF queue's 5-minute minimum.
            clock.Advance(TimeSpan.FromTicks(1));
            Assert.False(tab.BrowserMediaPlaying);
            Assert.Single(scripts);
        }
        finally { host.ScriptInvoker = null; }
    });

    [Fact]
    public void HeartbeatsKeepPlaybackBusyAndTimeoutOrNavigationReleasesIt()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var tab = new SettingsTabView();
            var host = tab.FindControl<WebHost>("BrowserWebHost")!;
            var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            tab.BrowserMediaClock = () => now;
            try
            {
                host.OnWebMessage("not json");
                host.OnWebMessage("{\"type\":\"other\",\"state\":\"playing\"}");
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\"}");
                Assert.False(tab.BrowserMediaPlaying);
                host.OnWebMessage(JsonSerializer.Serialize("{\"type\":\"ccpMedia\",\"state\":\"playing\"}"));
                Assert.True(tab.BrowserMediaPlaying);
                Assert.True(tab.BrowserMediaOwnsSlot);
                host.OnNavigationCompleted(new Uri("https://example.com/failed"), success: false);
                Assert.True(tab.BrowserMediaPlaying);
                host.OnNavigationCompleted(new Uri("https://example.com/#part"));
                Assert.True(tab.BrowserMediaPlaying);
                now = now.AddSeconds(19);
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\"}");
                now = now.AddSeconds(19);
                tab.CheckBrowserMediaHeartbeat();
                Assert.True(tab.BrowserMediaPlaying);
                now = now.AddSeconds(1);
                tab.CheckBrowserMediaHeartbeat();
                Assert.False(tab.BrowserMediaPlaying);
                Assert.False(tab.BrowserMediaOwnsSlot);
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\",\"reason\":\"navigated\"}");
                host.OnNavigationCompleted(new Uri("https://example.com/next"));
                Assert.False(tab.BrowserMediaPlaying);
            }
            finally { host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}"); }
        });
    }
}
