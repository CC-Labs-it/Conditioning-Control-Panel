// WPF MainWindow.Browser.cs:145-159, 1176-1192, 1242-1439.
using System;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using ConditioningControlPanel.Services.Browser;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private WebHost? _audioSyncHost;
    private HapticService? _audioSyncHaptics;
    private AudioSyncSettings? _audioSyncSettings;
    private BrowserAudioSyncService? _audioSyncService;
    private readonly object _audioSyncGate = new();
    private int _audioSyncGeneration;
    private bool _audioSyncArmed;

    // The tests drive the same transport without a physical device or decoder.
    internal Func<bool>? AudioSyncConnectedOverride;
    internal Func<string, TimeSpan?, Task>? AudioSyncDetectOverride;
    internal Action<double, bool>? AudioSyncStateOverride;
    internal Action<double>? AudioSyncSeekOverride;
    internal Action? AudioSyncEndedOverride;
    private bool AudioSyncConnected => AudioSyncConnectedOverride?.Invoke() ?? _audioSyncHaptics?.IsConnected == true;

    private void InitializeBrowserAudioSync()
    {
        _audioSyncHost = BrowserView;
        if (_audioSyncHost == null) return;
        _audioSyncHaptics = CoreHaptics.Service;
        _audioSyncSettings = CoreSettings.Current.Haptics.AudioSync;
        _audioSyncService = Platform.AudioSyncHost.Service;
        _audioSyncHost.NavigationBeginning += AudioSyncNavigationBeginning;
        _audioSyncHost.NavigationFinished += AudioSyncNavigationFinished;
        _audioSyncHost.WebMessage += AudioSyncMessage;
        _audioSyncHost.BrowserProcessFailed += StopBrowserAudioSync;
        if (_audioSyncHaptics != null) _audioSyncHaptics.ConnectionChanged += AudioSyncConnectionChanged;
        _audioSyncSettings.PropertyChanged += AudioSyncSettingsChanged;
        if (_audioSyncService != null)
        {
            _audioSyncService.ProcessingProgress += AudioSyncProgress;
            _audioSyncService.ChunkLoadingRequired += AudioSyncChunkLoading;
            _audioSyncService.ChunkLoadingCompleted += AudioSyncChunkLoaded;
        }
    }

    private static bool IsAudioSyncPage(Uri? url) => url is { IsAbsoluteUri: true } &&
        (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) &&
        (url.Host.Equals("hypnotube.com", StringComparison.OrdinalIgnoreCase) ||
         url.Host.EndsWith(".hypnotube.com", StringComparison.OrdinalIgnoreCase));

    private bool AudioSyncEligible => _audioSyncSettings?.Enabled == true && AudioSyncConnected;
    private void AudioSyncNavigationBeginning() => StopBrowserAudioSync();
    private void AudioSyncNavigationFinished(Uri url, bool success)
    {
        if (success && IsAudioSyncPage(url) && AudioSyncEligible) _ = ArmBrowserAudioSyncAsync(false);
    }
    internal void AudioSyncConnectionChanged(object? sender, bool connected) => Dispatcher.UIThread.Post(() =>
    {
        if (connected && AudioSyncEligible) _ = ArmBrowserAudioSyncAsync(true);
        else if (!connected) StopBrowserAudioSync();
    });
    private void AudioSyncSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AudioSyncSettings.Enabled)) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (AudioSyncEligible) _ = ArmBrowserAudioSyncAsync(true);
            else StopBrowserAudioSync();
        });
    }

    internal async Task ArmBrowserAudioSyncAsync(bool rearm)
    {
        var host = _audioSyncHost;
        if (host == null || host.ActiveNavigation != null || !IsAudioSyncPage(host.CurrentUrl) || !AudioSyncEligible) return;
        int generation;
        lock (_audioSyncGate) { generation = ++_audioSyncGeneration; _audioSyncArmed = true; }
        // Avalonia's native adapter supplies invokeCSharpAction, which Core's script uses.
        // Guard at execution time as a navigation may have overtaken this call.
        await host.InvokeScriptAsync("if (location.hostname === 'hypnotube.com' || location.hostname.endsWith('.hypnotube.com')) {" + BrowserAudioSyncScript.Source + "}");
        if (generation != _audioSyncGeneration || !_audioSyncArmed)
        {
            if (!_audioSyncArmed) await host.InvokeScriptAsync("window.__hapticCancel?.();");
            return;
        }
        if (rearm) await host.InvokeScriptAsync("window.__hapticRearm?.();");
    }

    internal void StopBrowserAudioSync()
    {
        lock (_audioSyncGate)
        {
            ++_audioSyncGeneration;
            if (!_audioSyncArmed) return;
            _audioSyncArmed = false;
            _audioSyncService?.StopSync();
        }
        if (_audioSyncHost != null) _ = _audioSyncHost.InvokeScriptAsync("window.__hapticCancel?.();");
    }

    internal void AudioSyncMessage(string body)
    {
        var host = _audioSyncHost;
        if (!_audioSyncArmed || host == null || host.ActiveNavigation != null || !IsAudioSyncPage(host.CurrentUrl)) return;
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return;
            switch (type.GetString())
            {
                case "audioSyncVideoDetected":
                    if (!AudioSyncEligible) { StopBrowserAudioSync(); return; }
                    if (!root.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(url.GetString()))
                    { _ = host.InvokeScriptAsync("if (!window.__hapticCancelled) window.__hapticSignalReady?.();"); return; }
                    TimeSpan? duration = null;
                    if (root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetDouble(out var seconds) && double.IsFinite(seconds) && seconds > 0 && seconds <= 86400)
                        duration = TimeSpan.FromSeconds(seconds);
                    _ = ProcessAudioSyncVideoAsync(url.GetString()!, duration);
                    break;
                case "audioSyncState":
                    if (TryAudioSyncTime(root, out var time))
                    {
                        var paused = root.TryGetProperty("paused", out var p) && p.ValueKind == JsonValueKind.True;
                        if (AudioSyncStateOverride != null) AudioSyncStateOverride(time, paused);
                        else _audioSyncService?.OnPlaybackStateUpdate(time, paused);
                    }
                    break;
                case "audioSyncSeek":
                    if (TryAudioSyncTime(root, out var seek))
                    {
                        if (AudioSyncSeekOverride != null) AudioSyncSeekOverride(seek);
                        else QueueAudioSyncSeek(seek);
                    }
                    break;
                case "audioSyncEnded":
                    if (AudioSyncEndedOverride != null) AudioSyncEndedOverride();
                    else _audioSyncService?.OnVideoEnded();
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
    }

    private static bool TryAudioSyncTime(JsonElement root, out double time)
    {
        time = 0;
        return root.TryGetProperty("currentTime", out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out time) && double.IsFinite(time) && time >= 0 && time <= 86400;
    }

    private async Task ProcessAudioSyncVideoAsync(string url, TimeSpan? duration)
    {
        int generation;
        lock (_audioSyncGate) generation = ++_audioSyncGeneration;
        try
        {
            if (AudioSyncDetectOverride != null) await AudioSyncDetectOverride(url, duration);
            else if (_audioSyncService is { } service)
                await Task.Run(async () =>
                {
                    Task? pending = null;
                    lock (_audioSyncGate)
                        if (_audioSyncArmed && generation == _audioSyncGeneration)
                            pending = service.OnVideoDetectedAsync(url, duration);
                    if (pending != null) await pending.ConfigureAwait(false);
                });
        }
        catch (Exception ex) { Serilog.Log.Debug("Browser audio sync failed: {Error}", ex.Message); }
        if (generation == _audioSyncGeneration && _audioSyncArmed && _audioSyncHost is { } host)
            await host.InvokeScriptAsync("if (!window.__hapticCancelled) window.__hapticSignalReady?.();");
    }

    private void QueueAudioSyncSeek(double time)
    {
        var service = _audioSyncService;
        var generation = _audioSyncGeneration;
        if (service == null) return;
        _ = Task.Run(() =>
        {
            lock (_audioSyncGate)
                if (_audioSyncArmed && generation == _audioSyncGeneration) service.OnVideoSeek(time);
        });
    }

    private void PostAudioSyncScript(string script)
    {
        int generation;
        lock (_audioSyncGate) generation = _audioSyncGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (_audioSyncArmed && generation == _audioSyncGeneration && _audioSyncHost is { } host)
                _ = host.InvokeScriptAsync(script);
        });
    }
    internal void AudioSyncProgress(object? sender, ChunkProgressEventArgs e) =>
        PostAudioSyncScript($"window.__hapticUpdateProgress?.({e.PercentComplete}, {JsonSerializer.Serialize(e.Status)});");
    internal void AudioSyncChunkLoading(object? sender, int index) =>
        PostAudioSyncScript("window.__hapticShowOverlay?.();");
    internal void AudioSyncChunkLoaded(object? sender, EventArgs e) => PostAudioSyncScript("window.__hapticHideOverlay?.();");

    private void DisposeBrowserAudioSync()
    {
        StopBrowserAudioSync();
        if (_audioSyncHost != null)
        {
            _audioSyncHost.NavigationBeginning -= AudioSyncNavigationBeginning;
            _audioSyncHost.NavigationFinished -= AudioSyncNavigationFinished;
            _audioSyncHost.WebMessage -= AudioSyncMessage;
            _audioSyncHost.BrowserProcessFailed -= StopBrowserAudioSync;
        }
        if (_audioSyncHaptics != null) _audioSyncHaptics.ConnectionChanged -= AudioSyncConnectionChanged;
        if (_audioSyncSettings != null) _audioSyncSettings.PropertyChanged -= AudioSyncSettingsChanged;
        if (_audioSyncService != null)
        {
            _audioSyncService.ProcessingProgress -= AudioSyncProgress;
            _audioSyncService.ChunkLoadingRequired -= AudioSyncChunkLoading;
            _audioSyncService.ChunkLoadingCompleted -= AudioSyncChunkLoaded;
        }
        _audioSyncHost = null;
        _audioSyncHaptics = null;
        _audioSyncSettings = null;
        _audioSyncService = null;
    }
}
