using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
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
                now = now.AddSeconds(19);
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"progress\"}");
                now = now.AddSeconds(19);
                tab.CheckBrowserMediaHeartbeat();
                Assert.True(tab.BrowserMediaPlaying);
                now = now.AddSeconds(1);
                tab.CheckBrowserMediaHeartbeat();
                Assert.False(tab.BrowserMediaPlaying);
                host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"playing\"}");
                host.OnNavigationCompleted(new Uri("https://example.com/next"));
                Assert.False(tab.BrowserMediaPlaying);
            }
            finally { host.OnWebMessage("{\"type\":\"ccpMedia\",\"state\":\"stopped\"}"); }
        });
    }
}
