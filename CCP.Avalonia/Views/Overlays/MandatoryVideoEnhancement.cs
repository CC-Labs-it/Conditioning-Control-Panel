using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF VideoEnhancementBridge.cs:25 and VideoServiceTimeSource.cs:20.
internal sealed partial class MandatoryVideoOverlay
{
    private readonly EnhancementHostService _enhancementHost = new();
    internal bool EnhancementRunning => _enhancementHost.IsRunning;

    private void BeginEnhancement(string path)
    {
        EndEnhancement();
        ResetEnhancementStall(Now);
        // App startup seeds the dispatcher (App.axaml.cs); never bind a dry-run logger to playback.
        if (!CoreSettings.Current.VideoEnhanceIfPossible || _playback == null || EnhancementHostService.DispatcherFactory == null) return;
        try
        {
            var resolved = EnhancementResolver.ResolveForLocalMedia(path);
            if (!resolved.Found) return;
            var loaded = resolved.FilePath != null ? _enhancementHost.LoadFromFile(resolved.FilePath)
                : _enhancementHost.LoadFromMemory(resolved.Enhancement!, "video:" + System.IO.Path.GetFileName(path));
            if (!loaded) return;
            var source = new MandatoryVideoTimeSource(this, _playback);
            // The browser card's adapter (SettingsTabView.BrowserEnhance.cs): a subscription, never a camera
            // start; the host disposes it on unbind.
            var webcam = new Platform.Deeper.BrowserEnhancementWebcam();
            if (!_enhancementHost.Bind(source, source.Attach, source.Dispose, webcam: webcam))
            {
                webcam.Dispose();
                source.Dispose();
                _enhancementHost.Unload();
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Mandatory video enhancement bind failed"); EndEnhancement(); }
    }

    private void EndEnhancement() => _enhancementHost.Unload();

    // WPF VideoService.cs:6870-6931 (#536, #874): while bound, an enhancement can loop or hold the clip
    // past its length, so once the length + 5 s safety deadline passes it becomes a progress watch that
    // ends the clip only after 90 s without a new playhead. Only a changed, known position counts as
    // progress, so a clock that never reports still reaches the end. The unknown-length fallback and the
    // max-length cap stay guillotines. Unbinding stops IsRunning, which restores the plain deadline.
    internal const double EnhancementStallGraceSeconds = 90;
    private long _stallMs = -1;
    private DateTime _stallSince;
    private bool EnhancementOwnsLength(double lengthSeconds) => EnhancementRunning && lengthSeconds > 0;
    private void ResetEnhancementStall(DateTime now) => (_stallMs, _stallSince) = (-1, now);
    private bool EnhancementStalled(DateTime now)
    {
        var ms = _playback?.Time ?? -1;
        if (ms >= 0 && ms != _stallMs) { (_stallMs, _stallSince) = (ms, now); return false; }
        return (now - _stallSince).TotalSeconds >= EnhancementStallGraceSeconds;
    }

    // WPF VideoServiceTimeSource.cs:149: the primary picture's on-screen box in DIPs. The Uniform
    // Image arranges to the contain-fit box (FitContain :181); before a frame, the full area (:171).
    internal PlaybackRect VideoScreenRect() => _surfaces.Count == 0 ? PlaybackRect.Empty : ScreenRect(_surfaces[0].Video);
    internal static PlaybackRect ScreenRect(Control video)
    {
        var box = video.Bounds.Width > 0 && video.Bounds.Height > 0 ? video : video.Parent as Control;
        if (box == null || box.Bounds.Width <= 0 || box.Bounds.Height <= 0 || TopLevel.GetTopLevel(box) is not { } top) return PlaybackRect.Empty;
        var p = box.PointToScreen(default);
        return new PlaybackRect(p.X / top.RenderScaling, p.Y / top.RenderScaling, box.Bounds.Width, box.Bounds.Height);
    }

    private sealed class MandatoryVideoTimeSource : IPlaybackTimeSource, IDisposable
    {
        private readonly MandatoryVideoOverlay _owner;
        private readonly IMandatoryPlayback _player;
        private readonly PlayheadExtrapolator _clock = new();
        private DispatcherTimer? _timer;
        private bool _attached;
        public event Action<double>? PlaybackTimeChanged;

        internal MandatoryVideoTimeSource(MandatoryVideoOverlay owner, IMandatoryPlayback player)
        { _owner = owner; _player = player; }

        internal void Attach()
        {
            if (_attached) return;
            _attached = true;
            _owner.PlaybackTimeMsChanged += OnReport;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PlayheadExtrapolator.TickMs) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnReport(long ms)
        {
            var at = Environment.TickCount64;
            void Report() { if (_attached) Emit(_clock.OnReport(ms, at)); }
            if (Dispatcher.UIThread.CheckAccess()) Report();
            else Dispatcher.UIThread.Post(Report);
        }
        private void OnTick(object? sender, EventArgs e)
        {
            if (_attached) Emit(_clock.OnTimer(Environment.TickCount64, _player.IsPlaying, _player.Length));
        }
        private void Emit(long ms)
        {
            if (ms < 0) return;
            try { PlaybackTimeChanged?.Invoke(ms / 1000.0); }
            catch (Exception ex) { Log.Debug("Mandatory video enhancement clock: {Error}", ex.Message); }
        }
        public double GetCurrentTimeSeconds() => Math.Max(0, _player.Time) / 1000.0;
        public double GetDurationSeconds() => Math.Max(0, _player.Length) / 1000.0;
        public bool IsPlaying => _player.IsPlaying;
        public void Seek(double seconds) { if (_attached && _player.IsSeekable) _player.Time = (long)Math.Max(0, seconds * 1000); }
        public void Pause() { if (_attached) { if (_owner._player != null) _owner.PausePrimary(); else _player.SetPause(true); } }
        public void Play() { if (_attached) { if (_owner._player != null) _owner.PlayPrimary(); else _player.SetPause(false); } }
        public PlaybackRect GetVideoRect() => _attached ? _owner.VideoScreenRect() : PlaybackRect.Empty;
        public void Dispose()
        {
            if (!_attached) return;
            _attached = false;
            _owner.PlaybackTimeMsChanged -= OnReport;
            if (_timer != null) { _timer.Stop(); _timer.Tick -= OnTick; _timer = null; }
        }
    }
}
