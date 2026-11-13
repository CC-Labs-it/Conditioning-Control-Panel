using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// ccp-bugs #1537, #1540 (and #1490, #1491): "random audio stings", and no report could name the
    /// sound. The activity log is cut to its tail on the way to the tracker and webcam lines flood it,
    /// and the engine-off trace (WPF EngineOffAudioLog) sits in the part that gets cut.
    ///
    /// <para>So: the last <see cref="Capacity"/> sounds the app started, in memory, written as the LAST
    /// section of a bug report, where the tail cut cannot reach it. File names only, never a path. Fed
    /// by WPF EngineOffAudioLog.Note, and on Linux by LibVlcAudio.PlayOneShot, when a sound
    /// really starts. It records and reports; it never changes what plays.</para>
    /// </summary>
    public sealed class RecentSounds
    {
        /// <summary>How many entries the ring keeps.</summary>
        public const int Capacity = 30;

        /// <summary>A longer name is cut: the section has to stay short.</summary>
        internal const int MaxNameChars = 80;

        /// <summary>
        /// The same player inside this gap folds into one entry, or a sweep over the launcher's hover
        /// notes would push the one sound worth seeing out of the ring. The gap is short on purpose:
        /// the same clip twice, a while apart, is two entries, and that is the shape of the bug. It sits
        /// just over the faucet's 3.8 s tick, the one repeating cue that would otherwise fill the ring.
        /// </summary>
        internal static readonly TimeSpan FoldGap = TimeSpan.FromSeconds(5);

        internal const string SectionHeader = "===== recent sounds (newest last) =====";
        internal const string NoSoundsLine = "(no sound through a tracked player since launch)";

        public static RecentSounds Shared { get; } = new();

        public sealed record Entry(DateTime At, string Player, string File, bool EngineRunning, bool Asked, int Count);

        private readonly Queue<Entry> _ring = new();
        private Entry? _newest;
        private readonly object _gate = new();

        /// <summary>A sound just started. <paramref name="path"/> is reduced to its file name here.</summary>
        public void Note(string player, string? path, bool engineRunning, bool asked, DateTime now)
        {
            if (string.IsNullOrEmpty(player)) player = "audio";
            var file = CleanName(path);

            lock (_gate)
            {
                if (_newest != null
                    && _newest.Player == player
                    && _newest.EngineRunning == engineRunning
                    && _newest.Asked == asked
                    && now >= _newest.At
                    && now - _newest.At <= FoldGap)
                {
                    // Queue has no way to touch its tail, so the tail is rewritten by rebuilding.
                    var folded = _newest with { At = now, File = file, Count = _newest.Count + 1 };
                    var keep = _ring.Count - 1;
                    for (int i = 0; i < keep; i++) _ring.Enqueue(_ring.Dequeue());
                    _ring.Dequeue();
                    _ring.Enqueue(folded);
                    _newest = folded;
                    return;
                }

                var entry = new Entry(now, player, file, engineRunning, asked, 1);
                _ring.Enqueue(entry);
                _newest = entry;
                while (_ring.Count > Capacity) _ring.Dequeue();
            }
        }

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<Entry> Snapshot()
        {
            lock (_gate) return _ring.ToArray();
        }

        /// <summary>The bug report section for the entries as they stand now.</summary>
        public string BuildSection() => BuildSection(Snapshot());

        /// <summary>
        /// Pure. Written even when empty: "nothing went through a tracked player" is itself the answer
        /// when a report says a sound played, since it points at a path outside the ring.
        /// </summary>
        public static string BuildSection(IReadOnlyList<Entry> entries)
        {
            var sb = new StringBuilder();
            sb.Append(SectionHeader);
            if (entries == null || entries.Count == 0)
            {
                sb.Append(Environment.NewLine).Append(NoSoundsLine);
                return sb.ToString();
            }

            foreach (var e in entries)
            {
                sb.Append(Environment.NewLine)
                  .Append(e.At.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
                  .Append(' ').Append(e.Player)
                  .Append(" '").Append(e.File).Append('\'');
                if (e.Count > 1) sb.Append(" x").Append(e.Count.ToString(CultureInfo.InvariantCulture));
                sb.Append(e.EngineRunning ? " engine-on" : " engine-off");
                if (e.Asked) sb.Append(" asked");
            }
            return sb.ToString();
        }

        /// <summary>WPF EngineOffAudioLog.NameOf: a full path can carry the user name.</summary>
        private static string FileNameOf(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "?";
            try
            {
                var name = System.IO.Path.GetFileName(path.TrimEnd('/', (char)92));
                return string.IsNullOrEmpty(name) ? "?" : name;
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>File name only, no query string, capped.</summary>
        internal static string CleanName(string? path)
        {
            var name = FileNameOf(path);
            var cut = name.IndexOfAny(new[] { '?', '#' });
            if (cut > 0) name = name.Substring(0, cut);
            return name.Length <= MaxNameChars ? name : name.Substring(0, MaxNameChars);
        }

        /// <summary>
        /// Pure: "FlashService.ShowImages" from a caller's source path and member (ccp-bugs #1564).
        /// The type file's name only, never the build machine's path.
        /// </summary>
        public static string DuckCaller(string? callerFile, string? callerMember)
        {
            var file = callerFile ?? "";
            var slash = file.LastIndexOfAny(new[] { '\\', '/' });
            if (slash >= 0) file = file.Substring(slash + 1);
            if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) file = file.Substring(0, file.Length - 3);
            if (string.IsNullOrEmpty(file)) file = "unknown";
            return string.IsNullOrEmpty(callerMember) ? file : file + "." + callerMember;
        }

        /// <summary>Test seam.</summary>
        internal void Clear()
        {
            lock (_gate)
            {
                _ring.Clear();
                _newest = null;
            }
        }
    }
}
