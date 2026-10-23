using System;
using System.Collections.Generic;

namespace ConditioningControlPanel
{
    /// <summary>What a Chaos announcement is about — drives the accent palette.</summary>
    public enum ChaosAnnounceKind { Mantra, Temptation, Willpower, Depth, Streak, Item, PowerUp, Narrator }
}

namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>One queued announcement (WPF ChaosAnnouncerOverlay's queue tuple).</summary>
    public sealed record ChaosAnnouncement(string Text, ChaosAnnounceKind Kind, string? ArtKey, string? SubText, int HoldMs, int Priority);

    /// <summary>
    /// The announcer's queue, shared by both heads (WPF ChaosAnnouncerOverlay.cs TryDequeue). Higher
    /// priority shows first; ties keep insertion order. Gameplay announces enqueue at 0, the narrator
    /// (the Madam) at <see cref="NarratorPriority"/>, so STORY &gt; REACTIVE &gt; gameplay.
    /// </summary>
    public sealed class ChaosAnnounceQueue
    {
        private readonly List<ChaosAnnouncement> _items = new();

        public int Count => _items.Count;

        public static int NarratorPriority(int bandPriority) => 100 + bandPriority;

        public void Add(ChaosAnnouncement a) => _items.Add(a);

        public void Clear() => _items.Clear();

        public bool TryDequeue(out ChaosAnnouncement item)
        {
            item = null!;
            if (_items.Count == 0) return false;
            int best = 0;
            for (int i = 1; i < _items.Count; i++)
                if (_items[i].Priority > _items[best].Priority) best = i;   // stable: first of the max wins
            item = _items[best];
            _items.RemoveAt(best);
            return true;
        }
    }

    /// <summary>
    /// The announcer's envelope (WPF ChaosAnnouncerOverlay.DisplayCore and its _life timer): opacity
    /// 0→1 linear over <see cref="InMs"/>, a 0.85→1 scale pop over <see cref="PopMs"/> with BackEase
    /// (amplitude 0.6, EaseOut), a hold of IN + hold, then a linear fade to 0 over <see cref="OutMs"/>.
    /// Pure functions of elapsed milliseconds so a head can drive them from a stepped clock.
    /// </summary>
    public static class ChaosAnnounceTiming
    {
        public const int InMs = 110;            // scale-pop + fade-in
        public const int HoldMs = 650;          // default dwell (event beats)
        public const int TeachHoldMs = 3000;    // onboarding/help lines linger so they can be read
        public const int OutMs = 240;           // fade-out
        public const int PopMs = InMs + 70;
        public const double PopFrom = 0.85;
        public const double BackAmplitude = 0.6;

        /// <summary>When the fade-out starts: WPF _life.Interval = IN_MS + max(0, holdMs).</summary>
        public static int LifeMs(int holdMs) => InMs + Math.Max(0, holdMs);

        public static int TotalMs(int holdMs) => LifeMs(holdMs) + OutMs;

        public static double Opacity(double elapsedMs, int holdMs)
        {
            if (elapsedMs <= 0) return 0;
            if (elapsedMs < InMs) return elapsedMs / InMs;
            int life = LifeMs(holdMs);
            if (elapsedMs < life) return 1;
            return Math.Max(0, 1 - (elapsedMs - life) / OutMs);
        }

        public static double Scale(double elapsedMs)
        {
            double t = Math.Clamp(elapsedMs / PopMs, 0, 1);
            return PopFrom + (1 - PopFrom) * BackEaseOut(t, BackAmplitude);
        }

        public static bool IsDone(double elapsedMs, int holdMs) => elapsedMs >= TotalMs(holdMs);

        /// <summary>WPF BackEase: EaseIn f(t) = t³ − t·a·sin(πt); EaseOut = 1 − f(1 − t).</summary>
        public static double BackEaseOut(double t, double amplitude)
        {
            double u = 1 - t;
            return 1 - (u * u * u - u * Math.Max(0, amplitude) * Math.Sin(Math.PI * u));
        }
    }
}
