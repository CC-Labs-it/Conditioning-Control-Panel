using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CCP.Tests.Shared;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace CCP.Avalonia.Tests;

public class HomeBrowserAudioSyncTests
{
    [Fact]
    public Task NavigationRequiresEnabledConnectedHypnoTube() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreHaptics.Service;
        var enabled = CoreSettings.Current.Haptics.AudioSync.Enabled;
        var settings = new HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        settings.V2.Provider("lovense").Enabled = false;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        using var haptics = new HapticService(settings);
        try
        {
            CoreHaptics.Service = haptics;
            var tab = new SettingsTabView();
            var host = tab.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            CoreSettings.Current.Haptics.AudioSync.Enabled = true;
            host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
            Assert.DoesNotContain(scripts, s => s.Contains(BrowserAudioSyncScript.Source));
            Assert.True(await haptics.ConnectAsync());
            host.OnNavigationCompleted(new Uri("https://example.com/watch"));
            host.OnNavigationCompleted(new Uri("https://nothypnotube.com/watch"));
            Assert.DoesNotContain(scripts, s => s.Contains(BrowserAudioSyncScript.Source));
            CoreSettings.Current.Haptics.AudioSync.Enabled = false;
            host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
            Assert.DoesNotContain(scripts, s => s.Contains(BrowserAudioSyncScript.Source));
            CoreSettings.Current.Haptics.AudioSync.Enabled = true;
            host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
            Assert.Single(scripts.Where(s => s.Contains(BrowserAudioSyncScript.Source)));
        }
        finally { CoreHaptics.Service = previous; CoreSettings.Current.Haptics.AudioSync.Enabled = enabled; }
    });

    [Theory]
    [InlineData("{\"type\":\"audioSyncVideoDetected\"}")]
    [InlineData("{\"type\":\"audioSyncVideoDetected\",\"url\":\"https://example.com/video.mp4\"}")]
    public Task MissingServiceOrUrlSignalsReady(string message) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var tab = new SettingsTabView();
        var host = tab.FindControl<WebHost>("BrowserWebHost")!;
        var scripts = new List<string>();
        host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
        host.OnWebMessage(message);
        Assert.Contains(scripts, s => s.Contains("__hapticSignalReady"));
        return Task.CompletedTask;
    });
    [Fact]
    public Task VideoMessagesReachServiceAndReportProgress() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var oldHaptics = CoreHaptics.Service;
        var oldService = AudioSyncHost.Service;
        var settings = new HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        settings.V2.Provider("lovense").Enabled = false;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        settings.AudioSync.Enabled = true;
        using var haptics = new HapticService(settings);
        var failDecode = false;
        using var sync = new BrowserAudioSyncService(haptics, settings.AudioSync,
            (_, _, _, _) => failDecode ? throw new InvalidOperationException("decoder failed")
                : Enumerable.Range(0, 44100).Select(i => (float)Math.Sin(i * .1)).ToArray());
        using var server = new FakeServer();
        server.Handle = context => FakeServer.Send(context, new byte[] { 1 });
        var field = typeof(AudioSyncHost).GetField("<Service>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)!;
        Window? window = null;
        try
        {
            Assert.True(await haptics.ConnectAsync());
            CoreHaptics.Service = haptics;
            field.SetValue(null, sync);
            var tab = new SettingsTabView();
            window = new Window { Content = tab };
            window.Show();
            Assert.Same(sync, typeof(SettingsTabView).GetField("_audioSyncService", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tab));
            var host = tab.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.OnWebMessage("{\"type\":\"audioSyncVideoDetected\"}");
            Assert.Contains(scripts, s => s.Contains("__hapticSignalReady"));
            scripts.Clear();
            var completed = 0;
            var startedOnUi = true;
            sync.ProcessingStarted += (_, _) => startedOnUi = Dispatcher.UIThread.CheckAccess();
            sync.ProcessingCompleted += (_, _) => completed++;
            host.OnWebMessage("{\"type\":\"audioSyncVideoDetected\",\"url\":\"https://example.com/page.html\",\"duration\":null}");
            for (var i = 0; i < 100 && !scripts.Any(s => s.Contains("__hapticSignalReady")); i++) await Task.Delay(50);
            Assert.Equal(1, completed);
            Assert.Contains(scripts, s => s.Contains("__hapticSignalReady"));
            scripts.Clear();
            host.OnWebMessage(System.Text.Json.JsonSerializer.Serialize(new { type = "audioSyncVideoDetected", url = server.Prefix + "video.mp4", duration = 1 }));
            for (var i = 0; i < 150 && (!scripts.Any(s => s.Contains("__hapticSignalReady")) || !scripts.Any(s => s.Contains("__hapticUpdateProgress"))); i++) await Task.Delay(100);
            Assert.True(sync.IsReadyToPlay);
            Assert.False(startedOnUi);
            Assert.Contains(scripts, s => s.Contains("__hapticUpdateProgress"));
            Assert.Contains(scripts, s => s.Contains("__hapticSignalReady"));
            host.OnWebMessage("{\"type\":\"audioSyncState\",\"currentTime\":0,\"paused\":true}");
            var paused = typeof(BrowserAudioSyncService).GetField("_isPaused", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((bool)paused.GetValue(sync)!);
            host.OnWebMessage("{\"type\":\"audioSyncEnded\"}");
            Assert.False((bool)paused.GetValue(sync)!);
            host.OnWebMessage("{\"type\":\"audioSyncSeek\",\"currentTime\":301}");
            for (var i = 0; i < 100 && !scripts.Any(s => s.Contains("__hapticHideOverlay")); i++) await Task.Delay(100);
            Assert.Contains(scripts, s => s.Contains("__hapticShowOverlay"));
            Assert.Contains(scripts, s => s.Contains("__hapticHideOverlay"));
            var errors = 0;
            sync.Error += (_, _) => errors++;
            failDecode = true;
            scripts.Clear();
            host.OnWebMessage(System.Text.Json.JsonSerializer.Serialize(new { type = "audioSyncVideoDetected", url = server.Prefix + "failure.mp4", duration = 1 }));
            for (var i = 0; i < 150 && !scripts.Any(s => s.Contains("__hapticSignalReady")); i++) await Task.Delay(100);
            Assert.True(errors > 0);
            Assert.Contains(scripts, s => s.Contains("__hapticSignalReady"));
        }
        finally
        {
            window?.Close();
            field.SetValue(null, oldService);
            CoreHaptics.Service = oldHaptics;
        }
    });

    [Fact]
    public Task LaterConnectionRearmsCurrentPage() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var previous = CoreHaptics.Service;
        var enabled = CoreSettings.Current.Haptics.AudioSync.Enabled;
        var settings = new HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        settings.V2.Provider("lovense").Enabled = false;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        using var haptics = new HapticService(settings);
        Window? window = null;
        try
        {
            CoreHaptics.Service = haptics;
            CoreSettings.Current.Haptics.AudioSync.Enabled = true;
            var tab = new SettingsTabView();
            window = new Window { Content = tab };
            window.Show();
            var host = tab.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
            Assert.True(await haptics.ConnectAsync());
            for (var i = 0; i < 100 && !scripts.Any(s => s == "if(window.__hapticRearm) window.__hapticRearm();"); i++) await Task.Delay(50);
            Assert.Contains(scripts, s => s.Contains(BrowserAudioSyncScript.Source));
            Assert.Contains(scripts, s => s == "if(window.__hapticRearm) window.__hapticRearm();");
        }
        finally
        {
            window?.Close();
            CoreHaptics.Service = previous;
            CoreSettings.Current.Haptics.AudioSync.Enabled = enabled;
        }
    });

}
