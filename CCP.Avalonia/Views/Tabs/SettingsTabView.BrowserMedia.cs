using System;
using System.Text.Json;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class SettingsTabView
    {
        // WPF BrowserService.cs:332-457 and BrowserMediaService.cs:230-277, :301-375,
        // :491-543. This head has no interaction queue; the Home browser state feeds Takeover,
        // scheduled mandatory video and Bubble Count gates. The reporter is injected after navigation because WebHost
        // cannot inject at document creation, and can observe only the top document.
        internal bool BrowserMediaPlaying { get; private set; }
        internal Func<DateTime> BrowserMediaClock = () => DateTime.UtcNow;
        private DateTime _browserMediaBeat;
        private DispatcherTimer? _browserMediaWatchdog;

        private void StartBrowserMediaReporter() => _ = BrowserWebHost.InvokeScriptAsync(BrowserMediaReporter);

        internal void OnBrowserMediaMessage(string message)
        {
            try
            {
                // WebHost adapters can return the page string raw or JSON-quoted.
                if (message.TrimStart().StartsWith('"'))
                    message = JsonSerializer.Deserialize<string>(message) ?? "";
                using var json = JsonDocument.Parse(message);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("type", out var type) || type.GetString() != "ccpMedia"
                    || !root.TryGetProperty("state", out var state)) return;
                switch (state.GetString())
                {
                    case "playing":
                        BrowserMediaPlaying = true;
                        _browserMediaBeat = BrowserMediaClock();
                        _browserMediaWatchdog ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                        _browserMediaWatchdog.Tick -= BrowserMediaWatchdogTick;
                        _browserMediaWatchdog.Tick += BrowserMediaWatchdogTick;
                        _browserMediaWatchdog.Start();
                        break;
                    case "progress":
                        if (BrowserMediaPlaying) _browserMediaBeat = BrowserMediaClock();
                        break;
                    case "stopped": StopBrowserMediaTracking(); break;
                }
            }
            catch (JsonException) { }
            catch (InvalidOperationException) { }
        }

        private void BrowserMediaWatchdogTick(object? sender, EventArgs e) => CheckBrowserMediaHeartbeat();

        internal void CheckBrowserMediaHeartbeat()
        {
            if (BrowserMediaPlaying && (BrowserMediaClock() - _browserMediaBeat).TotalSeconds >= 20)
                StopBrowserMediaTracking();
        }

        private void StopBrowserMediaTracking()
        {
            BrowserMediaPlaying = false;
            if (_browserMediaWatchdog == null) return;
            _browserMediaWatchdog.Stop();
            _browserMediaWatchdog.Tick -= BrowserMediaWatchdogTick;
            _browserMediaWatchdog = null;
        }

        // Adapted from WPF BrowserService.cs:340-457. The carrier uses the native WebHost
        // message bridge. WPF mutes the engine (BrowserService.cs:115), while WebHost mutes
        // elements. Treat our own mute as playback, keeping the 200 px preview filter.
        internal const string BrowserMediaReporter = """
            (function() {
                if (window.__ccpMediaBound) return;
                window.__ccpMediaBound = true;
                function post(o) {
                    try {
                        var s = JSON.stringify(o);
                        if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(s);
                        else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(s);
                    } catch (_) {}
                }
                function real(m) {
                    return m && (!m.muted || window.__ccpMuted) && (m.tagName === 'AUDIO' || (m.offsetWidth || 0) >= 200);
                }
                var active = null, beat = null, pauseTimer = null;
                function stopBeat() { if (beat) { clearInterval(beat); beat = null; } }
                function report(reason) {
                    active = null; stopBeat();
                    post({ type: 'ccpMedia', state: 'stopped', reason: reason });
                }
                function cancelStop() { if (pauseTimer) { clearTimeout(pauseTimer); pauseTimer = null; } }
                function playing(e) {
                    var m = e.target;
                    if (!real(m)) return;
                    cancelStop(); active = m;
                    post({ type: 'ccpMedia', state: 'playing' });
                    if (!beat) beat = setInterval(function() {
                        if (active && !active.paused && !active.ended)
                            post({ type: 'ccpMedia', state: 'progress' });
                    }, 1000);
                }
                function ended(e) {
                    if (active && e.target !== active) return;
                    cancelStop(); report('ended');
                }
                function maybeStop(e) {
                    if (!active || e.target !== active) return;
                    cancelStop();
                    pauseTimer = setTimeout(function() {
                        pauseTimer = null;
                        if (active && active.paused && !active.ended) report('paused');
                    }, 2000);
                }
                function bind(m) {
                    if (!m || m.__ccpMediaHooked) return;
                    m.__ccpMediaHooked = true;
                    m.addEventListener('playing', playing);
                    m.addEventListener('ended', ended);
                    m.addEventListener('pause', maybeStop);
                    if (!m.paused && !m.ended && m.readyState >= 3) playing({ target: m });
                }
                function scan() {
                    try { document.querySelectorAll('video,audio').forEach(bind); } catch (_) {}
                }
                function init() {
                    scan();
                    try { new MutationObserver(scan).observe(document.documentElement, { childList: true, subtree: true }); } catch (_) {}
                    setInterval(scan, 3000);
                }
                if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
                else init();
                window.addEventListener('pagehide', function() { report('navigated'); });
            })();
            """;
    }
}
