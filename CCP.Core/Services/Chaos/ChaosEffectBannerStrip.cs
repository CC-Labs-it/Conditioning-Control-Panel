using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>
    /// The state behind the Chaos effect-banner strip (WPF ConditioningControlPanel/Chaos/
    /// ChaosEffectBannerOverlay.cs): which labels are on screen, in what order, and how far each
    /// one is through its fade-in, throb and fade-out at a given instant. The heads only draw it.
    ///
    /// <para>WPF rules kept: one live label per effect id (AddEntry :119 "already on screen - let
    /// it ride"); labels sit left to right in the order they arrived (StackPanel append :164);
    /// End drops the id at once (so a re-Show while it fades adds a NEW label beside it, :176)
    /// and fades from the label's current opacity (:177); the faded label leaves the row when
    /// its 380 ms fade finishes (:180). Times come from the caller (P08), never a timer.</para>
    /// </summary>
    public sealed class ChaosEffectBannerStrip
    {
        public const double FadeInMs = 200;     // WPF FADE_IN_MS
        public const double FadeOutMs = 380;    // WPF FADE_OUT_MS
        public const double PulseMs = 850;      // WPF throb DoubleAnimation, AutoReverse, forever
        public const double PulsePeak = 1.03;   // 1.0 -> 1.03, SineEase EaseInOut

        public sealed class Entry
        {
            internal Entry(string id, string text, string? artKey, DateTimeOffset shownAt)
            { Id = id; Text = text; ArtKey = artKey; ShownAt = shownAt; }

            public string Id { get; }
            public string Text { get; }
            /// <summary>The announce art name, <c>artKey ?? id</c> (WPF :125).</summary>
            public string? ArtKey { get; }
            public DateTimeOffset ShownAt { get; }
            public DateTimeOffset? EndedAt { get; private set; }
            /// <summary>Opacity at <see cref="EndedAt"/>: the fade-out's start value.</summary>
            public double OpacityAtEnd { get; private set; }
            public bool IsLive => EndedAt == null;

            internal void End(DateTimeOffset now) { OpacityAtEnd = Opacity(now); EndedAt = now; }

            public double Opacity(DateTimeOffset now)
            {
                if (EndedAt is { } end)
                    return OpacityAtEnd * (1 - Math.Clamp((now - end).TotalMilliseconds / FadeOutMs, 0, 1));
                return Math.Clamp((now - ShownAt).TotalMilliseconds / FadeInMs, 0, 1);
            }

            public double Scale(DateTimeOffset now)
            {
                double ms = Math.Max(0, (now - ShownAt).TotalMilliseconds) % (2 * PulseMs);
                double t = ms <= PulseMs ? ms / PulseMs : 2 - ms / PulseMs;
                return 1 + (PulsePeak - 1) * (1 - Math.Cos(Math.PI * t)) / 2;
            }

            internal bool Gone(DateTimeOffset now) => EndedAt is { } end && (now - end).TotalMilliseconds >= FadeOutMs;
        }

        private readonly List<Entry> _row = new();

        /// <summary>Every label on screen, live or fading, left to right.</summary>
        public IReadOnlyList<Entry> Row => _row;

        public bool IsLive(string id) => FindLive(id) != null;

        /// <summary>WPF AddEntry: a new label at the right end, or null when the id is already live.</summary>
        public Entry? Show(string id, string text, string? artKey, DateTimeOffset now)
        {
            if (IsLive(id)) return null;
            var e = new Entry(id, text, artKey ?? id, now);
            _row.Add(e);
            return e;
        }

        /// <summary>WPF FadeEntry: starts the fade, or null when the id is not live.</summary>
        public Entry? End(string id, DateTimeOffset now)
        {
            var e = FindLive(id);
            e?.End(now);
            return e;
        }

        /// <summary>Drops labels whose fade-out finished; returns them so the head can remove their controls.</summary>
        public List<Entry> Prune(DateTimeOffset now)
        {
            var gone = _row.FindAll(e => e.Gone(now));
            foreach (var e in gone) _row.Remove(e);
            return gone;
        }

        /// <summary>WPF CloseNow: run teardown.</summary>
        public void Clear() => _row.Clear();

        private Entry? FindLive(string id) => _row.Find(e => e.IsLive && e.Id == id);
    }
}
