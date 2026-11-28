using System;
using System.Collections.Generic;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services
{
    /// <summary>What the stage and the cursor do to one Froth bubble this step (WPF BubbleService.Froth.cs:149-162):
    /// acceleration in the bubble's DIPs per second squared, the pool's shrink, and whether the eye takes it.</summary>
    public readonly record struct FrothPush(double Ax, double Ay, double PoolScale, bool Inside, bool Eaten)
    {
        public static readonly FrothPush None = new(0, 0, 1, false, false);
    }

    // WPF BubbleService.Froth.cs:280-453. Neutral until the Avalonia host enables Froth.
    public sealed partial class AmbientBubble
    {
        private double _frothGrow = 1, _frothShown = 1, _frothRim;
        private double _frothVx, _frothVy, _frothDrift = 1, _frothPool = 1, _frothWob, _frothT, _frothEdgePad;
        internal double FrothFuse = -1;
        internal List<AmbientBubble>? FrothParts;
        internal bool FrothPartsPaid;
        public double FrothShown => _frothShown;
        public double FrothRim => _frothRim;
        /// <summary>The size multiple drawn: the merges, eased, times the Whirlpool's shrink (WPF SyncFrothItem).</summary>
        public double FrothScale => _frothShown * _frothPool;
        /// <summary>Share of its own drift left; the pool damps it so a bubble spirals in (WPF _frothDrift).</summary>
        public double FrothDrift => _frothDrift;
        /// <summary>The merge wobble: drawn this wide and 2 minus this tall (<see cref="FrothRules.Squash"/>).</summary>
        public double FrothSquash(bool gentle) => FrothRules.Squash(_frothWob, _frothT, gentle);
        public double FrothRadius => Size * FrothRules.GlassOfSprite * _frothGrow;
        public int FrothPartCount => 1 + (FrothParts?.Count ?? 0);
        internal bool FrothLive => !Popping && Size > 0 && (Spec == null || (AmbientTrigger && Spec.VariantId == "flash"));

        internal void FrothAbsorb(AmbientBubble other, double fresh, double cap)
        {
            double ar = FrothRadius, br = other.FrothRadius;
            var c = FrothRules.MergedCentre(CenterX, CenterY, ar, other.CenterX, other.CenterY, br);
            double dx = c.X - CenterX, dy = c.Y - CenterY;
            X += dx; Y += dy; _startX += dx; _startY += dy; _spiralCx += dx; _spiralCy += dy;
            double wa = ar * ar, wb = br * br, w = wa + wb;
            if (w > 0)
            {
                _frothVx = (_frothVx * wa + other._frothVx * wb) / w;
                _frothVy = (_frothVy * wa + other._frothVy * wb) / w;
            }
            _frothGrow = Math.Max(_frothGrow, FrothRules.MergedRadius(ar, br, fresh, cap) / (Size * FrothRules.GlassOfSprite));
            _frothWob = 1;
            // WPF :394-397: a grown bubble leaves by the top or bottom only once the whole of it is past the edge.
            double pad = Size * (_frothGrow - 1) / 2.0;
            _top -= pad - _frothEdgePad;
            _bottom += pad - _frothEdgePad;
            _frothEdgePad = pad;
            FrothParts ??= new List<AmbientBubble>();
            FrothParts.Add(other);
            if (other.FrothParts != null) { FrothParts.AddRange(other.FrothParts); other.FrothParts = null; }
        }

        /// <summary>WPF Bubble.FrothStep (:346-373): the pull and the push, the pool's shrink and drag,
        /// the merge wobble, the size easing and the gold rim.</summary>
        internal void FrothStep(double dt, FrothPush f, double fresh, bool gentle)
        {
            _frothT += dt;
            _frothVx += f.Ax * dt;
            _frothVy += f.Ay * dt;
            double drag = Math.Max(0, 1 - dt * (f.Inside ? FrothRules.PoolDrag : FrothRules.Drag));
            _frothVx *= drag;
            _frothVy *= drag;
            _frothDrift = f.Inside
                ? _frothDrift * Math.Max(0, 1 - dt * FrothRules.PoolDrag)
                : Math.Min(1.0, _frothDrift + dt * FrothRules.DriftBack);
            double dx = _frothVx * dt, dy = _frothVy * dt;
            // Never pushed off the side of its own screen (the base rows only leave by their own edge).
            if (_right > _left && ((X + dx < _left && dx < 0) || (X + dx > _right && dx > 0))) { dx = 0; _frothVx = 0; }
            if (dx != 0 || dy != 0) { X += dx; _startX += dx; _spiralCx += dx; Y += dy; _startY += dy; _spiralCy += dy; }
            _frothPool = Math.Clamp(f.PoolScale, 0.05, 1.0);
            _frothShown = FrothRules.EaseSize(_frothShown, _frothGrow, dt, gentle);
            if (Math.Abs(_frothGrow - _frothShown) < 0.002) _frothShown = _frothGrow;
            if (_frothWob > 0) _frothWob = Math.Max(0, _frothWob - dt / FrothRules.WobbleSeconds);
            double target = FrothRules.Ripe(FrothRadius, fresh) ? 1 : 0;
            _frothRim += Math.Clamp(target - _frothRim, -dt / .4, dt / .4);
        }

        internal void FrothReset()
        {
            _frothGrow = _frothShown = _frothPool = _frothDrift = 1;
            _frothRim = _frothVx = _frothVy = _frothWob = 0;
            FrothFuse = -1;
        }
    }

    // WPF BubbleService.Froth.cs:73-181, 248-266. Only the Avalonia host calls these methods.
    public sealed partial class AmbientBubbleField
    {
        private bool _frothOn;
        /// <param name="force">The head's read of the stage and the cursor for one bubble (<see cref="FrothForce"/>);
        /// null = nothing pulls or pushes. An eaten bubble pops through <paramref name="pop"/> and pays normally
        /// (WPF :152, owner rule).</param>
        public void StepFroth(bool enabled, double dt, double fresh, double maxSize, Action<AmbientBubble> pop,
            Func<AmbientBubble, FrothPush>? force = null)
        {
            if (!enabled)
            {
                if (_frothOn)
                {
                    Bubbles.RemoveAll(b => b.FrothPartCount > 1);
                    foreach (var b in Bubbles) b.FrothReset();
                    _frothOn = false;
                }
                return;
            }
            _frothOn = true;
            dt = Math.Clamp(dt, 0, .1);
            if (dt <= 0 || fresh <= 0) return;
            foreach (var b in Bubbles.ToArray())
            {
                if (!b.FrothLive || !Bubbles.Contains(b)) continue;
                if (b.FrothFuse >= 0)
                {
                    b.FrothFuse -= dt;
                    if (b.FrothFuse <= 0) { b.FrothFuse = -1; pop(b); }
                    continue;
                }
                var f = force?.Invoke(b) ?? FrothPush.None;
                if (f.Eaten) { pop(b); continue; }
                b.FrothStep(dt, f, fresh, Safety.GentleVisuals.On);
            }
            for (int i = 0; i < Bubbles.Count; i++)
            {
                var a = Bubbles[i];
                if (!a.FrothLive || a.FrothFuse >= 0) continue;
                for (int j = i + 1; j < Bubbles.Count; j++)
                {
                    var b = Bubbles[j];
                    if (!b.FrothLive || b.FrothFuse >= 0 || a.Screen != b.Screen) continue;
                    if (!FrothRules.Touching(a.CenterX, a.CenterY, a.FrothRadius, b.CenterX, b.CenterY, b.FrothRadius)) continue;
                    if (a.FrothRadius >= b.FrothRadius) { a.FrothAbsorb(b, fresh, FrothOptions.ClampMaxSize(maxSize)); Bubbles.RemoveAt(j--); }
                    else { b.FrothAbsorb(a, fresh, FrothOptions.ClampMaxSize(maxSize)); Bubbles.RemoveAt(i--); break; }
                }
            }
        }

        public void FrothBlast(int screen, double x, double y, double reach, double fresh)
        {
            if (!_frothOn || reach <= 0) return;
            foreach (var b in Bubbles)
            {
                if (!b.FrothLive || b.FrothFuse >= 0 || b.Screen != screen) continue;
                double dx = b.CenterX - x, dy = b.CenterY - y, distance = Math.Sqrt(dx * dx + dy * dy);
                if (FrothRules.InReach(distance, reach, b.FrothRadius)) b.FrothFuse = FrothRules.ChainDelay(distance, fresh);
            }
        }
        /// <summary>WPF BubbleService.Froth.cs:149-162 for one bubble. The stage speaks device pixels and a
        /// bubble DIPs of its own screen (<paramref name="scale"/> px per DIP). The cursor herds bubbles only
        /// while the pool is not using them; Cursor push (<paramref name="pushGain"/>) never touches pay.</summary>
        public static FrothPush FrothForce(SuperStage stage, AmbientBubble b, double scale,
            (double X, double Y)? cursorPx, double fresh, double pushGain)
        {
            scale = scale > 0 ? scale : 1;
            double cx = b.CenterX * scale, cy = b.CenterY * scale, r = b.FrothRadius;
            var pull = stage.Pull(cx, cy, FrothRules.Mass(r, fresh), PullKind.Bubble);
            if (pull.Eaten) return new FrothPush(0, 0, pull.Scale, pull.Inside, true);
            double ax = pull.Ax / scale, ay = pull.Ay / scale;
            bool poolLive = stage.PoolLive && stage.Drags(PullKind.Bubble);
            if (!poolLive && cursorPx is { } c && pushGain > 0)
            {
                double dx = (cx - c.X) / scale, dy = (cy - c.Y) / scale, dd = Math.Sqrt(dx * dx + dy * dy);
                double push = FrothRules.NudgeAccel(dd - r, r, fresh) * pushGain;
                if (push > 0 && dd > 1e-6) { ax += dx / dd * push; ay += dy / dd * push; }
            }
            return new FrothPush(ax, ay, pull.Scale, pull.Inside, false);
        }

        /// <summary>WPF OnStageSwept (:279-294): the bouncing word covers this box (device px) and the Froth
        /// bubbles under it pop. The stage raises it only while "Moving effects pop" is on.</summary>
        public void FrothSweep(Func<AmbientBubble, double> scaleOf, double left, double top, double right, double bottom,
            Action<AmbientBubble> pop)
        {
            if (!_frothOn) return;
            foreach (var b in Bubbles.ToArray())
            {
                if (!b.FrothLive || !Bubbles.Contains(b)) continue;
                double s = scaleOf(b);
                if (FrothRules.UnderBox(b.CenterX * s, b.CenterY * s, b.FrothRadius * s, left, top, right, bottom)) pop(b);
            }
        }

        public IReadOnlyList<AmbientBubble> FrothTakeParts(AmbientBubble b)
        {
            if (b.FrothPartsPaid || b.FrothParts == null) return Array.Empty<AmbientBubble>();
            b.FrothPartsPaid = true;
            return b.FrothParts;
        }
    }
}
