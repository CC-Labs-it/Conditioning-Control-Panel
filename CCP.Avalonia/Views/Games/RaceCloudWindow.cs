using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

// WPF Services/Race/RaceCloudWindow.cs:44-330. Standalone until the race cloud bridge is wired.
internal sealed class RaceCloudWindow : IDisposable
{
    private readonly Func<Uri, bool> _isSite;
    private readonly Uri _startUrl;
    private readonly WebHost _web;
    private readonly Window _window;
    private bool _disposed, _startWanted;
    internal Func<string, bool> OpenExternal = ExternalOpener.Open;
    internal WebHost Web => _web;
    internal Window Window => _window;
    public event Action<JObject>? Message;
    public event Action? Hidden;
    public bool IsOpen => _window.IsVisible;

    public RaceCloudWindow(Uri startUrl, Func<Uri, bool> isSite)
    {
        _startUrl = startUrl;
        _isSite = isSite;
        if (!_isSite(startUrl)) throw new ArgumentException("Start URL must be on the site", nameof(startUrl));
        var folder = Path.Combine(CorePaths.UserData, "browser_data_bambicloud");
        Directory.CreateDirectory(folder);
        _web = new WebHost { PersistentUserDataFolder = folder, InjectCarrier = true,
            WindowsBrowserArguments = "--autoplay-policy=no-user-gesture-required",
            WindowsDocumentStartScript = WatcherScript };
        _web.AllowNavigation = AllowNavigation;
        _web.PopupRequested = Popup;
        _web.WebMessage += Receive;
        _web.NavigationCompleted += NavigationCompleted;
        _web.BrowserProcessFailed += Failed;
        var s = CoreSettings.Current;
        _window = new Window { Title = "BambiCloud", Background = Brushes.Black, Content = _web,
            Width = s.RaceCloudWindowWidth > 200 ? s.RaceCloudWindowWidth : 1100,
            Height = s.RaceCloudWindowHeight > 200 ? s.RaceCloudWindowHeight : 820,
            WindowStartupLocation = s.RaceCloudWindowLeft != 0 || s.RaceCloudWindowTop != 0
                ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen };
        if (_window.WindowStartupLocation == WindowStartupLocation.Manual)
        {
            var x = (int)s.RaceCloudWindowLeft; var y = (int)s.RaceCloudWindowTop;
            var screen = _window.Screens.ScreenFromPoint(new PixelPoint(x, y)) ?? _window.Screens.Primary;
            if (screen != null)
            {
                var a = screen.WorkingArea;
                x = Math.Clamp(x, a.X, Math.Max(a.X, a.Right - 200));
                y = Math.Clamp(y, a.Y, Math.Max(a.Y, a.Bottom - 120));
            }
            _window.Position = new PixelPoint(x, y);
        }
        _window.Closing += (_, e) => {
            if (_disposed) return;
            e.Cancel = true;
            if (!_window.IsVisible) return;
            SaveBounds(); _window.Hide(); Hidden?.Invoke();
        };
        _web.Source = _startUrl;
    }

    private bool AllowNavigation(Uri uri)
    {
        if (_isSite(uri)) return true;
        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) OpenExternal(uri.AbsoluteUri);
        return false;
    }
    private void Popup(Uri? uri)
    {
        if (uri == null) return;
        if (AllowNavigation(uri)) _web.Navigate(uri);
    }
    public void ShowOrFocus(string? url = null) => Open(url, false);
    public void ShowInBackground(string? url = null) => Open(url, true);
    private void Open(string? url, bool background)
    {
        if (_disposed) return;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && _isSite(uri))
        {
            _startWanted = false;
            _web.Navigate(uri);
        }
        if (!_window.IsVisible) { _window.ShowActivated = !background; _window.Show(); }
        if (!background) { _window.Activate(); _web.FocusPage(); }
    }
    public void RequestStart()
    {
        if (_disposed) return;
        _startWanted = true;
        if (!_window.IsVisible) ShowInBackground();
        PostToPage(new { type = "cloud-start" });
    }
    public void PostToPage(object msg)
    {
        if (!_disposed && _web.CurrentUrl is { } url && _isSite(url) && _web.ActiveNavigation == null)
            _ = _web.PostJsonAsync(JsonConvert.SerializeObject(msg));
    }
    private void Receive(string json)
    {
        if (_disposed || _web.CurrentUrl is not { } url || !_isSite(url) || _web.ActiveNavigation != null) return;
        try
        {
            var obj = JObject.Parse(json);
            var type = (string?)obj["type"];
            if (type == null || !type.StartsWith("cloud-", StringComparison.Ordinal)) return;
            if (type == "cloud-play") _startWanted = false;
            Message?.Invoke(obj);
        }
        catch (JsonException) { }
    }
    private async void NavigationCompleted(Uri url)
    {
        if (_disposed || !_isSite(url)) return;
        // WebHost runs the carrier on completion; install it again in sequence before the watcher.
        await _web.InvokeScriptAsync(WebHost.CarrierShim);
        var result = await _web.InvokeScriptAsync(WatcherScript + "\n; 'ccp-watcher-installed';");
        if (_disposed || _web.CurrentUrl != url) return;
        if (result == null) { Failed(); return; }
        if (_startWanted) PostToPage(new { type = "cloud-start" });
    }
    private void Failed() => Message?.Invoke(new JObject { ["type"] = "cloud-failed" });
    private void SaveBounds()
    {
        if (_window.WindowState != WindowState.Normal || _window.Width < 200 || _window.Height < 200) return;
        var s = CoreSettings.Current;
        s.RaceCloudWindowLeft = _window.Position.X; s.RaceCloudWindowTop = _window.Position.Y;
        s.RaceCloudWindowWidth = _window.Width; s.RaceCloudWindowHeight = _window.Height;
        CoreSettings.Save();
    }
    public void Dispose()
    {
        if (_disposed) return;
        SaveBounds(); _disposed = true; _window.Close();
        Message = null; Hidden = null;
    }

    internal const string WatcherScript = @"(function () {
  try {
    if (window.__ccpRaceCloud) return;
    var CLOCK_MS = 250, ECHO_MS = 800;
    var current = null, lastSrc = '', lastPlaying = null, quietUntil = 0;
    window.__ccpRaceCloud = { version: 1, tracked: function () { return current; } };

    function post(msg) {
      try {
        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage)
          window.chrome.webview.postMessage(msg);
      } catch (e) {}
    }
    function isMedia(el) { return !!el && (el.tagName === 'AUDIO' || el.tagName === 'VIDEO'); }
    function num(v) { return (typeof v === 'number' && isFinite(v) && v >= 0) ? v : 0; }
    function srcOf(el) { try { return (el && (el.currentSrc || el.src)) || ''; } catch (e) { return ''; } }
    function durOf(el) { try { return num(el.duration); } catch (e) { return 0; } }
    function isPlaying(el) { try { return !!el && !el.paused && !el.ended; } catch (e) { return false; } }
    /** Their media session title if their player set one, else the tab title, else the file name. */
    function titleOf(el) {
      try {
        var ms = navigator.mediaSession;
        if (ms && ms.metadata && ms.metadata.title) return String(ms.metadata.title).slice(0, 160);
      } catch (e) {}
      try { if (document.title) return String(document.title).slice(0, 160); } catch (e) {}
      try {
        var parts = srcOf(el).split('?')[0].split('/');
        return decodeURIComponent(parts[parts.length - 1] || '').slice(0, 160);
      } catch (e) {}
      return '';
    }
    function take(el) { if (isMedia(el)) current = el; }
    /** The page's element, preferring one that is actually running. */
    function scan() {
      try {
        if (current && document.contains && !document.contains(current)) current = null;
        var nodes = document.querySelectorAll('audio,video');
        var first = null, playing = null;
        for (var i = 0; i < nodes.length; i++) {
          if (!first) first = nodes[i];
          if (!playing && isPlaying(nodes[i])) playing = nodes[i];
        }
        var pick = playing || first;
        if (pick) current = pick;
      } catch (e) {}
      return current;
    }
    /** A new source is playing: one frame, once per source. */
    function announce(el) {
      var src = srcOf(el);
      if (!src || src === lastSrc) return;
      lastSrc = src;
      post({ type: 'cloud-track', src: src, title: titleOf(el), durationSec: durOf(el) });
    }
    /** State changes only, never an echo of a pause we were told to make. */
    function state(playing) {
      if (lastPlaying === playing) return;
      lastPlaying = playing;
      if (Date.now() < quietUntil) return;
      post({ type: playing ? 'cloud-play' : 'cloud-pause' });
    }
    function onMedia(e) {
      try {
        var el = e.target;
        if (!isMedia(el)) return;
        take(el);
        if (e.type === 'play' || e.type === 'playing') { announce(el); state(true); }
        else if (e.type === 'pause') { state(false); }
        else if (e.type === 'ended') { lastSrc = ''; lastPlaying = false; post({ type: 'cloud-ended' }); }
        else if (e.type === 'emptied') { lastSrc = ''; }
      } catch (err) {}
    }
    var EVENTS = ['play', 'playing', 'pause', 'ended', 'emptied'];
    for (var i = 0; i < EVENTS.length; i++) document.addEventListener(EVENTS[i], onMedia, true);

    function observe() {
      try {
        if (typeof MutationObserver !== 'function') return;
        var root = document.documentElement || document;
        if (!root) return;
        new MutationObserver(function (recs) {
          for (var a = 0; a < recs.length; a++) {
            var added = recs[a].addedNodes;
            if (!added) continue;
            for (var b = 0; b < added.length; b++) {
              var n = added[b];
              if (isMedia(n)) { take(n); return; }
              if (n && n.querySelector) { var f = n.querySelector('audio,video'); if (f) { take(f); return; } }
            }
          }
        }).observe(root, { childList: true, subtree: true });
      } catch (e) {}
    }
    observe();
    try { document.addEventListener('DOMContentLoaded', observe, false); } catch (e) {}

    setInterval(function () {
      try {
        var el = (current && document.contains && document.contains(current)) ? current : scan();
        if (!el) return;
        post({ type: 'cloud-clock', t: num(el.currentTime), playing: isPlaying(el), durationSec: durOf(el) });
      } catch (e) {}
    }, CLOCK_MS);

    // cloud-start: the game's play button. Press the play action in the track page's header (the
    // round play button beside the title), or play their element on a page without one, and keep
    // trying for eight seconds while their page is still putting itself together.
    var startTimer = 0;
    function start() {
      try {
        if (startTimer) clearInterval(startTimer);
        var tries = 0, pressed = 0;
        var attempt = function () {
          try {
            tries++;
            var el = scan();
            if (el && isPlaying(el)) { clearInterval(startTimer); startTimer = 0; return; }
            // The track page's own play action first: their player may still hold the LAST track,
            // and resuming that would start the wrong song. Only pressed while nothing plays (it
            // toggles), as soon as it exists and then every 3 s until the audio runs.
            var btn = document.querySelector('.section-header .action');
            if (btn && btn.click) { if (!pressed || tries - pressed >= 6) { pressed = tries; btn.click(); } }
            else if (el && srcOf(el)) { var p = el.play(); if (p && p['catch']) p['catch'](function () {}); }
            if (tries >= 16) { clearInterval(startTimer); startTimer = 0; }
          } catch (e) {}
        };
        startTimer = setInterval(attempt, 500);
        attempt();
      } catch (e) {}
    }

    // The instructions we take: the race's Brake pauses their player, letting go resumes it,
    // and cloud-start (above) starts the landed track.
    try {
      if (window.chrome && window.chrome.webview && window.chrome.webview.addEventListener) {
        window.chrome.webview.addEventListener('message', function (ev) {
          try {
            var m = ev && ev.data;
            if (m && m.type === 'cloud-start') { start(); return; }
            if (!m || m.type !== 'cloud-set-paused') return;
            var el = current || scan();
            if (!el) return;
            quietUntil = Date.now() + ECHO_MS;
            if (m.on) el.pause();
            else { var p = el.play(); if (p && p['catch']) p['catch'](function () {}); }
          } catch (e) {}
        });
      }
    } catch (e) {}
  } catch (e) {}
})();";
}
