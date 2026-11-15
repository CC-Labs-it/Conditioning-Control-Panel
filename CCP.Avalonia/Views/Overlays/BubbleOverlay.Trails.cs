using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    internal static partial class BubbleOverlay
    {
        // WPF BubbleService.cs:1639, 1764-1780, 3929-3954.
        internal const double TrailPopRadiusPx = 46;
        internal static readonly Dictionary<AmbientBubble, ChaosRabbitTrails> ChaosTrails = new();

        // WPF BubbleService.cs:1901-1915: the grown body mows while the field moves.
        internal static void TickDarterSweeps()
        {
            if (_chaosPopped == null || Field.HoldChaos || Field.FreezeChaos) return;
            var snapshot = Field.Bubbles.ToArray();
            foreach (var rabbit in snapshot)
            {
                if (!rabbit.Spanked || rabbit.Popping || rabbit.Spec?.IsDarter != true) continue;
                double grow = rabbit.Size * (rabbit.SpankGrowth - 1);
                var reach = new Rect(rabbit.X - grow / 2, rabbit.Y - grow / 2, rabbit.Size + grow, rabbit.Size + grow);
                foreach (var b in snapshot)
                {
                    if (b.Popping || b.Spec is not { IsDarter: false, IsFreeze: false }
                        || !Field.Bubbles.Contains(b)) continue;
                    if (!reach.Intersects(new Rect(b.X, b.Y, b.Size, b.Size))) continue;
                    var victimPx = CenterPx(b);
                    if (b.AmbientTrigger) Pop(b); else PopChaos(b);
                    if (_chaosPopped == null) return;
                    if (ChaosRunHost.State?.ElectrifiedRabbits == true) EStimClick(b, free: true, origin: victimPx);
                }
            }
        }

        internal static void TickChaosTrails()
        {
            if (_chaosPopped == null || Field.HoldChaos || Field.FreezeChaos) return;
            double trailSec = Math.Max(0, ChaosRunHost.State?.RabbitTrailSec ?? 0);
            var snapshot = Field.Bubbles.ToArray();
            foreach (var old in ChaosTrails.Keys.Where(b => !snapshot.Contains(b)).ToArray()) ChaosTrails.Remove(old);
            foreach (var rabbit in snapshot)
            {
                if (rabbit.Spec is not { IsDarter: true } spec || rabbit.AmbientTrigger) continue;
                if (!ChaosTrails.TryGetValue(rabbit, out var trail)) ChaosTrails[rabbit] = trail = new();
                var center = CenterPx(rabbit);
                var direction = trail.Step(center.X, center.Y, ScaleFor(rabbit), trailSec,
                    spec.IsSweeper, rabbit.DarterTelegraphing, rabbit.Popping, false, Field.Clock());
                if (direction != null)
                    ChaosFieldFxOverlay.TrailDot(center, spec.IsSweeper ? Math.Max(0.5, trailSec) : trailSec, spec.IsSweeper);
                if (trailSec <= 0) continue;
                foreach (var b in snapshot)
                {
                    if (b.Popping || b.Spec is not { } victim || victim.IsDarter || victim.IsFreeze
                        || !Field.Bubbles.Contains(b)) continue;
                    var p = CenterPx(b);
                    foreach (var dot in trail.Points)
                    {
                        double dx = p.X - dot.X, dy = p.Y - dot.Y;
                        if (dx * dx + dy * dy > TrailPopRadiusPx * TrailPopRadiusPx) continue;
                        if (b.AmbientTrigger) Pop(b); else PopChaos(b);
                        if (_chaosPopped == null) return;
                        break;
                    }
                }
            }
        }
    }
}
