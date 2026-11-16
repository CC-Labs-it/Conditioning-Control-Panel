using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.Services.Video.Browser;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF VideoService.cs:3207 and VideoService.Browser.cs:110-441: an eligible clip plays in the browser
// engine (BrowserVideoEngineEnabled, BrowserVideoGate); LibVLC is the fallback.
internal sealed partial class MandatoryVideoOverlay
{
    private BrowserPlayback? _browser;
    private bool _forceLibVlc;   // WPF forceLibVlc / _browserFallbackDone: one browser attempt per clip
    private readonly List<Window> _controlHosts = new();

    internal BrowserPlayback? Browser => _browser;
    internal IReadOnlyList<Window> ControlHosts => _controlHosts;

    /// <summary>The native web view paints over everything drawn in its window, so a clip that needs
    /// in-window attention targets, floating chrome or the gentle curtain stays on LibVLC (WPF draws
    /// those inside the page, VideoService.Browser.cs:150-171).</summary>
    private bool BrowserFits() => !_forceLibVlc && !Floating && !_attention && !Services.Safety.GentleVisuals.On;

    /// <summary>WPF StartBrowserVideoPlayback (VideoService.Browser.cs:110): false leaves nothing to undo.</summary>
    private bool TryStartBrowser(string path)
    {
        if (!BrowserFits() || _surfaces.Count == 0 || !BrowserVideoSurface.TryAcquire(this, path)) return false;
        var url = BrowserVideoSurface.Map(path);
        var views = new List<BrowserVideoSurface>();
        foreach (var _ in _surfaces)
        {
            var view = url == null ? null : BrowserVideoSurface.Create();
            if (view != null) { views.Add(view); continue; }
            foreach (var v in views) v.Dispose();
            BrowserVideoSurface.Release(this);
            if (url != null) BrowserVideoSurface.StoodDown = true;   // no engine: LibVLC from now on
            return false;
        }
        _playback = _browser = new BrowserPlayback(this, views);
        PlayerVolume = EffectivePlayerVolume;
        for (var i = 0; i < views.Count; i++)
        {
            var (x, view) = (_surfaces[i], views[i]);
            x.Video.IsVisible = false;
            x.Picture.Children.Add(view.View);
            view.Message += m => OnBrowserMessage(view, m);
            view.Failed += media => OnBrowserFailed(view, media);
            view.Start(url!, i == 0, PlayerVolume);
        }
        Log.Information("VideoService: {File} plays in the browser engine", System.IO.Path.GetFileName(path));
        return true;
    }

    /// <summary>The LibVLC path's clocks after Play (MandatoryVideoOverlay.cs:355-362). The funscript follows
    /// the page's time reports; the Core scheduler starts it after Show.</summary>
    private void StartBrowserClocks(string path)
    {
        BeginEnhancement(path);
        _sinceReady.Restart();
        _guard = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _guard.Tick += (_, _) => GuardTick();
        _guard.Start();
    }

    private bool BrowserCurrent(BrowserVideoSurface view) =>
        _browser != null && !_closing && _browser.Views.Count > 0 && ReferenceEquals(_browser.Views[0], view);

    private void OnBrowserMessage(BrowserVideoSurface view, JsonElement message)
    {
        if (!BrowserCurrent(view)) return;
        switch (message.GetProperty("type").GetString())
        {
            case "time":
                if (message.TryGetProperty("ms", out var t) && t.TryGetDouble(out var ms)) ReportPlaybackTime(_browser!.TimeMs = (long)ms);
                break;
            case "meta":
                if (message.TryGetProperty("durationMs", out var d) && d.TryGetDouble(out var len)) _browser!.LengthMs = (long)len;
                break;
            case "playing":
                if (_browser!.Started) break;
                _browser.Started = true;   // synchronous, as WPF OnBrowserPlaying (:520)
                FirstFrameMs = _sinceShow.ElapsedMilliseconds;
                FrameTs = Stopwatch.GetTimestamp();
                if (!_gracePaused) _sinceFrame.Start();
                OnPlayerPlaying(true);
                RevealVideoSurfaces();
                Services.Audio.RecentSounds.Shared.Note("video", _path, CoreEngine.IsRunning, false, DateTime.Now);
                break;
            case "ended":
                // WPF OnBrowserEnded (:571): the natural end funnel, off the page callback.
                Dispatcher.UIThread.Post(() =>
                {
                    if (!BrowserCurrent(view)) return;
                    if (Scheduler.IsPlaying && FirstFrameMs >= 0) App.ChasterNote("video");
                    OnNaturalVideoEnd();
                });
                break;
            case "key":
                // WPF OnBrowserKey (:741): a focused page eats keys, so it reports them to the window's handlers.
                if (message.TryGetProperty("key", out var k) && PageKey(k.GetString()) is { } key)
                {
                    var alt = message.TryGetProperty("alt", out var a) && a.ValueKind == JsonValueKind.True;
                    var window = _surfaces.Count > 0 ? _surfaces[0].Window : null;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!BrowserCurrent(view) || window == null) return;
                        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = alt ? KeyModifiers.Alt : KeyModifiers.None });
                    });
                }
                break;
        }
    }

    /// <summary>WPF TranslateDomKey (VideoService.Browser.cs:854): DOM key values to key names.</summary>
    internal static Key? PageKey(string? dom)
    {
        if (string.IsNullOrEmpty(dom)) return null;
        var name = dom.Length == 1 && char.IsAsciiDigit(dom[0]) ? "D" + dom
            : dom switch { " " => "Space", "Enter" => "Return", "ArrowUp" => "Up", "ArrowDown" => "Down", "ArrowLeft" => "Left", "ArrowRight" => "Right", _ => dom };
        return HotkeyNames.TryParse(name, out var key) ? key : null;
    }

    /// <summary>WPF OnBrowserFailed (:576) and FallbackToLibVlc (:361): a mirror drops alone; the primary
    /// replays the clip through LibVLC before playback starts (or under strict, unless the file is to
    /// blame), otherwise the clip ends through the natural funnel.</summary>
    private void OnBrowserFailed(BrowserVideoSurface view, bool mediaFailure)
    {
        var playback = _browser;
        if (playback == null || _closing || !playback.Views.Contains(view)) return;
        if (!ReferenceEquals(playback.Views[0], view))
        {
            Log.Warning("VideoService: a browser mirror failed - dropping it");
            playback.Drop(view);
            return;
        }
        if (mediaFailure) BrowserUnsafeVideoCache.Add(_path, "mandatory video media error");
        else BrowserVideoSurface.StoodDown = true;
        var action = VideoSurfaceHealth.DecideBrowserFailure(true, _forceLibVlc, playback.Started, _strict, mediaFailure);
        if (action == VideoSurfaceHealth.BrowserFailureAction.Ignore) return;
        _forceLibVlc = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(_browser, playback) || _closing) return;
            if (action == VideoSurfaceHealth.BrowserFailureAction.EndClip)
            {
                Log.Warning("VideoService: browser playback failed mid-clip - ending the video");
                OnNaturalVideoEnd();
                return;
            }
            Log.Warning("VideoService: browser playback failed - replaying {File} via LibVLC", System.IO.Path.GetFileName(_path));
            var muted = _userMuted;
            var watched = CloseAll();
            if (watched >= 1) CoreProgression.TrackVideoWatched(watched);
            _userMuted = muted;
            Play(_path, _strict);
        });
    }

    /// <summary>WPF VideoControlsOverlay is its own window (VideoService.Controls.cs:63); here only the
    /// browser path needs one, because the web view covers the in-window row.</summary>
    private void LiftControls(Surface x)
    {
        if (x.Buttons.Row.Parent is not Panel panel) return;
        panel.Children.Remove(x.Buttons.Row);
        var host = new Window
        {
            Title = "CCP Video", WindowDecorations = WindowDecorations.None, Topmost = true, ShowInTaskbar = false,
            ShowActivated = false, CanResize = false, SizeToContent = SizeToContent.WidthAndHeight,
            Background = Brushes.Transparent, TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            WindowStartupLocation = WindowStartupLocation.Manual, Content = x.Buttons.Row,
        };
        if (x.PanicTunnel != null) host.AddHandler(InputElement.KeyDownEvent, x.PanicTunnel, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        host.Opened += (_, _) => host.Position = new PixelPoint(
            x.Window.Position.X + (int)((x.Window.Bounds.Width - host.Bounds.Width) * x.Window.RenderScaling), x.Window.Position.Y);
        _controlHosts.Add(host);
        host.Show();
    }

    private void CloseControlHosts()
    {
        foreach (var host in _controlHosts) { host.Content = null; host.Close(); }
        _controlHosts.Clear();
    }

    /// <summary>The page player behind the decoder boundary, so grace pause, guards, the cut clock and
    /// teardown stay shared with LibVLC. Disposal releases the one browser session.</summary>
    internal sealed class BrowserPlayback(MandatoryVideoOverlay owner, List<BrowserVideoSurface> views) : IMandatoryPlayback
    {
        internal IReadOnlyList<BrowserVideoSurface> Views => views;
        internal long TimeMs, LengthMs;
        internal bool Started;
        private bool _paused;
        public long Time { get => TimeMs; set { foreach (var v in views) v.Seek(value); } }
        public long Length => LengthMs;
        public bool IsPlaying => Started && !_paused;
        public bool IsSeekable => LengthMs > 0;
        public bool HasVideo => false;   // no frame callbacks: the page's own 10 s start and stall checks own it
        public void SetPause(bool paused)
        {
            _paused = paused;
            foreach (var v in views)
            {
                if (paused) v.Pause(); else v.Resume();
                v.View.IsVisible = !paused;   // the grace card sits under the native view otherwise
            }
        }
        internal void SetVolume(int volume) { foreach (var v in views) v.SetVolume(volume); }
        internal void Drop(BrowserVideoSurface view)
        {
            views.Remove(view);
            (view.View.Parent as Panel)?.Children.Remove(view.View);
            view.Dispose();
        }
        public void Stop() { foreach (var v in views) v.Pause(); }
        public void Dispose()
        {
            foreach (var v in views.ToList()) Drop(v);
            BrowserVideoSurface.Release(owner);
        }
    }
}
