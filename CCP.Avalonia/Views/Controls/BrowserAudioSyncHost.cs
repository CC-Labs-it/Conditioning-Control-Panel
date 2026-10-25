using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

/// <summary>WPF MainWindow.Browser's audio-sync bridge, owned by the Home browser card.</summary>
internal sealed class BrowserAudioSyncHost : IDisposable
{
    private static readonly HashSet<BrowserAudioSyncHost> Live = new();
    private readonly WebHost _web;
    private readonly Func<string, Task<string?>> _script;
    private BrowserAudioSyncService? _sync;
    private int _page;
    private bool _stopped;
    internal static readonly string Injection = LoadScript();
    private static string LoadScript()
    {
        using var stream = typeof(BrowserAudioSyncHost).Assembly.GetManifestResourceStream(
            "ConditioningControlPanel.Avalonia.Views.Controls.BrowserAudioSync.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    internal BrowserAudioSyncHost(WebHost web, Func<string, Task<string?>>? script = null)
    {
        _web = web;
        _script = script ?? web.InvokeScriptAsync;
        web.InjectCarrier = true;
        web.WebMessage += OnMessage;
        web.NavigationCompleted += OnNavigation;
        web.NavigationBeginning += Stop;
        web.PropertyChanged += SourceChanged;
        Live.Add(this);
        if (web.CurrentUrl is { } url) OnNavigation(url);
    }
    private void SourceChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WebHost.SourceProperty) Stop();
    }
    private void OnNavigation(Uri url) { _page++; _sync?.Dispose(); _sync = null; _stopped = false; _ = _script(WebHost.CarrierShim + Injection); }
    private void OnMessage(string body) => _ = HandleAsync(body);
    internal async Task HandleAsync(string body)
    {
        if (_stopped) return;
        var page = _page;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var type)) return;
            switch (type.GetString())
            {
                case "audioSyncVideoDetected":
                    page = ++_page;
                    _sync?.Dispose();
                    _sync = null;
                    if (CoreHaptics.Service is { } haptics)
                        _sync = new BrowserAudioSyncService(haptics, CoreSettings.Current.Haptics.AudioSync, BrowserAudioRangeReader.Read);
                    if (_sync != null && root.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
                    {
                        _sync.ProcessingProgress += (_, e) => Dispatcher.UIThread.Post(() =>
                        {
                            if (page == _page && !_stopped) _ = _script("window.__hapticUpdateProgress?.(" +
                                JsonSerializer.Serialize(e.PercentComplete) + "," + JsonSerializer.Serialize(e.Status) + ");");
                        });
                        _sync.ChunkLoadingRequired += (_, _) => Send("window.__hapticShowOverlay?.('Loading haptic data...');document.querySelector('video')?.pause();", page);
                        _sync.ChunkLoadingCompleted += (_, _) => Send("window.__hapticSignalReady?.();", page);
                        var sync = _sync;
                        await Task.Run(() => sync.OnVideoDetectedAsync(url.GetString()!, BrowserAudioSyncService.ParseReportedDuration(body)));
                    }
                    Send("window.__hapticSignalReady?.();", page);
                    break;
                case "audioSyncState":
                    if (Time(root) is double time) _sync?.OnPlaybackStateUpdate(time, root.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True);
                    break;
                case "audioSyncSeek":
                    if (Time(root) is double seek) _sync?.OnVideoSeek(seek);
                    break;
                case "audioSyncEnded": _sync?.Reset(); break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { Send("window.__hapticSignalReady?.();", page); }
    }
    private static double? Time(JsonElement root) => root.TryGetProperty("currentTime", out var t) && t.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 && value <= 86400 ? value : null;
    private void Send(string script, int page) => Dispatcher.UIThread.Post(() => { if (page == _page && !_stopped) _ = _script(script); });
    internal void Stop()
    {
        _page++;
        _stopped = true;
        _sync?.Dispose();
        _sync = null;
        _ = _script("window.__hapticStopSync?.();window.__hapticDisabled=true;window.__hapticReady=false;window.__hapticHideOverlay?.();document.querySelectorAll('video').forEach(v=>v.pause());");
    }
    internal static void StopAll() { foreach (var host in Live) host.Stop(); }
    public void Dispose()
    {
        Stop();
        Live.Remove(this);
        _web.WebMessage -= OnMessage;
        _web.NavigationCompleted -= OnNavigation;
        _web.NavigationBeginning -= Stop;
        _web.PropertyChanged -= SourceChanged;
    }
}
