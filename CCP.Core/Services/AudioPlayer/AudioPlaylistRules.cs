using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.AudioPlayer
{
    /// <summary>
    /// The playlist's rules with no audio and no settings in them: the list, which track is
    /// current, and where next / previous / the end of a track lead under loop and shuffle.
    /// <see cref="AudioPlaylistService"/> owns one and does the playing.
    ///
    /// Tracks are whole paths. Two paths that differ only in case are the same file (Windows).
    /// </summary>
    public sealed class AudioPlaylistRules
    {
        private const int HistoryCap = 200;

        private readonly List<string> _tracks = new();
        private readonly Random _rng;
        // Shuffle: the tracks not yet played in this round, and the ones played, newest last.
        // Both hold paths so a remove or a reorder cannot point them at the wrong track.
        private readonly List<string> _bag = new();
        private readonly List<string> _history = new();
        private bool _shuffle;

        public AudioPlaylistRules(Random? rng = null) { _rng = rng ?? new Random(); }

        public IReadOnlyList<string> Tracks => _tracks;

        /// <summary>The current track, or -1 with none.</summary>
        public int CurrentIndex { get; private set; } = -1;

        public string? CurrentPath => CurrentIndex >= 0 && CurrentIndex < _tracks.Count ? _tracks[CurrentIndex] : null;

        public AudioPlaylistLoop Loop { get; set; }

        public bool Shuffle
        {
            get => _shuffle;
            set
            {
                if (_shuffle == value) return;
                _shuffle = value;
                _bag.Clear();
                _history.Clear();
                if (value) RefillBag();
            }
        }

        /// <summary>Master volume folded into the player's own level; both 0..100.</summary>
        public static int EffectiveVolume(int volume, int masterVolume)
            => (int)Math.Round(Math.Clamp(volume, 0, 100) * Math.Clamp(masterVolume, 0, 100) / 100.0);

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private int IndexOf(string path) => _tracks.FindIndex(t => Same(t, path));

        /// <summary>Appends; blank paths and paths already in the list are skipped. Returns how many were added.</summary>
        public int Add(IEnumerable<string>? paths)
        {
            if (paths == null) return 0;
            int added = 0;
            foreach (var raw in paths)
            {
                var path = raw?.Trim();
                if (string.IsNullOrEmpty(path) || IndexOf(path) >= 0) continue;
                _tracks.Add(path);
                if (_shuffle) _bag.Add(path);
                added++;
            }
            return added;
        }

        /// <summary>
        /// Removes a track. Returns true when it was the current one: the track that slid into
        /// its place becomes current (with loop All the last track hands over to the first), and
        /// with nothing there the current index is -1.
        /// </summary>
        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _tracks.Count) return false;
            var path = _tracks[index];
            _tracks.RemoveAt(index);
            _bag.RemoveAll(p => Same(p, path));
            _history.RemoveAll(p => Same(p, path));

            if (index < CurrentIndex) { CurrentIndex--; return false; }
            if (index > CurrentIndex) return false;

            if (_tracks.Count == 0) CurrentIndex = -1;
            else if (index >= _tracks.Count) CurrentIndex = Loop == AudioPlaylistLoop.All ? 0 : -1;
            if (CurrentPath is string now) _bag.RemoveAll(p => Same(p, now));
            return true;
        }

        /// <summary>Reorders; the current track stays current wherever it lands. False when nothing moved.</summary>
        public bool Move(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= _tracks.Count) return false;
            toIndex = Math.Clamp(toIndex, 0, _tracks.Count - 1);
            if (fromIndex == toIndex) return false;
            var current = CurrentPath;
            var path = _tracks[fromIndex];
            _tracks.RemoveAt(fromIndex);
            _tracks.Insert(toIndex, path);
            if (current != null) CurrentIndex = IndexOf(current);
            return true;
        }

        public void Clear()
        {
            _tracks.Clear();
            _bag.Clear();
            _history.Clear();
            CurrentIndex = -1;
        }

        /// <summary>The player picked a track. Returns the index now current, or -1 when out of range.</summary>
        public int Select(int index)
        {
            if (index < 0 || index >= _tracks.Count) return -1;
            SetCurrent(index);
            return index;
        }

        /// <summary>
        /// The track that follows the current one. <paramref name="auto"/> = the track ended by
        /// itself: loop One repeats it, and past the last track only loop All carries on (-1
        /// otherwise, with the list rewound to its first track). A press on Next always moves and
        /// wraps. Returns the new current index, or -1.
        /// </summary>
        public int Advance(bool auto)
        {
            if (_tracks.Count == 0) { CurrentIndex = -1; return -1; }
            if (CurrentIndex < 0) return _shuffle ? Draw() : Select(0);
            if (auto && Loop == AudioPlaylistLoop.One) return CurrentIndex;

            if (_shuffle)
            {
                if (_bag.Count == 0)
                {
                    RefillBag();
                    if (auto && Loop != AudioPlaylistLoop.All) return -1;
                }
                return Draw();
            }

            int next = CurrentIndex + 1;
            if (next >= _tracks.Count)
            {
                if (auto && Loop != AudioPlaylistLoop.All) { CurrentIndex = 0; return -1; }
                next = 0;
            }
            SetCurrent(next);
            return next;
        }

        /// <summary>The track before: with shuffle the one played before this one, else the one above (wraps).</summary>
        public int Back()
        {
            if (_tracks.Count == 0) { CurrentIndex = -1; return -1; }
            if (_shuffle)
            {
                while (_history.Count > 0)
                {
                    var path = _history[^1];
                    _history.RemoveAt(_history.Count - 1);
                    int at = IndexOf(path);
                    if (at < 0 || at == CurrentIndex) continue;
                    CurrentIndex = at;      // not pushed: going back does not rewrite the history
                    return at;
                }
            }
            int prev = CurrentIndex <= 0 ? _tracks.Count - 1 : CurrentIndex - 1;
            CurrentIndex = prev;
            return prev;
        }

        /// <summary>
        /// Starts at <paramref name="index"/> and walks on (forward with <see cref="Advance"/>,
        /// else <see cref="Back"/>) until <paramref name="tryPlay"/> accepts a track: a file that
        /// is gone or will not open is skipped. Every track is tried at most once. Returns the
        /// index that plays, or -1 when none does.
        /// </summary>
        public int SelectPlayable(int index, bool forward, bool auto, Func<string, bool> tryPlay)
        {
            int tries = _tracks.Count;
            int at = index;
            // A repeat that will not open must not spin on itself.
            bool one = auto && Loop == AudioPlaylistLoop.One;
            while (tries-- > 0 && at >= 0 && at < _tracks.Count)
            {
                if (CurrentIndex != at) SetCurrent(at);
                if (tryPlay(_tracks[at])) return at;
                at = forward ? Advance(auto && !one) : Back();
            }
            return -1;
        }

        private void SetCurrent(int index)
        {
            var before = CurrentPath;
            CurrentIndex = index;
            var now = _tracks[index];
            if (before != null && !Same(before, now))
            {
                _history.Add(before);
                if (_history.Count > HistoryCap) _history.RemoveAt(0);
            }
            _bag.RemoveAll(p => Same(p, now));
        }

        private void RefillBag()
        {
            _bag.Clear();
            var current = CurrentPath;
            foreach (var t in _tracks)
                if (current == null || !Same(t, current) || _tracks.Count == 1) _bag.Add(t);
        }

        private int Draw()
        {
            if (_bag.Count == 0) RefillBag();
            if (_bag.Count == 0) return -1;
            int at = IndexOf(_bag[_rng.Next(_bag.Count)]);
            SetCurrent(at);
            return at;
        }
    }
}
