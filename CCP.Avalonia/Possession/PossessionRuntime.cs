// PORTED (first slice) from ConditioningControlPanel/Services/Possession/PossessionDirector.cs: the
// effect host - the pick through the Core deck (Director :380-400, PossessionDeck.Pick), the CanApply
// gates (PossessionEffectBase.CanApply), hold-then-undo with WPF's 600 ms ease home
// (HoldThenUndoAsync :848), the 45 s target cooldown (PossessionDeck.TargetCooldown) and UndoAll
// (:144, synchronous, no animation). Off-limits controls (Core OffLimits rule, PossessionTree) are
// never offered to the deck and are refused if handed in directly.
//
// Time comes from the window's frame source (FrameClock's IFrameSource; tests step a fake): the hold
// is measured on frame timestamps, never summed deltas (P40). Nothing animates (P82).
//
// Panic: every live runtime registers itself; PanicSurfaces "possession" calls UndoAllLive, which
// returns every touched control's own transform and origin at once.
//
// Still WPF-only (later slices):
//   The shell stage and target registry exist; still needed: the director adapters and Lockdown
//     lifecycle (rungs from the timer, cadence NextDelay/FirstDelay, intensity/photosafe settings,
//     scenes), EmberAttribution charge + Ghost, PossessionEvents tripwires, PossessionPointer, Warden,
//     PossessionAudio, PossessionRemember, bark naming of IsBig effects.
//   possession-head-c: the remaining effects - crack, delete dialog, dissolve, dodge, doki dialog,
//     drop, fall, ghost cursor, glitch portrait, glyphrot, melt, misroute, relabel, reorder doors,
//     retitle, rewrite, room warp, scroll hijack, slider creep, steal card, swap, toast, toggle lie,
//     typo, xp drain (opacity effects clamp to [0,1], P82).

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession
{
    /// <summary>A control the shell offers to the haunt (WPF PossessionTarget, head-side).</summary>
    internal sealed record PossessionHeadTarget(Control Element, PossessionRole Role, string Key);

    internal sealed class PossessionRuntime
    {
        /// <summary>WPF HoldThenUndoAsync hands every effect 600 ms; Nudge/Drift/Breathe/Wobble clamp
        /// it to 600 (UndoMs 220-600 / 250-700) and ease home on CubicEase InOut.</summary>
        internal const double ReturnMs = 600;

        private static readonly List<WeakReference<PossessionRuntime>> Live = new();

        private sealed class Ghost
        {
            public required HeadEffect Effect;
            public required PossessionLease Lease;
            public required PossessionHeadTarget Target;
            public TimeSpan? Start;
            public double Tx, Ty, Rot, Sx = 1, Sy = 1;   // snapshot when the return begins
            public bool Returning;
        }

        private readonly IFrameSource _frames;
        private readonly TimeProvider _time;
        private readonly Random _rng;
        private readonly List<Ghost> _ghosts = new();
        private readonly Dictionary<string, long> _cooldownUntil = new();
        private readonly Action<TimeSpan> _onFrame;
        private bool _subscribed;
        private string? _lastKey;

        public PossessionRuntime(IFrameSource frames, TimeProvider? time = null, Random? rng = null)
        {
            _frames = frames;
            _time = time ?? TimeProvider.System;
            _rng = rng ?? new Random();
            _onFrame = OnFrame;
            lock (Live)
            {
                Live.RemoveAll(w => !w.TryGetTarget(out _));   // prune dead runtimes, not only on panic
                Live.Add(new WeakReference<PossessionRuntime>(this));
            }
        }

        public bool Photosafe { get; set; }
        public int LiveCount => _ghosts.Count;

        /// <summary>The ported effects, one instance each (WPF PossessionEffectCatalog.CreateAll).</summary>
        public static HeadEffect[] CreateAll() =>
            new HeadEffect[] { new NudgeHeadEffect(), new DriftHeadEffect(), new BreatheHeadEffect(), new WobbleHeadEffect() };

        /// <summary>WPF CanApply + Apply: refuses an off-limits, hidden, zero-size, wrong-role or already
        /// possessed control, and an effect instance that is already live.</summary>
        public bool TryApply(HeadEffect effect, PossessionHeadTarget target)
        {
            var el = target.Element;
            if (PossessionTree.IsOffLimits(el)) return false;
            if (!el.IsEffectivelyVisible || el.Bounds.Width <= 0 || el.Bounds.Height <= 0) return false;
            if (!Contains(effect.Roles, target.Role)) return false;
            foreach (var g in _ghosts)
                if (ReferenceEquals(g.Effect, effect) || ReferenceEquals(g.Target.Element, el)) return false;
            var lease = PossessionLease.Take(el);
            if (lease == null) return false;
            effect.Begin(lease, _rng, Photosafe);
            _ghosts.Add(new Ghost { Effect = effect, Lease = lease, Target = target });
            _lastKey = target.Key;
            if (!_subscribed) { _subscribed = true; _frames.Subscribe(_onFrame); }
            return true;
        }

        /// <summary>One haunt: the Core deck picks effect + victim from what may run now.</summary>
        public bool PickAndApply(IReadOnlyList<HeadEffect> effects, IReadOnlyList<PossessionHeadTarget> targets,
            PossessionRung rung, PossessionIntensity intensity)
        {
            long now = _time.GetTimestamp();
            int liveSlots = _ghosts.Count;
            if (!PossessionDeck.FitsConcurrency(liveSlots, 1, rung)) return false;
            var offered = new List<PossessionHeadTarget>();
            var metas = new List<PossessionTargetMeta>();
            foreach (var t in targets)
            {
                if (!t.Element.IsEffectivelyVisible || PossessionTree.IsOffLimits(t.Element)) continue;   // never offered
                bool live = _ghosts.Exists(g => ReferenceEquals(g.Target.Element, t.Element));
                bool cool = _cooldownUntil.TryGetValue(t.Key, out var until) && until > now;
                offered.Add(t);
                metas.Add(new PossessionTargetMeta(t.Key, t.Role, live, cool));
            }
            var effectMetas = new List<PossessionEffectMeta>(effects.Count);
            foreach (var e in effects) effectMetas.Add(e.Meta);
            var pick = PossessionDeck.Pick(effectMetas, metas, rung, intensity, Photosafe, _lastKey, _rng);
            if (pick is not { } p || p.TargetIndex < 0) return false;
            return TryApply(effects[p.EffectIndex], offered[p.TargetIndex]);
        }

        private void OnFrame(TimeSpan now)
        {
            foreach (var g in _ghosts.ToArray())
            {
                g.Start ??= now;
                double ms = (now - g.Start.Value).TotalMilliseconds;
                double hold = g.Effect.HoldFor.TotalMilliseconds;
                if (ms < hold) { g.Effect.Frame(ms); continue; }
                var l = g.Lease;
                if (!g.Returning)
                {
                    g.Returning = true;
                    g.Tx = l.Translate.X; g.Ty = l.Translate.Y; g.Rot = l.Rotate.Angle;
                    g.Sx = l.Scale.ScaleX; g.Sy = l.Scale.ScaleY;
                }
                double f = Math.Min(1, (ms - hold) / ReturnMs);
                if (f >= 1) { Finish(g); continue; }
                double k = 1 - HeadEffect.CubicInOut(f);
                l.Translate.X = g.Tx * k; l.Translate.Y = g.Ty * k; l.Rotate.Angle = g.Rot * k;
                l.Scale.ScaleX = 1 + (g.Sx - 1) * k; l.Scale.ScaleY = 1 + (g.Sy - 1) * k;
            }
            if (_ghosts.Count == 0) Unsubscribe();
        }

        private void Finish(Ghost g)
        {
            _ghosts.Remove(g);
            try { g.Lease.ReleaseImmediate(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession {Id} release failed", g.Effect.Id); }
            _cooldownUntil[g.Target.Key] = _time.GetTimestamp() + (long)(PossessionDeck.TargetCooldown.TotalSeconds * _time.TimestampFrequency);
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;
            _frames.Unsubscribe(_onFrame);
        }

        /// <summary>WPF UndoAll: synchronous, no animation, never throws. Every control gets its own
        /// transform and origin back now.</summary>
        public void UndoAll()
        {
            foreach (var g in _ghosts.ToArray()) Finish(g);
            Unsubscribe();
        }

        /// <summary>Panic (PanicSurfaces "possession"): UndoAll on every runtime still alive.</summary>
        internal static void UndoAllLive()
        {
            List<PossessionRuntime> alive = new();
            lock (Live)
            {
                Live.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (var w in Live) if (w.TryGetTarget(out var r)) alive.Add(r);
            }
            foreach (var r in alive)
            {
                try { r.UndoAll(); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Possession panic undo failed"); }
            }
        }

        private static bool Contains(IReadOnlyList<PossessionRole> roles, PossessionRole role)
        {
            for (int i = 0; i < roles.Count; i++) if (roles[i] == role) return true;
            return false;
        }
    }
}
