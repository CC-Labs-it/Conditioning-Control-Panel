using System;
using System.IO;
using System.Threading;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// What the playlist service needs from a sound: WPF EnhancementAudioPlayer's surface (Play / Pause /
    /// Resume / Stop / Seek, a 100 ms time tick, Ended at the natural end, StoppedUnexpectedly on a device
    /// error). Every member but <see cref="Mute"/> is for the UI thread; every event fires on it.
    /// </summary>
    internal interface IPlaylistAudio : IDisposable
    {
        /// <summary>The loaded file, or null once stopped.</summary>
        string? CurrentPath { get; }
        bool IsPlaying { get; }
        bool IsPaused { get; }
        long CurrentTimeMs { get; }
        /// <summary>0 while the length is unknown.</summary>
        long DurationMs { get; }
        /// <summary>0..100, linear gain. Applies to the live output and to every later Play.</summary>
        int Volume { get; set; }

        /// <summary>Loads and starts <paramref name="path"/>. False = it could not be opened.</summary>
        bool Play(string path);
        void Pause();
        void Resume();
        /// <summary>Stops and unloads; safe when idle.</summary>
        void Stop();
        void Seek(double seconds);
        /// <summary>Panic: silence now, from any thread. The next <see cref="Volume"/> set restores the level.</summary>
        void Mute();

        event Action<long>? PlaybackTimeMsChanged;
        event Action? Ended;
        event Action? StoppedUnexpectedly;
    }

    /// <summary>
    /// <see cref="IPlaylistAudio"/> on the process's shared LibVLC (WPF used NAudio's WaveOutEvent +
    /// MediaFoundationReader). One MediaPlayer per track, like DeeperLocalAudio: a libvlc event names the
    /// player it came from, so a late event from the previous track can never move the next one. libvlc
    /// events are never acted on inside libvlc's own thread (deadlock): they hop to the pool, then the UI.
    /// State (playing / paused) is this class's own, set at once, because libvlc's lags the call.
    /// </summary>
    internal sealed class VlcPlaylistAudio : IPlaylistAudio
    {
        private enum State { Stopped, Playing, Paused, Ended }

        private readonly Func<LibVLC?> _vlc;
        private MediaPlayer? _mp;
        private Media? _media;
        private string? _path;
        private State _state;
        private long _pendingMs;          // a seek made before libvlc could take it
        private int _volume = AudioPlaylistService.DefaultVolume;
        private DispatcherTimer? _tick;
        private bool _disposed;

        public VlcPlaylistAudio() : this(() => LibVlcAudio.Shared) { }
        internal VlcPlaylistAudio(Func<LibVLC?> vlc) { _vlc = vlc; }

        public string? CurrentPath => _path;
        public bool IsPlaying => _state == State.Playing;
        public bool IsPaused => _state == State.Paused;

        public long CurrentTimeMs
        {
            get
            {
                if (_mp == null) return 0;
                if (_state == State.Ended) return DurationMs;
                try { var t = _mp.Time; return t >= 0 && _pendingMs <= 0 ? t : _pendingMs; }
                catch { return 0; }
            }
        }

        public long DurationMs
        {
            get
            {
                try { var len = _mp?.Length ?? 0; return len > 0 ? len : 0; }
                catch { return 0; }
            }
        }

        public int Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 100);
                try { if (_mp != null) _mp.Volume = LibVlcAudio.VlcVolume(_volume / 100f); }
                catch (Exception ex) { Log.Debug("VlcPlaylistAudio volume: {E}", ex.Message); }
            }
        }

        public event Action<long>? PlaybackTimeMsChanged;
        public event Action? Ended;
        public event Action? StoppedUnexpectedly;

        public bool Play(string path)
        {
            if (_disposed) return false;
            var vlc = _vlc();
            if (vlc == null || string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Log.Debug("VlcPlaylistAudio.Play: no libvlc or file missing ({Path})", Path.GetFileName(path));
                return false;
            }
            Stop();
            try
            {
                var media = new Media(vlc, path, FromType.FromPath);
                media.AddOption(LibVlcAudio.NoVideo);
                var mp = new MediaPlayer(media) { Volume = LibVlcAudio.VlcVolume(_volume / 100f) };
                mp.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ => OnVlcPlaying(mp));
                mp.EndReached += (_, _) => ThreadPool.QueueUserWorkItem(_ => Dispatcher.UIThread.Post(() => OnVlcEnded(mp)));
                mp.EncounteredError += (_, _) => ThreadPool.QueueUserWorkItem(_ => Dispatcher.UIThread.Post(() => OnVlcError(mp)));
                _mp = mp;
                _media = media;
                if (!mp.Play()) { Stop(); return false; }
                _path = path;
                _state = State.Playing;
                _pendingMs = 0;
                StartTick();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "VlcPlaylistAudio.Play failed");
                Stop();
                return false;
            }
        }

        public void Pause()
        {
            if (_state != State.Playing || _mp == null) return;
            _state = State.Paused;
            _tick?.Stop();
            try { _mp.SetPause(true); } catch (Exception ex) { Log.Debug("VlcPlaylistAudio.Pause: {E}", ex.Message); }
        }

        public void Resume()
        {
            if (_state != State.Paused || _mp == null) return;
            _state = State.Playing;
            try { _mp.SetPause(false); } catch (Exception ex) { Log.Debug("VlcPlaylistAudio.Resume: {E}", ex.Message); }
            StartTick();
        }

        public void Stop() => Stop(releaseNow: false);

        /// <param name="releaseNow">Exit: free the player before libvlc shuts down, on this thread.</param>
        private void Stop(bool releaseNow)
        {
            _tick?.Stop();
            var mp = _mp;
            var media = _media;
            _mp = null;
            _media = null;
            _path = null;
            _state = State.Stopped;
            _pendingMs = 0;
            if (mp == null) return;
            // Silence first (synchronous), then stop and free off the UI thread: libvlc's Stop can block,
            // and a player must never be disposed inside its own event.
            try { mp.Volume = 0; } catch (Exception ex) { Log.Debug("VlcPlaylistAudio stop mute: {E}", ex.Message); }
            void Release()
            {
                // Under the player's lock: OnVlcPlaying on the pool may still be driving it (restack fixw4d).
                try { lock (mp) { mp.Stop(); mp.Dispose(); } media?.Dispose(); }
                catch (Exception ex) { Log.Debug("VlcPlaylistAudio release: {E}", ex.Message); }
            }
            if (releaseNow) Release();
            else ThreadPool.QueueUserWorkItem(_ => Release());
        }

        public void Seek(double seconds)
        {
            if (_mp == null) return;
            var ms = (long)(Math.Max(0, seconds) * 1000);
            try
            {
                if (_state == State.Ended) return;
                if (_mp.State is VLCState.Playing or VLCState.Paused) { _mp.Time = ms; _pendingMs = 0; }
                else _pendingMs = ms;
            }
            catch (Exception ex) { Log.Debug("VlcPlaylistAudio.Seek: {E}", ex.Message); }
            // Push a tick at once so the bar and an engine cursor follow without waiting for the timer.
            try { PlaybackTimeMsChanged?.Invoke(ms); } catch (Exception ex) { Log.Debug(ex, "VlcPlaylistAudio seek tick"); }
        }

        public void Mute()
        {
            try { var mp = _mp; if (mp != null) mp.Volume = 0; }
            catch (Exception ex) { Log.Debug("VlcPlaylistAudio.Mute: {E}", ex.Message); }
        }

        // libvlc reached Playing (start or resume), on the pool: the device, then a pause or seek that came first.
        private void OnVlcPlaying(MediaPlayer mp)
        {
            // Stop drops _mp before its Release takes this lock, so a player seen here is never freed under us.
            lock (mp)
            {
                if (!ReferenceEquals(mp, _mp)) return;
                try
                {
                    LibVlcAudio.ApplyPreferredDevice(mp);
                    var pending = Interlocked.Exchange(ref _pendingMs, 0);
                    if (pending > 0) mp.Time = pending;
                    if (_state == State.Paused) mp.SetPause(true);   // a pause pressed while the file was opening
                }
                catch (Exception ex) { Log.Debug("VlcPlaylistAudio playing: {E}", ex.Message); }
            }
        }

        private void OnVlcEnded(MediaPlayer mp)
        {
            if (!ReferenceEquals(mp, _mp) || _state is State.Stopped or State.Ended) return;
            _state = State.Ended;   // still loaded, like WPF's reader after a natural end; Stop or Play unloads
            _tick?.Stop();
            try { Ended?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "VlcPlaylistAudio Ended subscriber"); }
        }

        private void OnVlcError(MediaPlayer mp)
        {
            if (!ReferenceEquals(mp, _mp)) return;
            Log.Warning("VlcPlaylistAudio: playback stopped with an error ({File})", Path.GetFileName(_path));
            // Raised while still loaded, so the listener's own Stop sees a loaded track and tells the card.
            try { StoppedUnexpectedly?.Invoke(); } catch (Exception ex) { Log.Debug(ex, "VlcPlaylistAudio StoppedUnexpectedly subscriber"); }
            if (ReferenceEquals(mp, _mp)) Stop();
        }

        private void StartTick()
        {
            if (_tick == null)
            {
                _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _tick.Tick += (_, _) =>
                {
                    try { PlaybackTimeMsChanged?.Invoke(CurrentTimeMs); }
                    catch (Exception ex) { Log.Debug("VlcPlaylistAudio tick error: {E}", ex.Message); }
                };
            }
            _tick.Start();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop(releaseNow: true);
        }
    }
}
