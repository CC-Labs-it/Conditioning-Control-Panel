using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.AudioPlayer
{
    /// <summary>
    /// The audio player with a playlist (ccp-bugs suggestion "A simple audio player with
    /// playlist, pause, skip controls"). Plays the player's own audio files, on its own or
    /// beside the engine. This interface is the fixed contract between the three lanes that
    /// build it: the service (core), the card (ui) and the Deeper wiring (deeper).
    ///
    /// Rules every implementation keeps:
    /// - Every member is called on the UI thread and every event fires on the UI thread.
    /// - Panic stops it at once (Stop), like any other sound in the app.
    /// - Loudness is Volume (0-100) scaled by the app's master volume.
    /// - Tracks are identified by their whole path, never the bare filename.
    /// </summary>
    public interface IAudioPlaylistService
    {
        /// <summary>The playlist in play order as the player arranged it (whole paths).</summary>
        IReadOnlyList<string> Tracks { get; }

        /// <summary>Index into <see cref="Tracks"/> of the loaded track, or -1 when nothing is loaded.</summary>
        int CurrentIndex { get; }

        /// <summary>Whole path of the loaded track, or null.</summary>
        string? CurrentPath { get; }

        bool IsPlaying { get; }
        bool IsPaused { get; }

        /// <summary>Position in the loaded track, seconds. 0 when nothing is loaded.</summary>
        double PositionSeconds { get; }

        /// <summary>Length of the loaded track, seconds. 0 when unknown.</summary>
        double DurationSeconds { get; }

        /// <summary>0-100, saved with the settings.</summary>
        int Volume { get; set; }

        /// <summary>Saved with the settings.</summary>
        AudioPlaylistLoop Loop { get; set; }

        /// <summary>Saved with the settings.</summary>
        bool Shuffle { get; set; }

        /// <summary>Appends files (whole paths); a path already in the list is skipped. Returns how many were added.</summary>
        int Add(IEnumerable<string> paths);

        void RemoveAt(int index);
        void Move(int fromIndex, int toIndex);
        void Clear();

        /// <summary>Plays the track at <paramref name="index"/> from its start.</summary>
        void PlayAt(int index);

        /// <summary>Resumes a paused track, or starts the current (or first) track.</summary>
        void Play();

        void Pause();

        /// <summary>Stops and rewinds; the playlist stays.</summary>
        void Stop();

        void Next();
        void Previous();
        void Seek(double seconds);

        /// <summary>The list changed (add, remove, move, clear).</summary>
        event Action? TracksChanged;

        /// <summary>A different track was loaded (or none). Argument is the new CurrentPath.</summary>
        event Action<string?>? TrackChanged;

        /// <summary>Playing / paused / stopped changed.</summary>
        event Action? StateChanged;

        /// <summary>About 10 times a second while playing. Argument is PositionSeconds.</summary>
        event Action<double>? PositionChanged;
    }

    public enum AudioPlaylistLoop
    {
        Off = 0,
        All = 1,
        One = 2,
    }
}
