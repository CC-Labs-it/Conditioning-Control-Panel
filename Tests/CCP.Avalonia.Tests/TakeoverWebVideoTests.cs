using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class TakeoverWebVideoTests
{
    private static void WithShell(Action<MainShellWindow, SettingsTabView, WebHost> test)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown, s.AvatarEnabled) = (true, true, 1, false);
            s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
            s.AutonomyAnnouncementChance = 0;
            s.ProtectBrowserVideoPlayback = false;
            s.AutonomyCanTriggerFlash = s.AutonomyCanTriggerVideo = s.AutonomyCanTriggerSubliminal = false;
            s.AutonomyCanTriggerBubbles = s.AutonomyCanComment = s.AutonomyCanTriggerMindWipe = false;
            s.AutonomyCanTriggerLockCard = s.AutonomyCanTriggerPinkFilter = s.AutonomyCanTriggerBouncingText = false;
            s.AutonomyCanTriggerWallpaper = s.AutonomyCanTriggerVoiceCommand = false;
            s.AutonomyCanTriggerWebVideo = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                var tab = shell.Named<SettingsTabView>("SettingsTab")!;
                var host = tab.FindControl<WebHost>("BrowserWebHost")!;
                shell.AutonomyVideoLinks = () => new Dictionary<string, string> { ["one"] = "http://127.0.0.1:3001/video" };
                s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
                Assert.True(shell.Autonomy.Start(withTimer: false));
                test(shell, tab, host);
            }
            finally
            {
                shell.Autonomy.Stop();
                shell.Close();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
    [Fact]
    public void PickerClaimsNavigatesAndPlaybackReleases() => WithShell((shell, tab, host) => {
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            var calls = 0;
            shell.AutonomyNavigate = (url, fullscreen, user) =>
            {
                calls++;
                Assert.Equal("http://127.0.0.1:3001/video", url); Assert.True(fullscreen);
                Assert.False(user); Assert.True(tab.BrowserMediaOwnsSlot);
                return shell.NavigateToUrlInBrowser(url, fullscreen, user);
            };
            shell.Autonomy.TestTrigger();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, calls); Assert.True(tab.BrowserMediaOwnsSlot);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\",\"reason\":\"navigated\"}"); Assert.True(tab.BrowserMediaOwnsSlot);
            host.OnNavigationCompleted(new Uri("http://127.0.0.1:3001/video"));
            Assert.Contains(SettingsTabView.BrowserTakeoverScript, scripts);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}"); Assert.True(tab.BrowserMediaPlaying);
            Assert.True(tab.BrowserMediaOwnsSlot);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}"); Assert.False(tab.BrowserMediaPlaying);
            Assert.False(tab.BrowserMediaOwnsSlot);
            var playlist = "https://bambicloud.com/playlist/example"; scripts.Clear();
            shell.AutonomyVideoLinks = () => new Dictionary<string, string> { ["playlist"] = playlist };
            shell.AutonomyNavigate = (url, full, user) => { Assert.Equal(playlist, url); Assert.True(full); Assert.False(user); tab.ArmBrowserAutoPlay(); return true; };
            shell.Autonomy.TestTrigger(); Dispatcher.UIThread.RunJobs();
            host.OnNavigationCompleted(new Uri(playlist));
            Assert.Contains(SettingsTabView.BrowserTakeoverScript, scripts);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}"); Assert.True(tab.BrowserMediaOwnsSlot);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}"); Assert.False(tab.BrowserMediaOwnsSlot);
        });
    [Fact]
    public void PickerRejectsMediaPlayingClaimMandatoryVideoAndFyp() => WithShell((shell, tab, host) => {
            bool Allowed() => shell.Autonomy.CanPerform(AutonomyActionType.WebVideo);
            Assert.True(Allowed());
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}"); Assert.False(Allowed());
            tab.ClearBrowserMediaClaim();
            Assert.True(tab.BrowserMediaPlaying); Assert.False(Allowed());
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}"); Assert.True(tab.TryBeginBrowserTakeover());
            Assert.False(Allowed());
            tab.EndBrowserTakeover();
            var oldVideo = CoreEngine.Video;
            var video = new MandatoryVideoScheduler(MandatoryVideoOverlay.Instance, library: () => new[] { "/v/a.mp4" });
            CoreEngine.Video = video;
            try
            {
                Assert.True(video.Trigger(path: "/v/a.mp4")); Assert.True(video.IsPlaying);
                Assert.False(Allowed());
            }
            finally { video.ForceCleanup(); CoreEngine.Video = oldVideo; }
            var fyp = new GameWindow(new GameWindow.Game(GameWindow.FypId, "tab_fyp", "", null));
            try { fyp.Show(); Assert.False(Allowed()); }
            finally { fyp.Close(); }
            Assert.True(Allowed());
        });
    [Fact]
    public void MissingLinksOrFailedNavigationReleaseClaim() => WithShell((shell, tab, host) => {
            var calls = 0;
            shell.AutonomyNavigate = (_, _, _) => { calls++; return false; };
            shell.AutonomyVideoLinks = () => new Dictionary<string, string>();
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            Assert.Equal(0, calls); Assert.False(tab.BrowserMediaOwnsSlot);
            shell.AutonomyVideoLinks = () => new Dictionary<string, string> { ["one"] = "https://example.com/" };
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            Assert.Equal(1, calls); Assert.False(tab.BrowserMediaOwnsSlot);
            shell.AutonomyNavigate = null;
            host.ScriptInvoker = _ => Task.FromResult<string?>(null);
            shell.PerformAutonomy(AutonomyActionType.WebVideo); // sandbox refuses the external URL
            Assert.True(host.RefusedNavigations > 0); Assert.False(tab.BrowserMediaOwnsSlot);
        });
    [Fact]
    public void CoolOffAndFullscreenExitFollowTheMediaSession() => WithShell((shell, tab, host) => {
            var now = DateTime.UtcNow;
            tab.BrowserMediaClock = () => now;
            CoreSettings.Current.ProtectBrowserVideoPlayback = true;
            CoreSettings.Current.BrowserVideoGraceSeconds = 5;
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}"); Assert.False(shell.Autonomy.CanPerform(AutonomyActionType.WebVideo));
            now = now.AddSeconds(6);
            Assert.True(shell.Autonomy.CanPerform(AutonomyActionType.WebVideo));
            host.ScriptInvoker = _ => Task.FromResult<string?>(null);
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":10}");
            host.OnWebMessage("{\"type\":\"ccpFsExit\"}"); Assert.True(tab.BrowserMediaPlaying);
            Assert.True(tab.BrowserMediaOwnsSlot);
            tab.ReplaceBrowserTakeover();
            Assert.True(tab.BrowserMediaPlaying); Assert.True(tab.BrowserMediaOwnsSlot);
        });
    private sealed class Scheduled : IDisposable { public void Dispose() { } }
    [Fact]
    public void ClipDurationStopsTakeoverAndReplaysDeferredVideo() => WithShell((shell, tab, host) => {
            var timers = new List<(Action action, TimeSpan delay)>();
            var scripts = new List<string>();
            tab.ScheduleBrowserMediaRecovery = (action, delay) => { timers.Add((action, delay)); return new Scheduled(); };
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":10}");
            var stop = timers.Find(t => t.delay == TimeSpan.FromSeconds(25)).action;
            Assert.NotNull(stop);
            var replayed = 0;
            Assert.True(tab.DeferVideoForBrowser(() => replayed++));
            stop();
            Assert.False(tab.BrowserMediaOwnsSlot); Assert.Equal(1, replayed);
            Assert.Contains(SettingsTabView.BrowserMediaRecoveryScript, scripts);
        });
    [Fact]
    public void EngineStopKeepsLoadingAndDurationStopAlive() => WithShell((shell, tab, host) => {
            var timers = new List<(Action action, TimeSpan delay)>(); var scripts = new List<string>();
            tab.ScheduleBrowserMediaRecovery = (action, delay) => { timers.Add((action, delay)); return new Scheduled(); };
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            shell.PerformAutonomy(AutonomyActionType.WebVideo); shell.OnEngineStopped();
            Assert.False(tab.BrowserMediaOwnsSlot); Assert.False(tab.TryBeginBrowserTakeover());
            host.OnNavigationCompleted(new Uri("http://127.0.0.1:3001/video"));
            Assert.Contains(SettingsTabView.BrowserTakeoverScript, scripts);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\",\"dur\":10}"); Assert.True(tab.BrowserMediaPlaying);
            timers.FindLast(t => t.delay == TimeSpan.FromSeconds(25)).action();
            Assert.False(tab.BrowserMediaPlaying); Assert.Contains(SettingsTabView.BrowserMediaRecoveryScript, scripts);
        });
    [Fact]
    public void AutonomousPlaybackRespectsThePlayersVideoLimit() => WithShell((shell, tab, host) => {
            CoreSettings.Current.VideoMaxDurationSeconds = 5;
            host.ScriptInvoker = _ => Task.FromResult<string?>(null);
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\",\"played\":4}"); Assert.True(tab.BrowserMediaOwnsSlot);
            host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\",\"played\":5}"); Assert.False(tab.BrowserMediaOwnsSlot);
        });
    [Fact]
    public void PanicAndReplacementCancelPendingPlaybackAndReleaseClaim() => WithShell((shell, tab, host) => {
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            Assert.True(tab.BrowserMediaOwnsSlot);
            tab.StopBrowserMediaForPanic();
            Assert.False(tab.BrowserMediaOwnsSlot);
            host.OnNavigationCompleted(new Uri("http://127.0.0.1:3001/video"));
            Assert.DoesNotContain(SettingsTabView.BrowserTakeoverScript, scripts);
            Assert.Contains(SettingsTabView.BrowserMediaRecoveryScript, scripts);
            scripts.Clear();
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            Assert.True(tab.BrowserMediaOwnsSlot); Assert.True(shell.NavigateToUrlInBrowser("http://127.0.0.1:3001/other"));
            Assert.False(tab.BrowserMediaOwnsSlot);
            host.OnNavigationCompleted(new Uri("http://127.0.0.1:3001/other"));
            Assert.DoesNotContain(SettingsTabView.BrowserTakeoverScript, scripts);
            Assert.Contains(SettingsTabView.BrowserMediaRecoveryScript, scripts);
        });
    [Fact]
    public void FailedLoadAndStalledLoadReleaseAndStopTakeover() => WithShell((shell, tab, host) => {
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            var now = DateTime.UtcNow;
            tab.BrowserMediaClock = () => now;
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            Assert.True(tab.BrowserMediaOwnsSlot);
            host.OnNavigationCompleted(new Uri("http://127.0.0.1:3001/video"), success: false);
            Assert.False(tab.BrowserMediaOwnsSlot);
            shell.PerformAutonomy(AutonomyActionType.WebVideo);
            Assert.True(tab.BrowserMediaOwnsSlot);
            var replayed = 0;
            Assert.True(tab.DeferVideoForBrowser(() => replayed++));
            now = now.AddSeconds(61);
            tab.CheckBrowserMediaHeartbeat();
            Assert.False(tab.BrowserMediaOwnsSlot); Assert.Equal(1, replayed);
            Assert.Contains(SettingsTabView.BrowserMediaRecoveryScript, scripts);
        });
}
