using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserAudioSyncTransportTests
{
    private static Task WithShell(Func<MainShellWindow, WebHost, List<string>, Task> check) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var settings = CoreSettings.Current.Haptics.AudioSync;
        var enabled = settings.Enabled;
        settings.Enabled = true;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = s => { scripts.Add(s); return Task.FromResult<string?>(null); };
            shell.AudioSyncConnectedOverride = () => true;
            await check(shell, host, scripts);
        }
        finally
        {
            shell.Close();
            settings.Enabled = enabled;
            Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public Task NavigationGatesInjectionAndConnectRearms() => WithShell(async (shell, host, scripts) =>
    {
        host.OnNavigationCompleted(new Uri("https://example.org/"));
        Assert.DoesNotContain(scripts, s => s.Contains("__hapticSyncInjected"));
        shell.AudioSyncConnectedOverride = () => false;
        host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
        Assert.DoesNotContain(scripts, s => s.Contains("__hapticSyncInjected"));
        shell.AudioSyncConnectedOverride = () => true;
        shell.AudioSyncConnectionChanged(null, true);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(scripts, s => s.Contains("__hapticSyncInjected"));
        Assert.Contains(scripts, s => s.Contains("__hapticRearm?."));
        scripts.Clear();
        host.OnNavigationCompleted(new Uri("https://www.hypnotube.com/watch"), false);
        Assert.DoesNotContain(scripts, s => s.Contains("__hapticSyncInjected"));
        host.OnNavigationCompleted(new Uri("https://www.hypnotube.com/watch"));
        Assert.Contains(scripts, s => s.Contains("__hapticSyncInjected"));
        await Task.CompletedTask;
    });

    [Fact]
    public Task MessagesRouteAndPanicRejectsLateCompletion() => WithShell(async (shell, host, scripts) =>
    {
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? url = null; TimeSpan? duration = null; double state = -1, seek = -1; bool paused = false; int ended = 0;
        shell.AudioSyncDetectOverride = (u, d) => { url = u; duration = d; return pending.Task; };
        shell.AudioSyncStateOverride = (t, p) => { state = t; paused = p; };
        shell.AudioSyncSeekOverride = t => seek = t;
        shell.AudioSyncEndedOverride = () => ended++;
        host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
        host.OnWebMessage("{\"type\":\"audioSyncVideoDetected\",\"url\":\"https://cdn.test/v.mp4\",\"duration\":42.5}");
        Assert.Equal("https://cdn.test/v.mp4", url);
        Assert.Equal(TimeSpan.FromSeconds(42.5), duration);
        host.OnWebMessage("{\"type\":\"audioSyncState\",\"currentTime\":3.5,\"paused\":true}");
        host.OnWebMessage("{\"type\":\"audioSyncSeek\",\"currentTime\":9}");
        host.OnWebMessage("{\"type\":\"audioSyncEnded\"}");
        Assert.Equal(3.5, state); Assert.True(paused); Assert.Equal(9, seek); Assert.Equal(1, ended);
        shell.PanicPauseBrowserMedia();
        Assert.Contains(scripts, s => s.Contains("__hapticCancel?."));
        pending.SetResult();
        await Task.Yield();
        Assert.DoesNotContain(scripts, s => s.Contains("__hapticSignalReady?."));
        host.OnWebMessage("{\"type\":\"audioSyncEnded\"}");
        Assert.Equal(1, ended);
    });

    [Fact]
    public Task SuccessfulOrFailedProcessingReleasesPageAndBadMessagesAreIgnored() => WithShell(async (shell, host, scripts) =>
    {
        shell.AudioSyncDetectOverride = (_, _) => throw new InvalidOperationException("decoder failed");
        host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
        host.OnWebMessage("not json");
        host.OnWebMessage("{\"type\":\"audioSyncVideoDetected\",\"url\":\"https://cdn.test/v.mp4\",\"duration\":null}");
        await Task.Yield();
        Assert.Contains(scripts, s => s.Contains("__hapticSignalReady?."));
        scripts.Clear();
        shell.AudioSyncDetectOverride = (_, _) => Task.CompletedTask;
        host.OnWebMessage("{\"type\":\"audioSyncVideoDetected\",\"url\":\"https://cdn.test/v.mp4\"}");
        Assert.Contains(scripts, s => s.Contains("__hapticSignalReady?."));
    });

    [Theory]
    [InlineData("audioSyncVideoDetected")]
    [InlineData("audioSyncState")]
    [InlineData("audioSyncSeek")]
    [InlineData("audioSyncEnded")]
    public Task EachMessageReachesTheService(string kind) => WithShell(async (shell, host, scripts) =>
    {
        var calls = 0;
        shell.AudioSyncDetectOverride = (_, _) => { calls++; return Task.CompletedTask; };
        shell.AudioSyncStateOverride = (_, _) => calls++;
        shell.AudioSyncSeekOverride = _ => calls++;
        shell.AudioSyncEndedOverride = () => calls++;
        host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
        host.OnWebMessage($"{{\"type\":\"{kind}\",\"url\":\"https://cdn.test/v.mp4\",\"currentTime\":1}}");
        Assert.Equal(1, calls);
        await Task.CompletedTask;
    });

    [Fact]
    public Task DisablingSyncCancelsInterception() => WithShell(async (shell, host, scripts) =>
    {
        host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
        scripts.Clear();
        CoreSettings.Current.Haptics.AudioSync.Enabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(scripts, s => s.Contains("__hapticCancel?."));
        await Task.CompletedTask;
    });

    [Fact]
    public Task ProgressAndSeekOverlayStopWithTheSession() => WithShell(async (shell, host, scripts) =>
    {
        host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
        scripts.Clear();
        shell.AudioSyncProgress(null, new ConditioningControlPanel.Services.Audio.ChunkProgressEventArgs(0, "Working", 37));
        shell.AudioSyncChunkLoading(null, 2);
        shell.AudioSyncChunkLoaded(null, EventArgs.Empty);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(scripts, s => s.Contains("__hapticUpdateProgress?.(37, \"Working\")"));
        Assert.Contains(scripts, s => s.Contains("__hapticShowOverlay?."));
        Assert.Contains(scripts, s => s.Contains("__hapticHideOverlay?."));
        scripts.Clear();
        shell.AudioSyncProgress(null, new ConditioningControlPanel.Services.Audio.ChunkProgressEventArgs(0, "Late", 99));
        shell.StopBrowserAudioSync();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(scripts, s => s.Contains("__hapticUpdateProgress?."));
        await Task.CompletedTask;
    });

    [Fact]
    public Task IdleNavigationDoesNotStopHaptics() => WithShell(async (shell, host, scripts) =>
    {
        shell.StopBrowserAudioSync();
        Assert.DoesNotContain(scripts, s => s.Contains("__hapticCancel?."));
        await Task.CompletedTask;
    });

    [Fact]
    public Task RealServiceStartsOffTheUiThread() => WithShell(async (shell, host, scripts) =>
    {
        var settings = new ConditioningControlPanel.Models.HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        foreach (var provider in new[] { "lovense", "buttplug" }) settings.V2.Provider(provider).Enabled = false;
        settings.V2.Provider("mock").Enabled = true;
        settings.AudioSync.Enabled = true;
        using var haptics = new ConditioningControlPanel.Services.HapticService(settings);
        Assert.True(await haptics.ConnectAsync());
        using var service = new ConditioningControlPanel.Services.BrowserAudioSyncService(haptics, settings.AudioSync,
            (_, _, _, _) => Array.Empty<float>());
        var field = typeof(MainShellWindow).GetField("_audioSyncService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var previous = field.GetValue(shell);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.ProcessingStarted += (_, _) => started.TrySetResult(Dispatcher.UIThread.CheckAccess());
        field.SetValue(shell, service);
        try
        {
            host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
            host.OnWebMessage("{\"type\":\"audioSyncVideoDetected\",\"url\":\"http://127.0.0.1:1/video.mp4\",\"duration\":1}");
            Assert.False(await started.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            shell.StopBrowserAudioSync();
        }
        finally { field.SetValue(shell, previous); }
    });

    [Fact]
    public Task LateInjectionIsCancelledAfterPanic() => WithShell(async (shell, host, scripts) =>
    {
        var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        host.ScriptInvoker = s => { scripts.Add(s); return s.Contains("__hapticSyncInjected") ? pending.Task : Task.FromResult<string?>(null); };
        host.OnNavigationCompleted(new Uri("https://hypnotube.com/watch"));
        shell.PanicPauseBrowserMedia();
        pending.SetResult(null);
        await Task.Yield();
        Assert.True(scripts.Count(s => s.Contains("__hapticCancel?.")) >= 2);
    });
}
