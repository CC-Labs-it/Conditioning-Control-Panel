using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.AudioPlayer;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>One line of the playlist as the card draws it.</summary>
    public sealed class AudioPlaylistRow
    {
        public int Index { get; init; }
        public string Path { get; init; } = "";
        public string Name { get; init; } = "";
        public string Number => (Index + 1).ToString();
        public bool IsCurrent { get; init; }
        public bool CanMoveUp { get; init; }
        public bool CanMoveDown { get; init; }
    }

    /// <summary>
    /// Everything the playlist card knows, with no WPF in it: the card binds to this and a test
    /// drives it with a fake <see cref="IAudioPlaylistService"/>. A null service is the empty,
    /// switched-off card (nothing throws, nothing can be added).
    /// </summary>
    public sealed class AudioPlaylistCardModel : INotifyPropertyChanged
    {
        public static readonly string[] Extensions = { ".mp3", ".wav", ".flac", ".m4a" };

        private readonly IAudioPlaylistService? _service;
        private readonly Func<bool> _readEnhancements;
        private readonly Action<bool> _writeEnhancements;
        private bool _attached;
        private bool _scrubbing;
        /// <summary>True while a seek drag is live; a track change ends it.</summary>
        public bool IsScrubbing => _scrubbing;
        private double _position;

        /// <param name="readEnhancements">The "enhancements run with tracks" switch; the app's setting when null.</param>
        /// <param name="writeEnhancements">Stores the switch and tells the bridge; the app's setting when null.</param>
        public AudioPlaylistCardModel(IAudioPlaylistService? service,
            Func<bool>? readEnhancements = null, Action<bool>? writeEnhancements = null)
        {
            _service = service;
            _readEnhancements = readEnhancements ?? ReadEnhancementsSetting;
            _writeEnhancements = writeEnhancements ?? WriteEnhancementsSetting;
            Refresh();
        }

        private static bool ReadEnhancementsSetting() => App.Settings?.Current?.AudioPlaylistEnhancements != false;

        private static void WriteEnhancementsSetting(bool on)
        {
            var settings = App.Settings;
            if (settings?.Current == null) return;
            settings.Current.AudioPlaylistEnhancements = on;
            // The bridge first: off must stop a running enhancement before anything else happens.
            Services.Deeper.PlaylistEnhancementBridge.Installed?.Refresh();
            try { settings.Save(); }
            catch (Exception ex) { App.Logger?.Debug("AudioPlaylistCardModel save error: {Error}", ex.Message); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<AudioPlaylistRow> Rows { get; } = new();

        public bool IsAvailable => _service != null;
        public bool HasTracks => Rows.Count > 0;
        public bool IsEmpty => Rows.Count == 0;
        public bool IsPlaying => _service?.IsPlaying ?? false;
        public bool HasCurrent => _service?.CurrentPath != null;
        public string CurrentName => DisplayName(_service?.CurrentPath);

        public double DurationSeconds => Math.Max(0, _service?.DurationSeconds ?? 0);
        public bool CanSeek => DurationSeconds > 0;
        /// <summary>The seek bar's top end; never 0, so an unknown length is still a bar.</summary>
        public double SeekMaximum => CanSeek ? DurationSeconds : 1;
        public double PositionSeconds => _position;
        public string ElapsedText => FormatTime(_position);
        public string TotalText => CanSeek ? FormatTime(DurationSeconds) : "--:--";

        public int Volume
        {
            get => _service?.Volume ?? 0;
            set
            {
                if (_service == null) return;
                var v = Math.Clamp(value, 0, 100);
                if (_service.Volume != v) _service.Volume = v;
                Raise(nameof(Volume), nameof(VolumeText));
            }
        }

        public string VolumeText => Volume + "%";

        public bool Shuffle
        {
            get => _service?.Shuffle ?? false;
            set
            {
                if (_service == null) return;
                if (_service.Shuffle != value) _service.Shuffle = value;
                Raise(nameof(Shuffle));
            }
        }

        public AudioPlaylistLoop Loop => _service?.Loop ?? AudioPlaylistLoop.Off;

        /// <summary>True when loop or shuffle is off its default, so the fold never hides a changed setting.</summary>
        public bool HasChangedOptions => Loop != AudioPlaylistLoop.Off || Shuffle || (IsAvailable && !Enhancements);

        /// <summary>A track's Deeper enhancement runs with it. On by default; off stops a running one at once.</summary>
        public bool Enhancements
        {
            get => _readEnhancements();
            set
            {
                if (_service == null) return;
                if (_readEnhancements() != value) _writeEnhancements(value);
                Raise(nameof(Enhancements));
            }
        }

        /// <summary>The picker's filter line; <paramref name="label"/> is the localised "Audio files".</summary>
        public static string DialogFilter(string label)
        {
            var mask = string.Join(";", Extensions.Select(e => "*" + e));
            return $"{label} ({mask})|{mask}";
        }

        public static bool IsSupported(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext;
            try { ext = System.IO.Path.GetExtension(path); }
            catch (ArgumentException) { return false; }
            return Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        public static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
            var t = TimeSpan.FromSeconds(Math.Floor(seconds));
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";
        }

        public static string DisplayName(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return System.IO.Path.GetFileNameWithoutExtension(path); }
            catch (ArgumentException) { return path; }
        }

        /// <summary>Starts listening to the service. Safe to call twice.</summary>
        public void Attach()
        {
            if (_service == null || _attached) return;
            _attached = true;
            _service.TracksChanged += OnTracksChanged;
            _service.TrackChanged += OnTrackChanged;
            _service.StateChanged += OnStateChanged;
            _service.PositionChanged += OnPositionChanged;
            Refresh();
        }

        public void Detach()
        {
            if (_service == null || !_attached) return;
            _attached = false;
            _service.TracksChanged -= OnTracksChanged;
            _service.TrackChanged -= OnTrackChanged;
            _service.StateChanged -= OnStateChanged;
            _service.PositionChanged -= OnPositionChanged;
        }

        /// <summary>Adds the audio files among <paramref name="paths"/>; anything else is dropped. Returns how many went in.</summary>
        public int AddFiles(IEnumerable<string>? paths)
        {
            if (_service == null || paths == null) return 0;
            var audio = paths.Where(IsSupported).ToList();
            return audio.Count == 0 ? 0 : _service.Add(audio);
        }

        public void Remove(int index)
        {
            if (InRange(index)) _service!.RemoveAt(index);
        }

        public void MoveUp(int index)
        {
            if (InRange(index) && index > 0) _service!.Move(index, index - 1);
        }

        public void MoveDown(int index)
        {
            if (InRange(index) && index < _service!.Tracks.Count - 1) _service.Move(index, index + 1);
        }

        public void Clear() => _service?.Clear();

        public void PlayRow(int index)
        {
            if (InRange(index)) _service!.PlayAt(index);
        }

        public void TogglePlay()
        {
            if (_service == null || _service.Tracks.Count == 0) return;
            if (_service.IsPlaying) _service.Pause();
            else _service.Play();
        }

        public void Next()
        {
            if (HasTracks) _service!.Next();
        }

        public void Previous()
        {
            if (HasTracks) _service!.Previous();
        }

        /// <summary>Off, then All, then One, then Off again.</summary>
        public void CycleLoop()
        {
            if (_service == null) return;
            _service.Loop = _service.Loop switch
            {
                AudioPlaylistLoop.Off => AudioPlaylistLoop.All,
                AudioPlaylistLoop.All => AudioPlaylistLoop.One,
                _ => AudioPlaylistLoop.Off,
            };
            Raise(nameof(Loop));
        }

        /// <summary>The thumb is held: the bar follows the hand, not the player, until <see cref="Seek"/>.</summary>
        public void Scrub(double seconds)
        {
            _scrubbing = true;
            SetPosition(seconds);
        }

        public void Seek(double seconds)
        {
            _scrubbing = false;
            if (_service == null || !CanSeek) return;
            var target = Math.Clamp(seconds, 0, DurationSeconds);
            _service.Seek(target);
            SetPosition(target);
        }

        /// <summary>Re-reads everything from the service.</summary>
        public void Refresh()
        {
            RebuildRows();
            if (!_scrubbing) _position = ClampPosition(_service?.PositionSeconds ?? 0);
            Raise(nameof(IsAvailable), nameof(IsPlaying), nameof(HasCurrent), nameof(CurrentName),
                nameof(DurationSeconds), nameof(CanSeek), nameof(SeekMaximum), nameof(PositionSeconds),
                nameof(ElapsedText), nameof(TotalText), nameof(Volume), nameof(VolumeText),
                nameof(Shuffle), nameof(Loop), nameof(Enhancements));
        }

        private bool InRange(int index) => _service != null && index >= 0 && index < _service.Tracks.Count;

        private double ClampPosition(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) return 0;
            return CanSeek ? Math.Min(seconds, DurationSeconds) : seconds;
        }

        private void SetPosition(double seconds)
        {
            _position = ClampPosition(seconds);
            Raise(nameof(PositionSeconds), nameof(ElapsedText));
        }

        private void RebuildRows()
        {
            var tracks = _service?.Tracks ?? Array.Empty<string>();
            var current = _service?.CurrentIndex ?? -1;
            Rows.Clear();
            for (var i = 0; i < tracks.Count; i++)
            {
                Rows.Add(new AudioPlaylistRow
                {
                    Index = i,
                    Path = tracks[i],
                    Name = DisplayName(tracks[i]),
                    IsCurrent = i == current,
                    CanMoveUp = i > 0,
                    CanMoveDown = i < tracks.Count - 1,
                });
            }
            Raise(nameof(HasTracks), nameof(IsEmpty));
        }

        private void OnTracksChanged() => Refresh();

        private void OnTrackChanged(string? _)
        {
            _scrubbing = false;
            Refresh();
        }

        private void OnStateChanged() => Refresh();

        private void OnPositionChanged(double seconds)
        {
            if (_scrubbing) return;
            // The length is often known only once the file is open, so it rides along here.
            Raise(nameof(DurationSeconds), nameof(CanSeek), nameof(SeekMaximum), nameof(TotalText));
            SetPosition(seconds);
        }

        private void Raise(params string[] names)
        {
            var handler = PropertyChanged;
            if (handler == null) return;
            foreach (var name in names) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
