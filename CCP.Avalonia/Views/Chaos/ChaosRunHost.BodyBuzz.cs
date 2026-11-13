using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

internal static partial class ChaosRunHost
{
    private static readonly List<DispatcherTimer> BodyBuzzHops = new();
    private static DateTime _lastBodyBuzzZap;
    internal static int BodyBuzzHopCount => BodyBuzzHops.Count;

    // WPF ChaosModeService.cs:1870-1878 and BubbleService.cs:453-503.
    internal static void BodyBuzz(AmbientBubble source)
    {
        if (_state == null || _state.EStimShockwaveChance <= 0 || Rng.NextDouble() >= _state.EStimShockwaveChance) return;
        var center = BubbleOverlay.CenterPx(source);
        if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Ripple(center, 440, 450, strong: false);
        else ChaosFieldFxOverlay.Ripple(center, 440, 450);
        var targets = BubbleOverlay.Field.Bubbles.Where(b => !b.Popping && b.Spec != null && !b.Spec.IsDarter && !b.Spec.IsFreeze && !b.Spec.IsTease && !b.Spec.IsBrittle
                && !(b.Spec.IsChaperoneLive && b.Escort is { Popping: false } escort && BubbleOverlay.Field.Bubbles.Contains(escort)))
            .Select(b => (Bubble: b, Point: BubbleOverlay.CenterPx(b)))
            .Select(x => (x.Bubble, x.Point, Distance: Math.Pow(x.Point.X - center.X, 2) + Math.Pow(x.Point.Y - center.Y, 2)))
            .Where(x => x.Distance <= 440 * 440).OrderBy(x => x.Distance).Take(8).ToArray();
        var bolts = targets.Select(x => (From: center, To: x.Point)).ToArray();
        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i].Bubble;
            var hop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70 * (i + 1)) };
            hop.Tick += (_, _) =>
            {
                hop.Stop();
                BodyBuzzHops.Remove(hop);
                if (BubbleOverlay.Field.Bubbles.Contains(target) && !target.Popping) BubbleOverlay.PopChaos(target);
            };
            BodyBuzzHops.Add(hop);
            hop.Start();
        }
        if (bolts.Length > 0)
        {
            if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Strike(bolts);
            else ChaosEStimOverlay.Strike(bolts.Select(x => (new PixelPoint((int)x.From.X, (int)x.From.Y), new PixelPoint((int)x.To.X, (int)x.To.Y))).ToArray());
            var now = DateTime.UtcNow;
            if ((now - _lastBodyBuzzZap).TotalMilliseconds >= 140)
            {
                _lastBodyBuzzZap = now;
                BubbleOverlay.PlayChaosCue("estim_zap", 0.45f);
            }
        }
        Pulse(Color.FromRgb(0x7A, 0xE0, 0xFF), 0.25);
        _state.PushEvent($"⚡ {Loc.Get("chaos_body_buzz_spreads")}");
    }

    internal static void ClearBodyBuzzHops()
    {
        foreach (var hop in BodyBuzzHops) hop.Stop();
        BodyBuzzHops.Clear();
    }
}
