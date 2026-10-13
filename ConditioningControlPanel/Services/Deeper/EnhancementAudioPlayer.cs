using System;
using System.IO;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Long-form audio player for end-user Deeper enhancement playback.
    ///
    /// Separate from <see cref="AudioService"/> — that one is built around
    /// short cue clips (StopSound on every Play, no Pause/Seek), so it's the
    /// wrong shape for sitting through a 20-minute hypno track. This player
    /// uses MediaFoundationReader (covers MP3/AAC/WAV/M4A) + WaveOutEvent
    /// and exposes Play / Pause / Resume / Stop / Seek with a 100 ms polling
    /// timer that emits PlaybackTimeMsChanged so the engine has a tick stream
    /// to bind to.
    ///
    /// Eager construct (cheap), lazy resource open. Disposing kills the
    /// underlying reader + output device.
    /// </summary>
    public sealed class EnhancementAudioPlayer : IDisposable
    {
        private MediaFoundationReader? _reader;
        private WaveOutEvent? _output;
        // The level is a SAMPLE gain. Never WaveOutEvent.Volume: that is waveOutSetVolume, which
        // moves the whole app in the Windows mixer and is remembered across launches.
        private VolumeSampleProvider? _gain;
        private DispatcherTimer? _tickTimer;
        private string? _currentPath;
        private bool _disposed;

        public string? CurrentPath => _currentPath;
        public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;
        public bool IsPaused => _output?.PlaybackState == PlaybackState.Paused;
        public long CurrentTimeMs => _reader == null ? 0 : (long)_reader.CurrentTime.TotalMilliseconds;
        public long DurationMs => _reader == null ? 0 : (long)_reader.TotalTime.TotalMilliseconds;

        // Cached so a level set before the first Play (or between Stop and the
        // next Play) survives: WaveOutEvent is rebuilt on every Play at 1.0, and
        // the old setter was a no-op with no output, so the slider said 80 while
        // the device ran at 100 until the user touched it.
        private int _volume = DefaultVolume;
        public const int DefaultVolume = 80;

        /// <summary>0..100. Default 80. Applies to the live output and to every later Play.</summary>
        public int Volume
        {
            get => _volume;
            set
            {
                _volume = ClampVolume(value);
                ApplyVolume();
            }
        }

        internal static int ClampVolume(int value) => Math.Clamp(value, 0, 100);

        private void ApplyVolume()
        {
            try { var gain = _gain; if (gain != null) gain.Volume = _volume / 100f; }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Volume apply error: {Error}", ex.Message); }
        }

        /// <summary>
        /// Fires from the UI thread on each polling tick while playing.
        /// Argument is current time in milliseconds.
        /// </summary>
        public event Action<long>? PlaybackTimeMsChanged;

        /// <summary>Fires (UI thread) when playback reaches the end naturally.</summary>
        public event Action? Ended;

        /// <summary>Fires (UI thread) when a device error stops playback (headphones pulled): not Stop, not the end of the file.</summary>
        public event Action? StoppedUnexpectedly;

        /// <summary>Fires (UI thread) when a new file is loaded successfully.</summary>
        public event Action<string>? Loaded;

        /// <summary>
        /// Open and start playing a file. Replaces any in-flight playback.
        /// Returns false on any failure (file missing, codec unsupported, etc.).
        /// </summary>
        public bool Play(string path)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                App.Logger?.Debug("EnhancementAudioPlayer.Play: file missing ({Path})", path);
                return false;
            }

            try
            {
                Stop();

                _reader = new MediaFoundationReader(path);
                _output = new WaveOutEvent { DesiredLatency = 200 };
                App.Audio?.ApplyPreferredDevice(_output);
                _gain = new VolumeSampleProvider(_reader.ToSampleProvider()) { Volume = _volume / 100f };
                _output.Init(_gain);
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Play();
                _currentPath = path;

                EnsureTimer();
                _tickTimer?.Start();

                try { Loaded?.Invoke(path); }
                catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer Loaded subscriber error: {Error}", ex.Message); }
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementAudioPlayer.Play failed");
                Stop();
                return false;
            }
        }

        public void Pause()
        {
            try { _output?.Pause(); _tickTimer?.Stop(); }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Pause error: {Error}", ex.Message); }
        }

        public void Resume()
        {
            try { _output?.Play(); _tickTimer?.Start(); }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Resume error: {Error}", ex.Message); }
        }

        public void Stop()
        {
            try
            {
                _tickTimer?.Stop();
                if (_output != null)
                {
                    _output.PlaybackStopped -= OnPlaybackStopped;
                    _output.Stop();
                    _output.Dispose();
                    _output = null;
                }
                _reader?.Dispose();
                _reader = null;
                _gain = null;
                _currentPath = null;
            }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Stop error: {Error}", ex.Message); }
        }

        public void Seek(double seconds)
        {
            try
            {
                if (_reader == null) return;
                _reader.CurrentTime = TimeSpan.FromSeconds(Math.Max(0, seconds));
                // Push a tick immediately so the engine cursor rewinds without
                // waiting for the next 100 ms timer fire.
                try { PlaybackTimeMsChanged?.Invoke(CurrentTimeMs); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer.Seek error: {Error}", ex.Message); }
        }

        private void EnsureTimer()
        {
            if (_tickTimer != null) return;
            _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _tickTimer.Tick += (_, _) =>
            {
                try { PlaybackTimeMsChanged?.Invoke(CurrentTimeMs); }
                catch (Exception ex) { App.Logger?.Debug("EnhancementAudioPlayer tick error: {Error}", ex.Message); }
            };
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            // NAudio raises this on its WaveOut callback thread. The whole
            // body must run on the UI thread because (a) DispatcherTimer.Stop
            // silently corrupts the timer when called off-thread, and (b) the
            // _reader / _output Position/Length read can race with a UI-thread
            // Stop() that's mid-dispose. Marshal first, then read state.
            var ex = e.Exception;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            void Handle()
            {
                try
                {
                    _tickTimer?.Stop();
                    bool naturalEnd = IsNaturalEnd(ex != null, _reader?.Position ?? -1, _reader?.Length ?? 0,
                        _reader?.WaveFormat.AverageBytesPerSecond ?? 0);
                    if (naturalEnd)
                    {
                        try { Ended?.Invoke(); } catch (Exception ex) { Diag.Swallowed(ex); }
                    }
                    if (ex != null)
                    {
                        App.Logger?.Warning(ex, "EnhancementAudioPlayer playback stopped with error");
                        if (!naturalEnd)
                        {
                            try { StoppedUnexpectedly?.Invoke(); } catch (Exception ex3) { Diag.Swallowed(ex3); }
                        }
                    }
                }
                catch (Exception ex2) { Diag.Swallowed(ex2); }
            }

            if (dispatcher.CheckAccess()) Handle();
            else { try { dispatcher.BeginInvoke((Action)Handle); } catch (Exception ex2) { Diag.Swallowed(ex2); } }
        }

        /// <summary>
        /// Did the file play out? Stop() unhooks this handler first, so a stop that arrives here with
        /// no error IS the end of the file. Never compare Position with Length alone: an MP3 ends
        /// about a kilobyte short of the length MediaFoundation reports (1055 bytes measured on a
        /// mono file), so a fixed 1024-byte margin missed every MP3 and the playlist sat silent
        /// after track one. A device error counts as the end only inside the last half second.
        /// </summary>
        internal static bool IsNaturalEnd(bool hadError, long position, long length, int bytesPerSecond)
        {
            if (position < 0 || length <= 0) return false;
            if (!hadError) return true;
            return position >= length - Math.Max(1024, bytesPerSecond / 2);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
