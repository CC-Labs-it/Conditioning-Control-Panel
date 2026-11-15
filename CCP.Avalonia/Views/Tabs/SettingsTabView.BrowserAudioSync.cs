using System;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using ConditioningControlPanel.Services.Browser;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    private HapticService? _audioSyncHaptics;
    private BrowserAudioSyncService? _audioSyncService;
    private int _audioSyncPage;

    private void HookBrowserAudioSync()
    {
        BrowserWebHost.NavigationBeginning += () => _audioSyncPage++;
        BrowserWebHost.NavigationFinished += (url, success) =>
        {
            if (success && AudioSyncEligible(url)) _ = InjectAudioSyncAsync(false);
        };
        BrowserWebHost.WebMessage += OnBrowserAudioSyncMessage;
        AttachedToVisualTree += (_, _) => BindBrowserAudioSync();
        DetachedFromVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!this.IsAttachedToVisualTree()) UnbindBrowserAudioSync();
        });
    }

    private void BindBrowserAudioSync()
    {
        UnbindBrowserAudioSync();
        _audioSyncHaptics = CoreHaptics.Service;
        if (_audioSyncHaptics != null) _audioSyncHaptics.ConnectionChanged += AudioSyncConnectionChanged;
        _audioSyncService = AudioSyncHost.Service;
        if (_audioSyncService == null) return;
        _audioSyncService.ProcessingProgress += AudioSyncProgress;
        _audioSyncService.ChunkLoadingRequired += AudioSyncChunkLoading;
        _audioSyncService.ChunkLoadingCompleted += AudioSyncChunkLoaded;
    }

    private void UnbindBrowserAudioSync()
    {
        if (_audioSyncHaptics != null) _audioSyncHaptics.ConnectionChanged -= AudioSyncConnectionChanged;
        if (_audioSyncService != null)
        {
            _audioSyncService.ProcessingProgress -= AudioSyncProgress;
            _audioSyncService.ChunkLoadingRequired -= AudioSyncChunkLoading;
            _audioSyncService.ChunkLoadingCompleted -= AudioSyncChunkLoaded;
        }
        _audioSyncHaptics = null;
        _audioSyncService = null;
    }

    private static bool AudioSyncEligible(Uri? url) =>
        url != null && BrowserPopupRule.IsPartner(url.ToString())
        && CoreSettings.Current.Haptics.AudioSync.Enabled && CoreHaptics.Service?.IsConnected == true;

    private async Task InjectAudioSyncAsync(bool rearm)
    {
        // WebKit exposes invokeCSharpAction; WebView2 exposes chrome.webview.
        await BrowserWebHost.InvokeScriptAsync("if(!window.invokeCSharpAction && window.chrome?.webview) window.invokeCSharpAction=m=>window.chrome.webview.postMessage(m);\n" + BrowserAudioSyncScript.Source);
        if (rearm) await BrowserWebHost.InvokeScriptAsync("if(window.__hapticRearm) window.__hapticRearm();");
    }

    private void AudioSyncConnectionChanged(object? sender, bool connected)
    {
        if (!connected) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (AudioSyncEligible(BrowserWebHost.CurrentUrl)) _ = InjectAudioSyncAsync(true);
        });
    }

    private void AudioSyncProgress(object? sender, ChunkProgressEventArgs e) => Dispatcher.UIThread.Post(() =>
        _ = BrowserWebHost.InvokeScriptAsync($"if(window.__hapticUpdateProgress) window.__hapticUpdateProgress({e.PercentComplete}, {JsonSerializer.Serialize(e.Status)});"));
    private void AudioSyncChunkLoading(object? sender, int chunk) => Dispatcher.UIThread.Post(() =>
        _ = BrowserWebHost.InvokeScriptAsync("document.querySelector('video')?.pause(); if(window.__hapticShowOverlay) window.__hapticShowOverlay('Loading haptic data...');"));
    private void AudioSyncChunkLoaded(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        _ = BrowserWebHost.InvokeScriptAsync("if(window.__hapticHideOverlay) window.__hapticHideOverlay(); document.querySelector('video')?.play();"));

    private async void OnBrowserAudioSyncMessage(string message)
    {
        try
        {
            using var json = JsonDocument.Parse(message);
            var root = json.RootElement;
            if (!root.TryGetProperty("type", out var type)) return;
            var service = AudioSyncHost.Service;
            switch (type.GetString())
            {
                case "audioSyncVideoDetected":
                    var page = ++_audioSyncPage;
                    try
                    {
                        if (service != null && root.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(url.GetString()))
                        {
                            TimeSpan? duration = null;
                            if (root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds) && seconds > 0 && seconds <= 86400) duration = TimeSpan.FromSeconds(seconds);
                            var videoUrl = url.GetString()!;
                            await Task.Run(() => service.OnVideoDetectedAsync(videoUrl, duration));
                        }
                    }
                    finally
                    {
                        if (page == _audioSyncPage) await BrowserWebHost.InvokeScriptAsync("if(window.__hapticSignalReady) window.__hapticSignalReady(); else window.__hapticReady=true;");
                    }
                    break;
                case "audioSyncState":
                    if (root.TryGetProperty("currentTime", out var time) && time.TryGetDouble(out var position))
                        service?.OnPlaybackStateUpdate(position, root.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True);
                    break;
                case "audioSyncSeek":
                    if (root.TryGetProperty("currentTime", out var seek) && seek.TryGetDouble(out var target)) service?.OnVideoSeek(target);
                    break;
                case "audioSyncEnded": service?.OnVideoEnded(); break;
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("Browser audio sync message failed: {Error}", ex.Message); }
    }
}
