using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using ConditioningControlPanel.Services.AudioPlayer;
using ConditioningControlPanel.Models;


namespace ConditioningControlPanel.Avalonia.AudioPlayer
{
    internal sealed class AudioPlaylistService : IAudioPlaylistService, IDisposable
    {
        internal const double PreviousRestartsAfterSeconds = 3.0;

        private readonly AudioPlaylistRules _rules = new();
        private readonly IPlaylistAudioPlayer _player;
        private readonly AppSettings? _settings;
        private AppSettings CurrentSettings => _settings ?? CoreSettings.Current;
        private AppSettings? _hooked;
        private string? _announcedPath;
        private int _volume = 80;
        private bool _disposed;

        internal IPlaylistAudioPlayer Player => _player;

        public AudioPlaylistService(IPlaylistAudioPlayer? player = null, AppSettings? settings = null)
        {
            _player = player ?? new PlaylistAudioPlayer();
            _settings = settings;
            var s = CurrentSettings;
            if (s != null)
            {
                _rules.Add(s.AudioPlaylistPaths);
                _rules.Loop = (AudioPlaylistLoop)Math.Clamp(s.AudioPlaylistLoop, 0, 2);
                _rules.Shuffle = s.AudioPlaylistShuffle;
                _volume = Math.Clamp(s.AudioPlaylistVolume, 0, 100);
            }
            _player.PlaybackTimeMsChanged += OnPlayerTick;
            _player.Ended += OnPlayerEnded;
            _player.StoppedUnexpectedly += Stop;   // a device error is a stop the card must hear
            ApplyVolume();
        }

        public IReadOnlyList<string> Tracks => _rules.Tracks;
        public int CurrentIndex => _rules.CurrentIndex;
        public string? CurrentPath => _rules.CurrentPath;
        public bool IsPlaying => _player.IsPlaying;
        public bool IsPaused => _player.IsPaused;
        public double PositionSeconds => _player.CurrentPath == null ? 0 : _player.CurrentTimeMs / 1000.0;
        public double DurationSeconds => _player.CurrentPath == null ? 0 : _player.DurationMs / 1000.0;

        public int Volume
        {
            get => _volume;
            set
            {
                int v = Math.Clamp(value, 0, 100);
                if (v == _volume) return;
                _volume = v;
                ApplyVolume();
                Persist();
            }
        }

        public AudioPlaylistLoop Loop
        {
            get => _rules.Loop;
            set
            {
                if (!Enum.IsDefined(typeof(AudioPlaylistLoop), value) || _rules.Loop == value) return;
                _rules.Loop = value;
                Persist();
            }
        }

        public bool Shuffle
        {
            get => _rules.Shuffle;
            set
            {
                if (_rules.Shuffle == value) return;
                _rules.Shuffle = value;
                Persist();
            }
        }

        public event Action? TracksChanged;
        public event Action<string?>? TrackChanged;
        public event Action? StateChanged;
        public event Action<double>? PositionChanged;

        public int Add(IEnumerable<string> paths)
        {
            int added = _rules.Add(paths);
            if (added > 0) { Persist(); Raise(TracksChanged); }
            return added;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _rules.Tracks.Count) return;
            bool wasPlaying = _player.IsPlaying;
            bool removedCurrent = _rules.RemoveAt(index);
            Persist();
            Raise(TracksChanged);
            if (!removedCurrent) return;

            // The track on the device is gone from the list: move on if it was sounding, else rest.
            if (wasPlaying && _rules.CurrentIndex >= 0) StartFrom(_rules.CurrentIndex, forward: true, auto: false);
            else { _player.Stop(); Announce(); }
        }

        public void Move(int fromIndex, int toIndex)
        {
            if (!_rules.Move(fromIndex, toIndex)) return;
            Persist();
            Raise(TracksChanged);
        }

        public void Clear()
        {
            if (_rules.Tracks.Count == 0) return;
            _player.Stop();
            _rules.Clear();
            Persist();
            Raise(TracksChanged);
            Announce();
        }

        public void PlayAt(int index)
        {
            if (index < 0 || index >= _rules.Tracks.Count) return;
            StartFrom(index, forward: true, auto: false);
        }

        public void Play()
        {
            if (_disposed || _player.IsPlaying) return;
            if (_player.IsPaused)
            {
                ApplyVolume();
                _player.Resume();
                Raise(StateChanged);
                return;
            }
            int at = _rules.CurrentIndex >= 0 ? _rules.CurrentIndex : _rules.Advance(auto: false);
            if (at >= 0) StartFrom(at, forward: true, auto: false);
        }

        public void Pause()
        {
            if (!_player.IsPlaying) return;
            _player.Pause();
            Raise(StateChanged);
        }

        public void Stop()
        {
            bool wasLoaded = _player.CurrentPath != null;
            _player.Stop();
            ApplyVolume();      // undoes the mute an emergency stop left behind
            if (!wasLoaded) return;
            Raise(StateChanged);
            RaisePosition(0);
        }

        public void Next() => Step(forward: true);

        public void Previous()
        {
            if (_player.CurrentPath != null && PositionSeconds > PreviousRestartsAfterSeconds) { Seek(0); return; }
            Step(forward: false);
        }

        public void Seek(double seconds)
        {
            if (_player.CurrentPath == null || double.IsNaN(seconds)) return;
            double length = DurationSeconds;
            _player.Seek(length > 0 ? Math.Clamp(seconds, 0, length) : Math.Max(0, seconds));
        }

        public void StopEmergency()
        {
            try { _player.Volume = 0; } catch (Exception ex) { Serilog.Log.Debug(ex, "Playlist stop failed"); }
            try { OnUi(Stop); } catch (Exception ex) { Serilog.Log.Debug(ex, "Playlist stop failed"); }
        }

        private void Step(bool forward)
        {
            if (_rules.Tracks.Count == 0) return;
            bool loaded = _player.CurrentPath != null;
            int at = forward ? _rules.Advance(auto: false) : _rules.Back();
            if (at < 0) return;
            // A skip while nothing is loaded only picks the track; Play starts it.
            if (loaded) StartFrom(at, forward, auto: false);
            else Announce();
        }

        private void StartFrom(int index, bool forward, bool auto)
        {
            if (_disposed) return;
            int at = _rules.SelectPlayable(index, forward, auto, TryPlay);
            if (at < 0)
            {
                _player.Stop();
                Serilog.Log.Debug("AudioPlaylistService: no track in the list could be opened");
            }
            Announce();
            Raise(StateChanged);
            RaisePosition(PositionSeconds);
        }

        private bool TryPlay(string path)
        {
            ApplyVolume();
            bool ok = _player.Play(path);
            if (!ok) Serilog.Log.Information("AudioPlaylistService: skipped a track that would not open");
            return ok;
        }

        private void OnPlayerEnded()
        {
            if (_disposed) return;
            int at = _rules.Advance(auto: true);
            if (at >= 0) { StartFrom(at, forward: true, auto: true); return; }
            // End of the list with loop off: rest, rewound.
            _player.Stop();
            Announce();
            Raise(StateChanged);
            RaisePosition(0);
        }

        private void OnPlayerTick(long ms) => RaisePosition(ms / 1000.0);

        private void ApplyVolume()
        {
            var s = CurrentSettings;
            HookMaster(s);
            _player.Volume = AudioPlaylistRules.EffectiveVolume(_volume, s?.MasterVolume ?? 100);
        }

        // The settings object can be swapped whole (cloud restore), so the hook follows it.
        private void HookMaster(AppSettings? s)
        {
            if (ReferenceEquals(s, _hooked)) return;
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsChanged;
            _hooked = s;
            if (s != null) s.PropertyChanged += OnSettingsChanged;
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AppSettings.MasterVolume) || _disposed) return;
            OnUi(() => { if (!_disposed) ApplyVolume(); });
        }

        private void Persist()
        {
            try
            {
                var s = CurrentSettings;
                if (s == null) return;
                s.AudioPlaylistPaths = new List<string>(_rules.Tracks);
                s.AudioPlaylistVolume = _volume;
                s.AudioPlaylistLoop = (int)_rules.Loop;
                s.AudioPlaylistShuffle = _rules.Shuffle;

                CoreSettings.Save(suppressCloudBackup: true);
            }
            catch (Exception ex) { Serilog.Log.Debug("AudioPlaylistService.Persist error: {Error}", ex.Message); }
        }

        private static void OnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        }

        private void Announce()
        {
            var now = _rules.CurrentPath;
            if (string.Equals(now, _announcedPath, StringComparison.OrdinalIgnoreCase)) return;
            _announcedPath = now;
            try { TrackChanged?.Invoke(now); }
            catch (Exception ex) { Serilog.Log.Debug("AudioPlaylistService TrackChanged subscriber error: {Error}", ex.Message); }
        }

        private static void Raise(Action? handler)
        {
            try { handler?.Invoke(); }
            catch (Exception ex) { Serilog.Log.Debug("AudioPlaylistService subscriber error: {Error}", ex.Message); }
        }

        private void RaisePosition(double seconds)
        {
            try { PositionChanged?.Invoke(seconds); }
            catch (Exception ex) { Serilog.Log.Debug("AudioPlaylistService PositionChanged subscriber error: {Error}", ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            HookMaster(null);
            _player.PlaybackTimeMsChanged -= OnPlayerTick;
            _player.Ended -= OnPlayerEnded;
            _player.StoppedUnexpectedly -= Stop;
            _player.Dispose();
        }
    }
}
