using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion.Brain
{
    /// <summary>
    /// Structural anti-fixation (doc 01 §3.4). Tracks the last few titles the companion suggested —
    /// from ANY source — and injects them as a single exclusion line in the prompt's dynamic tail.
    ///
    /// <para>This replaces the per-call Fisher-Yates shuffle of the example-title list, which was
    /// both the #1 provider-cache killer (no two prompts byte-identical) and a weaker fix: the model
    /// got a random subset and was merely *hoped* not to fixate. Here it gets a stable full list plus
    /// an explicit "not these" set.</para>
    ///
    /// <para>Pure logic with an injectable clock so the TTL is testable. Thread-safe: reactions and
    /// chat replies both call <see cref="Note"/>.</para>
    /// </summary>
    public sealed class RecentRecommendations
    {
        /// <summary>How many titles are remembered. Older ones fall off the back. 6 was a
        /// shallow ban against a 13-19 title pool — and invented titles now consume slots too,
        /// so the list needs the headroom.</summary>
        public const int MaxTracked = 12;

        /// <summary>How long a suggestion stays "recent".</summary>
        public static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

        private readonly object _lock = new();
        private readonly List<(string Title, DateTime Utc)> _entries = new();
        private readonly Func<DateTime> _clock;

        public RecentRecommendations(Func<DateTime>? utcClock = null)
        {
            _clock = utcClock ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Records a title the companion just suggested. Case-insensitively deduped: re-suggesting an
        /// already-tracked title refreshes its timestamp and moves it to the front rather than
        /// consuming a second slot (otherwise one repeated title could evict the whole ban list).
        /// Blank titles are ignored.
        /// </summary>
        public void Note(string? title)
        {
            var clean = (title ?? string.Empty).Trim();
            if (clean.Length == 0) return;

            lock (_lock)
            {
                _entries.RemoveAll(e => string.Equals(e.Title, clean, StringComparison.OrdinalIgnoreCase));
                _entries.Insert(0, (clean, _clock()));
                while (_entries.Count > MaxTracked) _entries.RemoveAt(_entries.Count - 1);
            }
        }

        /// <summary>Currently-excluded titles, newest first. Prunes anything past the TTL.</summary>
        public IReadOnlyList<string> Current()
        {
            var now = _clock();
            lock (_lock)
            {
                _entries.RemoveAll(e => now - e.Utc >= Ttl);
                return _entries.Select(e => e.Title).ToList();
            }
        }

        /// <summary>
        /// The one-line dynamic-tail constraint, or null when there is nothing to exclude (so the
        /// caller emits no empty line and the tail stays as small as possible).
        /// </summary>
        /// <param name="listedTitles">The video titles the prompt lists (the user's pool). When
        /// every one of them is banned, the one suggested longest ago is allowed again and the line
        /// says so. Without that, a small pool (one video left) ended with nothing the model was
        /// allowed to name, and it reached for an off-pool built-in title instead (ccp-bugs #1757).</param>
        public string? BuildExclusionLine(IReadOnlyCollection<string>? listedTitles = null)
        {
            var titles = Current().ToList();
            if (titles.Count == 0) return null;

            string? freed = null;
            if (listedTitles != null && listedTitles.Count > 0)
            {
                var listed = new HashSet<string>(
                    listedTitles.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                if (listed.Count > 0 && listed.All(t => titles.Contains(t, StringComparer.OrdinalIgnoreCase)))
                {
                    // Newest first, so the last listed title in the ban is the oldest pick.
                    freed = titles.Last(t => listed.Contains(t));
                    titles.RemoveAll(t => string.Equals(t, freed, StringComparison.OrdinalIgnoreCase));
                }
            }

            var line = titles.Count > 0
                ? "Already suggested recently (pick something else): " + string.Join(", ", titles)
                : null;
            if (freed == null) return line;

            var again = $"Every video in your list was suggested recently, so \"{freed}\" may be suggested again; " +
                        "never name a video that is not in your list.";
            return line == null ? again : line + ". " + again;
        }

        /// <summary>Drops everything. Part of "forget everything".</summary>
        public void Clear()
        {
            lock (_lock) _entries.Clear();
        }
    }
}
