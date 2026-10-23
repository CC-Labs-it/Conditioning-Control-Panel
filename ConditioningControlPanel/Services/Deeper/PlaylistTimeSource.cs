using System;
using ConditioningControlPanel.Services.AudioPlayer;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Adapts the playlist audio player (<see cref="IAudioPlaylistService"/>) to
    /// <see cref="IPlaybackTimeSource"/> for the enhancement engine. Built on the
    /// interface's events only, so it never needs the concrete service.
    ///
    /// The source is pinned to ONE track (whole path). Once the playlist has moved
    /// to another track it goes quiet: no tick is forwarded, the time reads 0 and
    /// a seek is ignored, so an engine that has not been unbound yet can never act
    /// on the next track's clock. Audio only: GetVideoRect is always empty.
    /// </summary>
    public sealed class PlaylistTimeSource : IPlaybackTimeSource, IDisposable
    {
        private readonly IAudioPlaylistService _playlist;
        private bool _attached;

        /// <summary>Whole path of the track this source reports for.</summary>
        public string TrackPath { get; }

        public event Action<double>? PlaybackTimeChanged;

        public PlaylistTimeSource(IAudioPlaylistService playlist, string trackPath)
        {
            _playlist = playlist ?? throw new ArgumentNullException(nameof(playlist));
            TrackPath = trackPath ?? throw new ArgumentNullException(nameof(trackPath));
        }

        public bool IsAttached => _attached;

        public void Attach()
        {
            if (_attached) return;
            _playlist.PositionChanged += OnPosition;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached) return;
            _playlist.PositionChanged -= OnPosition;
            _attached = false;
        }

        /// <summary>True while the playlist still has this source's track loaded.</summary>
        private bool OnMyTrack =>
            string.Equals(_playlist.CurrentPath, TrackPath, StringComparison.OrdinalIgnoreCase);

        private void OnPosition(double seconds)
        {
            if (!OnMyTrack) return;
            try { PlaybackTimeChanged?.Invoke(seconds); }
            catch (Exception ex) { App.Logger?.Debug("PlaylistTimeSource handler error: {Error}", ex.Message); }
        }

        public double GetCurrentTimeSeconds() => OnMyTrack ? _playlist.PositionSeconds : 0;
        public double GetDurationSeconds() => OnMyTrack ? _playlist.DurationSeconds : 0;
        public bool IsPlaying => OnMyTrack && _playlist.IsPlaying;

        // Transport is dead once the player is stopped or this source is detached: an engine
        // tearing down (a speak prompt releasing its hold) must never start the track again.
        private bool CanDrive => _attached && OnMyTrack && (_playlist.IsPlaying || _playlist.IsPaused);

        public void Seek(double seconds) { if (CanDrive) _playlist.Seek(seconds); }
        public void Pause() { if (CanDrive) _playlist.Pause(); }
        public void Play() { if (CanDrive) _playlist.Play(); }

        public PlaybackRect GetVideoRect() => PlaybackRect.Empty;

        public void Dispose() => Detach();
    }
}
