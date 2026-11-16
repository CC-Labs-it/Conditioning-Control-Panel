using System;
using System.Collections.Generic;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services
{
    // WPF BubbleService.Froth.cs:280-453. Neutral until the Avalonia host enables Froth.
    public sealed partial class AmbientBubble
    {
        private double _frothGrow = 1, _frothShown = 1, _frothRim, _frothVx, _frothVy, _frothPool = 1;
        internal double FrothFuse = -1;
        internal List<AmbientBubble>? FrothParts;
        internal bool FrothPartsPaid;
        public double FrothShown => _frothShown * _frothPool;
        public double FrothRim => _frothRim;
        public double FrothRadius => Size * FrothRules.GlassOfSprite * _frothGrow;
        public int FrothPartCount => 1 + (FrothParts?.Count ?? 0);
        internal bool FrothLive => !Popping && Size > 0 && (Spec == null || (AmbientTrigger && Spec.VariantId == "flash"));

        internal void FrothAbsorb(AmbientBubble other, double fresh, double cap)
        {
            double ar = FrothRadius, br = other.FrothRadius;
            var c = FrothRules.MergedCentre(CenterX, CenterY, ar, other.CenterX, other.CenterY, br);
            double dx = c.X - CenterX, dy = c.Y - CenterY;
            X += dx; Y += dy; _startX += dx; _startY += dy; _spiralCx += dx; _spiralCy += dy;
            _frothGrow = Math.Max(_frothGrow, FrothRules.MergedRadius(ar, br, fresh, cap) / (Size * FrothRules.GlassOfSprite));
            FrothParts ??= new List<AmbientBubble>();
            FrothParts.Add(other);
            if (other.FrothParts != null) { FrothParts.AddRange(other.FrothParts); other.FrothParts = null; }
        }

        internal void FrothAnimate(double dt, double fresh, bool gentle)
        {
            _frothShown = FrothRules.EaseSize(_frothShown, _frothGrow, dt, gentle);
            double target = FrothRules.Ripe(FrothRadius, fresh) ? 1 : 0;
            _frothRim += Math.Clamp(target - _frothRim, -dt / .4, dt / .4);
        }
        // WPF BubbleService.Froth.cs:336-361: keep the base motion anchors with the pushed bubble.
        public void FrothMove(double dt, double ax, double ay, double poolScale, bool insidePool)
        {
            if (!FrothLive) return;
            dt = Math.Clamp(dt, 0, .1);
            _frothVx += ax * dt; _frothVy += ay * dt;
            double drag = Math.Max(0, 1 - dt * (insidePool ? FrothRules.PoolDrag : FrothRules.Drag));
            _frothVx *= drag; _frothVy *= drag;
            double dx = _frothVx * dt, dy = _frothVy * dt;
            X += dx; Y += dy; _startX += dx; _startY += dy; _spiralCx += dx; _spiralCy += dy;
            _frothPool = Math.Clamp(poolScale, .05, 1);
        }

        internal void FrothReset() { _frothGrow = _frothShown = _frothPool = 1; _frothRim = _frothVx = _frothVy = 0; FrothFuse = -1; }
    }

    // WPF BubbleService.Froth.cs:73-181, 248-266. Only the Avalonia host calls these methods.
    public sealed partial class AmbientBubbleField
    {
        private bool _frothOn;
        public void StepFroth(bool enabled, double dt, double fresh, double maxSize, Action<AmbientBubble> pop)
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
                b.FrothAnimate(dt, fresh, Safety.GentleVisuals.On);
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
        public IReadOnlyList<AmbientBubble> FrothTakeParts(AmbientBubble b)
        {
            if (b.FrothPartsPaid || b.FrothParts == null) return Array.Empty<AmbientBubble>();
            b.FrothPartsPaid = true;
            return b.FrothParts;
        }
    }
}
