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
        EStimBurstAt(center, 8, 440);
        Pulse(Color.FromRgb(0x7A, 0xE0, 0xFF), 0.25);
        _state.PushEvent(Loc.Get("chaos_body_buzz_spreads"));   // no icon in code (IconEmojiRatchet)
    }

    // WPF BubbleService.cs:1901-1914: the spanked rabbit sweep calls this for each victim.
    internal static void ElectrifiedVictim(Point victimPx)
    {
        if (_state?.ElectrifiedRabbits == true) EStimBurstAt(victimPx, 3, 620);
    }

    // WPF BubbleService.cs:445-485: free, non-chaining discharges shared with Body Buzz.
    private static void EStimBurstAt(Point fromPx, int maxArcs, double rangePx)
    {
        var targets = BubbleOverlay.Field.Bubbles.Where(b => !b.Popping && b.Spec != null && !b.Spec.IsDarter && !b.Spec.IsFreeze && !b.Spec.IsTease && !b.Spec.IsBrittle
                && !(b.Spec.IsChaperoneLive && b.Escort is { Popping: false } escort && BubbleOverlay.Field.Bubbles.Contains(escort)))
            .Select(b => (Bubble: b, Point: BubbleOverlay.CenterPx(b)))
            .Select(x => (x.Bubble, x.Point, Distance: Math.Pow(x.Point.X - fromPx.X, 2) + Math.Pow(x.Point.Y - fromPx.Y, 2)))
            .Where(x => x.Distance <= rangePx * rangePx).OrderBy(x => x.Distance).Take(maxArcs).ToArray();
        var bolts = targets.Select(x => (From: fromPx, To: x.Point)).ToArray();
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
            // Gentle Visuals: the Skia bolt re-jitters every frame (flicker); the overlay path fades.
            if (ChaosSkiaFxOverlay.Enabled && ConditioningControlPanel.Services.Safety.GentleVisuals.AllowFlicker)
                ChaosSkiaFxOverlay.Strike(bolts);
            else ChaosEStimOverlay.Strike(bolts.Select(x => (new PixelPoint((int)x.From.X, (int)x.From.Y), new PixelPoint((int)x.To.X, (int)x.To.Y))).ToArray());
            var now = DateTime.UtcNow;
            if ((now - _lastBodyBuzzZap).TotalMilliseconds >= 140)
            {
                _lastBodyBuzzZap = now;
                BubbleOverlay.PlayChaosCue("estim_zap", 0.45f);
            }
        }
    }

    internal static void ClearBodyBuzzHops()
    {
        foreach (var hop in BodyBuzzHops) hop.Stop();
        BodyBuzzHops.Clear();
    }
}
