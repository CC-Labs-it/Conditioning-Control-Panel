using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>The editor's local-audio transport: WPF's WaveOutEvent + AudioFileReader pair
    /// (DeeperEditorWindow.xaml.cs:1214) on the process's shared LibVLC. One seam so tests can
    /// drive the editor's play/seek/end path without a sound card.</summary>
    internal interface IDeeperLocalAudio : IDisposable
    {
        int Volume { get; set; }
        double DurationSeconds { get; }
        double? PlaybackDurationSeconds { get; }
        double PositionSeconds { get; set; }
        void Play();
        void Pause();
        /// <summary>Raised off the UI thread once the clip has played to its end and rewound.</summary>
        event Action? Ended;
        event Action? Failed;
    }

    internal static class DeeperLocalAudio
    {
        /// <summary>Null when libvlc did not load or the file will not parse. Tests swap it.</summary>
        internal static Func<string, Task<IDeeperLocalAudio?>> Open { get; set; } = OpenVlcAsync;

        internal static Func<string, Image, Task<IDeeperLocalAudio?>> OpenVideo { get; set; } = OpenVideoAsync;

        private static Task<IDeeperLocalAudio?> OpenVlcAsync(string path) => OpenVlcAsync(path, null);
        private static Task<IDeeperLocalAudio?> OpenVideoAsync(string path, Image image) => OpenVlcAsync(path, image);

        private static async Task<IDeeperLocalAudio?> OpenVlcAsync(string path, Image? image)
        {
            var vlc = LibVlcAudio.Shared;
            if (vlc == null) return null;
            var media = new Media(vlc, path, FromType.FromPath);
            if (image == null) media.AddOption(LibVlcAudio.NoVideo);
            // AudioFileReader.TotalTime: the parse gives the duration before anything plays.
            var status = await LibVlcAudio.ParseAsync(media, MediaParseOptions.ParseLocal, 5000);
            if (status != MediaParsedStatus.Done || media.Duration <= 0) { media.Dispose(); return null; }
            // Prime with audio disabled at the media level: LibVLC mute is not reliable before
            // an output exists. The original media is used only after an explicit Play.
            var previewMedia = image == null ? null : new Media(vlc, path, FromType.FromPath);
            previewMedia?.AddOption(ConditioningControlPanel.Services.LibVlcSilence.NoAudioOption);
            Media currentMedia = previewMedia ?? media;
            var mp = new MediaPlayer(currentMedia);
            Transport? audio = null;
            var sink = image == null ? null : new VlcFrameSink(mp, () => currentMedia, bmp => image.Source = bmp,
                () => { image.InvalidateVisual(); audio?.OnVideoFrame(); });
            audio = new Transport(new VlcEngine(mp, previewMedia == null ? null : () =>
            {
                mp.Stop();
                currentMedia = media;
                mp.Media = media;
                LibVlcAudio.PreparePreferredDevice(mp);
            }), media.Duration, () =>
            {
                try { mp.Stop(); sink?.Free(); mp.Dispose(); previewMedia?.Dispose(); media.Dispose(); }
                catch (Exception ex) { Serilog.Log.Debug(ex, "DeeperEditor: local audio dispose"); }
            });
            // Never act inside libvlc's own event (deadlock; device and seek do not stick there).
            mp.Playing += (_, _) => ThreadPool.QueueUserWorkItem(_ =>
            {
                audio.OnPlaying();
                lock (audio.Gate) if (!audio.IsDisposed) LibVlcAudio.ApplyPreferredDevice(mp);
            });
            mp.EncounteredError += (_, _) => ThreadPool.QueueUserWorkItem(_ => audio.OnError());
            mp.Paused += (_, _) => ThreadPool.QueueUserWorkItem(_ => audio.OnPaused());
            mp.EndReached += (_, _) => ThreadPool.QueueUserWorkItem(_ => audio.OnEndReached());
            if (image != null) audio.PrimeVideo();
            return audio;
        }

        /// <summary>The MediaPlayer members the transport uses; a seam so its state rules are
        /// tested without libvlc or real time.</summary>
        internal interface IEngine
        {
            int Volume { get; set; }
            VLCState State { get; }
            long Time { get; set; }
            long Length { get; }
            bool Play();
            void SetPause(bool pause);
            void Stop();
            bool PreparePlayback();
        }

        private sealed class VlcEngine(MediaPlayer p, Action? switchToFull) : IEngine
        {
            public int Volume { get => p.Volume; set => p.Volume = value; }
            public VLCState State => p.State;
            public long Time { get => p.Time; set => p.Time = value; }
            public long Length => p.Length;
            public bool Play() => p.Play();
            public void SetPause(bool pause) => p.SetPause(pause);
            public void Stop() => p.Stop();
            public bool PreparePlayback()
            {
                var switchMedia = switchToFull;
                if (switchMedia == null) return false;
                switchToFull = null;
                switchMedia();
                return true;
            }
        }

        /// <summary>Play/pause/seek/end over an <see cref="IEngine"/>. <see cref="OnPlaying"/> and
        /// <see cref="OnEndReached"/> are called off libvlc's thread by the adapter.</summary>
        internal sealed class Transport(IEngine player, long parsedMs, Action release) : IDeeperLocalAudio
        {
            internal readonly object Gate = new();
            private long _pendingMs;   // a seek made before playback could take it; consumed once
            private bool _primingVideo, _primePlaying, _primePauseRequested, _playAfterPrime;
            private Timer? _primeTimer;
            private long _playbackLengthMs;
            private int _disposed;
            internal bool IsDisposed => _disposed != 0;

            private int _volume = 100;
            public int Volume { get => _volume; set { lock (Gate) { _volume = Math.Clamp(value, 0, 100); if (Live) player.Volume = LibVlcAudio.VlcVolume(_volume / 100f); } } }
            public event Action? Ended;
            public event Action? Failed;

            private bool Live => player.State is VLCState.Playing or VLCState.Paused;

            // The parse misreads some LAME/Xing mp3 headers (measured: a 6.0 s ffmpeg mp3 parsed as
            // 12.1 s); the playing player's Length is exact, so it wins once known.
            public double? PlaybackDurationSeconds
            {
                get
                {
                    lock (Gate)
                    {
                        var length = Live ? player.Length : -1;
                        if (length > 0) _playbackLengthMs = length;
                        return _playbackLengthMs > 0 ? _playbackLengthMs / 1000.0 : null;
                    }
                }
            }
            public double DurationSeconds => PlaybackDurationSeconds ?? parsedMs / 1000.0;

            public double PositionSeconds
            {
                get
                {
                    lock (Gate)
                    {
                        var t = Live ? player.Time : -1;
                        return (t >= 0 ? t : _pendingMs) / 1000.0;
                    }
                }
                set
                {
                    lock (Gate)
                    {
                        var ms = (long)(Math.Clamp(value, 0, DurationSeconds) * 1000);
                        if (Live) { player.Time = ms; _pendingMs = 0; }
                        else _pendingMs = ms;
                    }
                }
            }

            public void Play()
            {
                lock (Gate)
                {
                    if (_primingVideo) { _playAfterPrime = true; return; }
                    StartPlayback();
                }
            }

            private void StartPlayback()
            {
                var position = Live ? player.Time : _pendingMs;
                if (player.PreparePlayback())
                {
                    _pendingMs = Math.Max(0, position);
                    if (!player.Play()) throw new InvalidOperationException("Audio transport could not start");
                }
                else if (player.State == VLCState.Paused) player.SetPause(false);
                else if (!player.Play()) throw new InvalidOperationException("Audio transport could not start");
            }

            /// <summary>Paused when playing; while the clip is still opening, stopped, so a pause
            /// (or a panic) in that gap cannot be followed by audio starting under a play glyph.</summary>
            public void Pause()
            {
                lock (Gate)
                {
                    if (_primingVideo)
                    {
                        _primingVideo = false;
                        _playAfterPrime = false;
                        _primeTimer?.Dispose();
                        _primeTimer = null;
                        player.Stop();
                        return;
                    }
                    if (player.State == VLCState.Playing) player.SetPause(true);
                    else if (player.State != VLCState.Paused) player.Stop();
                }
            }

            // WPF starts the video once and pauses it to render the first frame.
            internal void PrimeVideo()
            {
                lock (Gate)
                {
                    if (IsDisposed) return;
                    _primingVideo = true;
                    _primePlaying = false;
                    _primePauseRequested = false;
                    _playAfterPrime = false;
                    // A slow open gets the long bound; Playing re-arms the short frame bound.
                    _primeTimer = new Timer(_ => OnPrimeTimeout(), null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
                    player.Play();
                }
            }

            internal void OnPrimeTimeout()
            {
                lock (Gate)
                {
                    if (IsDisposed || !_primingVideo) return;
                    _primingVideo = false;
                    _primeTimer?.Dispose();
                    _primeTimer = null;
                    player.Stop();
                    if (_playAfterPrime) StartPlayback();
                    _playAfterPrime = false;
                }
            }

            /// <summary>Apply a pending seek off the VLC thread.</summary>
            internal void OnPlaying()
            {
                lock (Gate)
                {
                    if (IsDisposed) return;
                    player.Volume = LibVlcAudio.VlcVolume(_volume / 100f);
                    if (_pendingMs > 0) { player.Time = _pendingMs; _pendingMs = 0; }
                    if (_primingVideo) { _primePlaying = true; _primeTimer?.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan); }
                }
            }

            // Playing can fire before a frame exists. Pause only after the sink has delivered one.
            internal void OnVideoFrame()
            {
                lock (Gate)
                {
                    if (IsDisposed || !_primingVideo || !_primePlaying || _primePauseRequested) return;
                    _primePauseRequested = true;
                    player.SetPause(true);
                }
            }

            // A click during Opening must survive the asynchronous initial pause.
            internal void OnPaused()
            {
                lock (Gate)
                {
                    if (IsDisposed || !_primingVideo) return;
                    _primingVideo = false;
                    _primeTimer?.Dispose();
                    _primeTimer = null;
                    _primePlaying = false;
                    _primePauseRequested = false;
                    if (_playAfterPrime) StartPlayback();
                    _playAfterPrime = false;
                }
            }

            /// <summary>WPF OnVlcEndReached: Stop + rewind so the next Play replays.</summary>
            internal void OnEndReached()
            {
                lock (Gate)
                {
                    if (IsDisposed) return;
                    var length = player.Length;
                    if (length > 0) _playbackLengthMs = length;
                    _primeTimer?.Dispose(); _primeTimer = null;
                    player.Stop(); _pendingMs = 0; _primingVideo = false; _playAfterPrime = false;
                }
                Ended?.Invoke();
            }

            internal void OnError()
            {
                lock (Gate) { if (IsDisposed) return; _primeTimer?.Dispose(); _primeTimer = null; _primingVideo = false; _playAfterPrime = false; player.Stop(); }
                Failed?.Invoke();
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                Ended = null; Failed = null;
                _primeTimer?.Dispose();
                // Never dispose a player from inside its own libvlc event: that deadlocks.
                ThreadPool.QueueUserWorkItem(_ => { lock (Gate) release(); });
            }
        }
    }
}
