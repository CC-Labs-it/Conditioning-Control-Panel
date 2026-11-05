// WPF EnhancementPlayerWindow.xaml.cs:1530-1744, 2020-2558, 1333, 1852, 2167-2182, 2659.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel.Avalonia.Views.Deeper;
public partial class EnhancementPlayerWindow {
    internal const string BrowserFullscreenOnMessage = "ccp_fullscreen_on", BrowserFullscreenOffMessage = "ccp_fullscreen_off", BrowserExitFullscreenMessage = "ccp_exit_fullscreen";
    // WPF player:1530-1702, 2782-2999. Capture relays beat page shortcuts; editable inputs keep their keys.
    internal const string BrowserFullscreenScript = @"(function() {
    if(window.__ccpFsBound)return; window.__ccpFsBound=true;
    function post(m){var w=window.chrome&&window.chrome.webview;if(w&&w.postMessage)w.postMessage(m);else if(window.invokeCSharpAction)window.invokeCSharpAction(m);}
    function fs(){return document.fullscreenElement||document.webkitFullscreenElement;}
    function any(){return fs()||window._ccpForcedFs;}
    function enter(){var v=null,area=0;document.querySelectorAll('video').forEach(function(x){var r=x.getBoundingClientRect(),a=r.width*r.height;if(a>area){v=x;area=a;}});if(!v)return false;try{var p=v.requestFullscreen?v.requestFullscreen():v.webkitRequestFullscreen();if(p&&p.catch)p.catch(function(){});}catch(_){}return true;}
    function exit(n){if(!fs()||!n)return;try{var p=document.exitFullscreen?document.exitFullscreen():document.webkitExitFullscreen();if(p&&p.then)p.then(function(){exit(n-1);},function(){setTimeout(function(){exit(n-1);},30);});else setTimeout(function(){exit(n-1);},30);}catch(_){setTimeout(function(){exit(n-1);},30);}}
    function dbl(e){if(any()){e.preventDefault();e.stopImmediatePropagation();exit(5);post('ccp_exit_fullscreen');}else if(enter()){e.preventDefault();e.stopImmediatePropagation();}}
    document.addEventListener('dblclick',dbl,true);document.addEventListener('dblclick',dbl,false);window.addEventListener('dblclick',dbl,true);
    function bind(){document.querySelectorAll('video').forEach(function(v){if(v._ccpBound)return;v._ccpBound=true;v.addEventListener('dblclick',dbl,true);v.addEventListener('dblclick',dbl,false);});}
    bind();setInterval(function(){if(any())bind();},1000);
    var last=!!fs();function change(){var f=!!fs();if(f!==last){last=f;post(f?'ccp_fullscreen_on':'ccp_fullscreen_off');}if(!f&&window._ccpForcedFs)post('ccp_exit_fullscreen');}
    document.addEventListener('fullscreenchange',change);document.addEventListener('webkitfullscreenchange',change);
    function key(e){return e.code==='Space'||e.key===' '?'Space':({ArrowLeft:'Left',ArrowRight:'Right',ArrowUp:'Up',ArrowDown:'Down',Escape:'Escape',F11:'F11',m:'M',M:'M',f:'F',F:'F'})[e.key];}
    window.addEventListener('keydown',function(e){var k=key(e);if(e.target&&(e.target.isContentEditable||/^(INPUT|TEXTAREA)$/.test(e.target.tagName))){if((k==='Escape'||k==='F11')&&any())post('ccp_exit_fullscreen');return;}if(!k)return;
        if(k==='F'&&!e.repeat&&!any()&&enter()){e.preventDefault();return;}
        post('ccp_key:'+k+':'+(e.shiftKey?1:0)+':'+(e.repeat?1:0));if(k!=='Escape'&&document.querySelector('video')){e.preventDefault();e.stopImmediatePropagation();}},true);
    window.addEventListener('keyup',function(e){if(key(e)==='Escape')post('ccp_keyup:Escape');},true);
    window.addEventListener('mousemove',function(){if(window._ccpForcedFs)post('ccp_pointer');},true);
})();";
    private static readonly HashSet<EnhancementPlayerWindow> FullscreenPlayers = new();
    private Window? _videoFullscreenWindow;
    private DisplayMirror? _videoMirror;
    private bool _videoTransition, _videoClosing, _videoExitPending, _videoHostClosed;
    internal Func<IDisposable?>? VideoMoveOverride;
    internal Action<Window> CloseVideoHost = w => w.Close();
    internal Window? VideoFullscreenWindow => _videoFullscreenWindow;
    private void OnVideoNavigationCompleted(Uri uri) {
        if (_videoClosing) return;
        _ = _videoBrowser.InvokeScriptAsync(BrowserFullscreenScript);
        if (_videoFullscreenWindow != null) { _ = _videoBrowser.InvokeScriptAsync("window._ccpForcedFs = true;"); ArmCursor(); }
    }
    private void OnVideoWebMessage(string message) {
        if (_videoClosing) return;
        message = message.Trim().Trim('"');
        if (message.StartsWith("ccp_key:", StringComparison.Ordinal)) {
            var p = message.Split(':');
            if (p.Length == 4 && Enum.TryParse<Key>(p[1], out var key)) PlayerKey(key, p[2] == "1", p[3] == "1");
        }
        if (message == "ccp_keyup:Escape") EscapeUp();
        if (message == "ccp_pointer") ArmCursor();
        switch (message) {
            case BrowserFullscreenOnMessage: EnterVideoFullscreen(); break;
            case BrowserFullscreenOffMessage:
                if (!_videoTransition) ExitVideoFullscreen();
                break;
            case BrowserExitFullscreenMessage: ExitVideoFullscreen(); break;
        }
    }
    private void EnterVideoFullscreen() {
        if (_videoClosing || _videoTransition || _videoFullscreenWindow != null) return;
        _videoTransition = true;
        try {
            if (CoreSettings.Current.DualMonitorEnabled && ScreenList.Enumerate(this).Count > 1) {
                var mirror = ScreenMirrorHead.For(this);
                if (mirror?.EnableMirror() == true) _videoMirror = mirror;
            }
            var screen = Screens.ScreenFromWindow(this)?.Bounds ?? default;
            var host = new Window {
                WindowDecorations = WindowDecorations.None, CanResize = false, ShowInTaskbar = false,
                Topmost = true, Background = Brushes.Black, WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new global::Avalonia.PixelPoint(screen.X + 100, screen.Y + 100), Width = 400, Height = 300,
            };
            _videoFullscreenWindow = host; _videoHostClosed = false;
            FullscreenPlayers.Add(this);
            host.Closing += OnVideoHostClosing; host.Closed += (_, _) => { _videoHostClosed = true; if (!_videoTransition) ExitVideoFullscreen(false); };
            host.Deactivated += OnVideoHostDeactivated; host.Activated += OnVideoHostActivated;
            host.AddHandler(KeyUpEvent, OnPlayerKeyUp, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            host.PointerMoved += OnVideoPointerMoved; host.AddHandler(KeyDownEvent, OnVideoHostKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            using var moving = VideoMoveOverride?.Invoke() ?? _videoBrowser.BeginReparenting();
            ((Grid)_videoPane.Child!).Children.Remove(_videoBrowser); host.Content = _videoBrowser;
            host.Show(); host.WindowState = WindowState.FullScreen;
            _ = _videoBrowser.InvokeScriptAsync("window._ccpForcedFs = true;");
            ArmCursor(); ReassertEffects();
        } catch (Exception ex) { Serilog.Log.Warning(ex, "Deeper fullscreen entry failed"); _videoExitPending = true; }
        finally {
            _videoTransition = false;
            if (_videoExitPending) { _videoExitPending = false; ExitVideoFullscreen(); }
        }
    }
    // WPF player:2143-2157, 2782-2999: the host, page and player share transport and the 3 s escape hatch.
    internal Func<DateTimeOffset> InputNow = () => DateTimeOffset.UtcNow;
    private DateTimeOffset? _escapeStart, _cursorDue;
    private DateTimeOffset _lastEscape;
    private bool _escapeConsumed;
    private double? _volumeBeforeMute;
    private readonly HashSet<Key> _pressed = new();
    private bool StrictInput => CoreSettings.Current.StrictLockEnabled || LockdownService.Current?.IsActive == true;
    private void OnVideoHostKeyDown(object? sender, KeyEventArgs e) {
        if (e.Source is TextBox || (sender as Window)?.FocusManager?.GetFocusedElement() is TextBox) return;
        e.Handled = PlayerKey(e.Key, e.KeyModifiers.HasFlag(KeyModifiers.Shift), !_pressed.Add(e.Key));
    }
    private void OnPlayerKeyUp(object? sender, KeyEventArgs e) { if ((sender as Window)?.FocusManager?.GetFocusedElement() is TextBox) return; _pressed.Remove(e.Key); if (e.Key == Key.Escape) { EscapeUp(); e.Handled = true; } }
    private void EscapeUp() {
        CancelEscHold(); _pressed.Remove(Key.Escape);
        if (!_escapeConsumed && !StrictInput && !_videoNavigated && !_isPlaying) Close();
        _escapeConsumed = false;
    }
    internal bool PlayerKey(Key key, bool shift = false, bool repeat = false) {
        switch (key) {
            case Key.Escape:
                _lastEscape = InputNow();
                if (!repeat && _changePopup.IsOpen) { _changePopup.IsOpen = false; _escapeConsumed = true; return true; }
                if (!repeat) { _escapeConsumed = _videoFullscreenWindow != null; ExitVideoFullscreen();
                    if (!StrictInput && (_videoNavigated || _isPlaying)) { _escapeStart = InputNow(); ShowHoldEscHint(0); } }
                return true;
            case Key.F11: ExitVideoFullscreen(); return true;
            case Key.F: if (!repeat) { if (_videoFullscreenWindow != null) ExitVideoFullscreen(); else if (_isVideoMode && _videoNavigated) EnterVideoFullscreen(); } return true;
            case Key.Space: if (!repeat) BtnPlayPause_Click(); return true;
            case Key.Left: case Key.Right:
                if (!_isVideoMode) { _currentSec = Math.Clamp(_currentSec + (key == Key.Left ? -1 : 1) * (shift ? 30 : 5), 0, Math.Max(0, _durationSec)); UpdatePlayhead(Fraction()); return true; }
                _ = _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("l.currentTime=Math.max(0,Math.min(isFinite(l.duration)&&l.duration>0?l.duration:Infinity,l.currentTime+(" + ((key == Key.Left ? -1 : 1) * (shift ? 30 : 5)) + "));")); return true;
            case Key.Up: case Key.Down:
                if (!_isVideoMode) { _volumeBeforeMute = null; _sliderVolume.Value = Math.Clamp(_sliderVolume.Value + (key == Key.Up ? 5 : -5), 0, 100); return true; }
                _ = _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("delete l._ccpMuteVolume;l.volume=Math.max(0,Math.min(1,l.volume+" + (key == Key.Up ? "0.05" : "-0.05") + "));")); return true;
            case Key.M:
                if (!_isVideoMode) { if (!repeat) { var old = _sliderVolume.Value; var restore = _volumeBeforeMute; _sliderVolume.Value = restore ?? 0; _volumeBeforeMute = restore == null ? old : null; } return true; }
                if (!repeat) _ = _videoBrowser.InvokeScriptAsync(DeeperPreview.Invoke("if(l._ccpMuteVolume!=null){l.volume=l._ccpMuteVolume;delete l._ccpMuteVolume;}else{l._ccpMuteVolume=l.volume;l.volume=0;}")); return true;
            default: return false;
        }
    }
    private void ArmCursor() { _cursorDue = InputNow().AddSeconds(2); if (_videoFullscreenWindow != null) _videoFullscreenWindow.Cursor = null; _ = _videoBrowser.InvokeScriptAsync("document.documentElement.style.cursor='';"); }
    private void OnVideoPointerMoved(object? sender, PointerEventArgs e) => ArmCursor();
    private void CancelEscHold() { _escapeStart = null; if (this.FindControl<StackPanel>("HoldEscHint") is { } hint) hint.IsVisible = false; }
    private void ShowHoldEscHint(double fraction) {
        if (this.FindControl<StackPanel>("HoldEscHint") is { } hint && this.FindControl<ProgressBar>("HoldEscProgress") is { } progress) { progress.Value = Math.Clamp(fraction, 0, 1); hint.IsVisible = true; }
    }
    internal void TickPlayerInput() {
        var now = InputNow();
        if (_escapeStart != null && (StrictInput || (now - _lastEscape).TotalSeconds > 1.5)) CancelEscHold();
        if (_escapeStart is { } held) ShowHoldEscHint((now - held).TotalSeconds / 3);
        if (_escapeStart is { } start && (now - start).TotalSeconds >= 3) {
            CancelEscHold(); _escapeConsumed = true; ExitVideoFullscreen(); BtnStop_Click();
            _txtStatus.Text = ConditioningControlPanel.Localization.Loc.Get("deeper_player_status_escape_stopped");
        }
        if (_videoFullscreenWindow != null) { ReassertEffects(); if (_cursorDue <= now) {
            _cursorDue = null; _videoFullscreenWindow.Cursor = new Cursor(StandardCursorType.None);
            _ = _videoBrowser.InvokeScriptAsync("document.documentElement.style.cursor='none';"); } }
    }
    // WPF player:2182,2256,2383; OverlayService:121-148. Reassert on entry, focus, ticks and exit, without activation.
    internal Func<IEnumerable<Window>> EffectWindows = () => (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows ?? Array.Empty<Window>();
    internal Action<Window> RaiseEffect = w => X11Overlay.Raise(w);
    private void ReassertEffects() {
        if (CoreEngine.Video?.IsPlaying == true) return; // WPF OverlayService:3050-3120: keep the mandatory video anchor above ambient effects.
        foreach (var w in EffectWindows().Where(w => w.IsVisible && w.GetType().Namespace == typeof(Overlays.TintOverlayWindow).Namespace).OrderBy(w => w is Overlays.TintOverlayWindow ? 0 : w is Overlays.SpiralOverlayWindow ? 1 : 2).ToArray())
            try { RaiseEffect(w); } catch (Exception ex) { Serilog.Log.Debug(ex, "Effect raise failed"); }
    }
    private void OnVideoHostDeactivated(object? sender, EventArgs e) { _pressed.Clear(); ((Window)sender!).Topmost = false; }
    private void OnVideoHostActivated(object? sender, EventArgs e) { ((Window)sender!).Topmost = true; ReassertEffects(); }
    private void OnVideoHostClosing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        ExitVideoFullscreen(closeHost: false);
    private void ExitVideoFullscreen(bool closeHost = true) {
        if (_videoTransition) { _videoExitPending = true; return; }
        var host = _videoFullscreenWindow;
        try { _videoMirror?.DisableMirror(); }
        finally { if (_videoMirror?.IsMirroring != true) _videoMirror = null; }
        if (host == null) { if (_videoMirror == null) FullscreenPlayers.Remove(this); return; }
        _videoTransition = true;
        try {
            _cursorDue = null; host.Cursor = null;
            _ = _videoBrowser.InvokeScriptAsync("window._ccpForcedFs = false; document.documentElement.style.cursor=''; try { if(document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
            try {
                using var moving = VideoMoveOverride?.Invoke() ?? _videoBrowser.BeginReparenting();
                host.Content = null;
                var pane = (Grid)_videoPane.Child!;
                if (!pane.Children.Contains(_videoBrowser)) pane.Children.Insert(0, _videoBrowser);
            } catch (Exception ex) { Serilog.Log.Warning(ex, "Video return failed"); }
            host.Topmost = false;
            try { if (closeHost) CloseVideoHost(host); } catch (Exception ex) { Serilog.Log.Warning(ex, "Video close failed"); }
            if (closeHost && host.IsVisible) host.Hide(); // WPF ForceExitVideoFullscreen:2459-2495.
            if (_videoHostClosed && _videoBrowser.Parent == _videoPane.Child) {
                host.Closing -= OnVideoHostClosing; host.Deactivated -= OnVideoHostDeactivated; host.Activated -= OnVideoHostActivated;
                host.RemoveHandler(KeyDownEvent, OnVideoHostKeyDown); host.RemoveHandler(KeyUpEvent, OnPlayerKeyUp); host.PointerMoved -= OnVideoPointerMoved;
                _videoFullscreenWindow = null;
            }
        } catch (Exception ex) { Serilog.Log.Warning(ex, "Video teardown failed"); try { host.Topmost = false; host.Hide(); } catch { } }
        finally { _videoTransition = false; _videoExitPending = false;
            if (_videoFullscreenWindow == null && _videoMirror == null) FullscreenPlayers.Remove(this); ReassertEffects(); }
    }
    internal static void ForceExitFullscreenAll() {
        foreach (var player in FullscreenPlayers.ToArray()) player.ExitVideoFullscreen();
    }
}
