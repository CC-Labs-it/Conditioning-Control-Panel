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
// Text effects (restack b165, #3794 #3922 #4041): Typo, Rewrite and GlyphRot (WPF TypoEffect.cs, RewriteEffect.cs,
// GlyphRotEffect.cs) take no transform lease; their text returns at once in Finish (hold end, Return, panic),
// and only while the control still shows exactly what the effect wrote.
//
// Panic: every live runtime registers itself; PanicSurfaces "possession" calls UndoAllLive, which
// returns every touched control's own transform and origin at once.
//
// Still WPF-only (later slices):
//   possession-head-b: the shell host (MainWindow.Possession.cs walk: Possession.Role opt-in,
//     InferLeafRole, size/placement filter, display names, the two overlay canvases), the Lockdown
//     lifecycle (rungs from the timer, cadence NextDelay/FirstDelay, intensity/photosafe settings,
//     scenes), EmberAttribution charge + Ghost, PossessionEvents tripwires, Warden,
//     PossessionAudio, PossessionRemember, bark naming of IsBig effects.
//   possession-head-c: the remaining effects - crack, dissolve, fall, ghost cursor, glitch portrait,
//     melt, misroute, relabel, reorder doors, room warp, steal card, swap, toast, toggle lie,
//     xp drain (opacity effects clamp to [0,1], P82).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
            /// <summary>Null for a text effect: nothing to ease home, the text returns in Finish.</summary>
            public PossessionLease? Lease;
            public required PossessionHeadTarget Target;
            public TimeSpan? Start;
            public double Tx, Ty, Rot, Sx = 1, Sy = 1;   // snapshot when the return begins
            public bool Returning;
            /// <summary>Hold before the ease home. Directed ghosts (the Core director owns the hold)
            /// hold until <see cref="Return"/> asks for the way home.</summary>
            public double HoldMs;
            public double ReturnMs = PossessionRuntime.ReturnMs;
            public bool ReturnAsked;
            public readonly TaskCompletionSource Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            new HeadEffect[] { new NudgeHeadEffect(), new DriftHeadEffect(), new BreatheHeadEffect(), new WobbleHeadEffect(),
                new TypoHeadEffect(), new RewriteHeadEffect(), new GlyphRotHeadEffect() };

        /// <summary>WPF CanApply + Apply: refuses an off-limits, hidden, zero-size, wrong-role or already
        /// possessed control, and an effect instance that is already live.</summary>
        public bool TryApply(HeadEffect effect, PossessionHeadTarget target) => TryApply(effect, target, directed: false);

        /// <summary>As <see cref="TryApply(HeadEffect, PossessionHeadTarget)"/>; a <paramref name="directed"/>
        /// ghost is held until <see cref="Return"/> (the Core PossessionDirector owns the hold, the
        /// 45 s cooldown and the ledger, exactly as WPF HoldThenUndoAsync does).</summary>
        public bool TryApply(HeadEffect effect, PossessionHeadTarget target, bool directed)
        {
            var el = target.Element;
            if (PossessionTree.IsOffLimits(el, effect is WobbleHeadEffect && target.Role == PossessionRole.Timer && el is TextBlock && el.Name == "TxtLockdownTimer")) return false;
            if (!el.IsEffectivelyVisible || el.Bounds.Width <= 0 || el.Bounds.Height <= 0) return false;
            if (!Contains(effect.Roles, target.Role)) return false;
            foreach (var g in _ghosts)
                if (ReferenceEquals(g.Effect, effect) || ReferenceEquals(g.Target.Element, el)) return false;
            if (!effect.CanApply(el)) return false;
            // Text effects (typo, rewrite, glyph rot) leave the control's transform alone (no lease).
            var lease = effect.UsesTransform ? PossessionLease.Take(el) : null;
            if (effect.UsesTransform && lease == null) return false;
            effect.Begin(el, lease, _rng, Photosafe);
            _ghosts.Add(new Ghost
            {
                Effect = effect, Lease = lease, Target = target,
                HoldMs = directed ? double.PositiveInfinity : effect.HoldFor.TotalMilliseconds,
                ReturnMs = Math.Min(ReturnMs, effect.UndoMs),
            });
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
                if (!t.Element.IsEffectivelyVisible || PossessionTree.IsOffLimits(t.Element, t.Role == PossessionRole.Timer && t.Element is TextBlock && t.Element.Name == "TxtLockdownTimer")) continue;   // never offered
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
                if (g.ReturnAsked && !g.Returning) g.HoldMs = Math.Min(g.HoldMs, ms);   // home starts now
                double hold = g.HoldMs;
                if (ms < hold) { g.Effect.Frame(ms); continue; }
                // WPF TypoEffect.UndoCoreAsync ignores duration: text returns at the hold boundary.
                if (g.ReturnMs <= 0 || g.Lease is not { } l) { Finish(g); continue; }
                if (!g.Returning)
                {
                    g.Returning = true;
                    g.Tx = l.Translate.X; g.Ty = l.Translate.Y; g.Rot = l.Rotate.Angle;
                    g.Sx = l.Scale.ScaleX; g.Sy = l.Scale.ScaleY;
                }
                double f = g.ReturnMs <= 0 ? 1 : Math.Min(1, (ms - hold) / g.ReturnMs);
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
            try
            {
                try { g.Effect.Release(); }
                finally { g.Lease?.ReleaseImmediate(); }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession {Id} release failed", g.Effect.Id); }
            _cooldownUntil[g.Target.Key] = _time.GetTimestamp() + (long)(PossessionDeck.TargetCooldown.TotalSeconds * _time.TimestampFrequency);
            g.Done.TrySetResult();
        }

        /// <summary>True while <paramref name="effect"/> holds a control on this runtime.</summary>
        public bool IsRunning(HeadEffect effect) => _ghosts.Exists(g => ReferenceEquals(g.Effect, effect));

        /// <summary>WPF UndoAsync(duration) for a directed ghost: ease home over <paramref name="duration"/>
        /// (WPF's four transform effects clamp it to 600 ms), then release. Zero = home now, synchronously
        /// (UndoAll, crash-safe exits). Completes when the control has its own transform back; a ghost that
        /// is already gone (panic took it) completes at once.</summary>
        public Task Return(HeadEffect effect, TimeSpan duration)
        {
            var g = _ghosts.Find(x => ReferenceEquals(x.Effect, effect));
            if (g == null) return Task.CompletedTask;
            double ms = Math.Min(Math.Min(Math.Max(0, duration.TotalMilliseconds), ReturnMs), effect.UndoMs);
            if (ms <= 0 || g.Start == null)
            {
                Finish(g);
                if (_ghosts.Count == 0) Unsubscribe();
                return Task.CompletedTask;
            }
            g.ReturnAsked = true;
            g.ReturnMs = ms;
            return g.Done.Task;
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
            PossessionGhost.UndoAll();
            DropEffect.UndoAllLive();
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
