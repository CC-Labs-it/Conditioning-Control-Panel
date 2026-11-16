using System;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal static partial class BubbleOverlay
{
    internal static Func<PixelPoint?> FrothPointer = () => X11Pointer.Read()?.At;

    // WPF BubbleService.Froth.cs:115-154. The stage and pointer use device pixels.
    internal static void StepFrothMotion(double dt)
    {
        if (!FrothOn) return;
        var stage = SuperStageHost.Stage;
        var cursor = FrothPointer();
        double pushGain = FrothOptions.PushFactor(CoreSettings.Current.FrothCursorPush);
        bool poolLive = stage.PoolLive && stage.Drags(PullKind.Bubble);
        foreach (var b in Field.Bubbles.ToArray())
        {
            if (!FrothOn) break;
            if (!b.FrothLive || b.FrothFuse >= 0) continue;
            var center = CenterPx(b);
            double scale = ScaleFor(b), radius = b.FrothRadius;
            var pull = stage.Pull(center.X, center.Y, FrothRules.Mass(radius, FrothFresh), PullKind.Bubble);
            if (pull.Eaten) { Pop(b); continue; }
            double ax = pull.Ax / scale, ay = pull.Ay / scale;
            if (!poolLive && cursor is { } point && pushGain > 0)
            {
                double dx = (center.X - point.X) / scale, dy = (center.Y - point.Y) / scale;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double push = FrothRules.NudgeAccel(distance - radius, radius, FrothFresh) * pushGain;
                if (push > 0 && distance > 1e-6) { ax += dx / distance * push; ay += dy / distance * push; }
            }
            b.FrothMove(dt, ax, ay, pull.Scale, pull.Inside);
        }
    }

    // WPF BubbleService.Froth.cs:270-281. No new surface or input hook is needed.
    private static void FrothSwept(double left, double top, double right, double bottom)
    {
        if (!FrothOn) return;
        for (int i = Field.Bubbles.Count - 1; i >= 0; i--)
        {
            if (!FrothOn) break;
            if (i >= Field.Bubbles.Count) continue;
            var b = Field.Bubbles[i];
            if (!b.FrothLive) continue;
            var center = CenterPx(b);
            if (FrothRules.UnderBox(center.X, center.Y, b.FrothRadius * ScaleFor(b), left, top, right, bottom)) Pop(b);
        }
    }
}
